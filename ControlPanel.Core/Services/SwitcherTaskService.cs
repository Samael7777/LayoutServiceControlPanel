using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Win32.TaskScheduler;
using SchedTask = Microsoft.Win32.TaskScheduler.Task;

// ReSharper disable UnusedMember.Global

namespace ControlPanel.Core.Services;

/// <summary>
/// Point-in-time snapshot of the scheduled task. Compared by value, so the change
/// event is raised only when something actually changed.
/// </summary>
/// <param name="Exists">The task is registered in Task Scheduler.</param>
/// <param name="IsEnabled">The task is enabled.</param>
/// <param name="IsConfigValid">
/// The task matches the expected configuration: enabled, logon trigger for any user,
/// runs for the Users group with the highest privileges, and points to this executable.
/// </param>
/// <param name="State">Aggregate state of the task (Running if at least one instance is running).</param>
/// <param name="RunningInstances">
/// Number of running instances (one per logged-on user session). Always at least 1 while
/// <paramref name="State"/> is Running. Non-administrators may not see other users' instances.
/// </param>
/// <param name="LastRunTime">Time of the last run, or <see cref="DateTime.MinValue"/> if never run.</param>
/// <param name="LastTaskResult">Result code of the last run.</param>
public readonly record struct SwitcherTaskSnapshot(
    bool Exists,
    bool IsEnabled,
    bool IsConfigValid,
    TaskState State,
    int RunningInstances,
    DateTime LastRunTime,
    int LastTaskResult)
{
    /// <summary>Snapshot representing a task that does not exist.</summary>
    public static SwitcherTaskSnapshot Missing { get; } =
        new(false, false, false, TaskState.Unknown, 0, DateTime.MinValue, 0);

    /// <summary>At least one instance of the task is running.</summary>
    public bool IsRunning => State == TaskState.Running;
}

/// <summary>Event data for <see cref="SwitcherTaskService.TaskStatusChanged"/>.</summary>
public sealed class TaskStatusChangedEventArgs(SwitcherTaskSnapshot previous, SwitcherTaskSnapshot current)
    : EventArgs
{
    /// <summary>Snapshot before the change.</summary>
    public SwitcherTaskSnapshot Previous { get; } = previous;

    /// <summary>Snapshot after the change.</summary>
    public SwitcherTaskSnapshot Current { get; } = current;
}

/// <summary>
/// Manages the autorun task of the keyboard layout switcher in Windows Task Scheduler:
/// create, delete, start, stop, status query, and a state change event.
/// </summary>
/// <remarks>
/// <para>
/// <b>Multi-user behavior.</b> The task is registered for the built-in Users group with a
/// logon trigger that is not bound to a specific account. It therefore starts at logon of
/// every user, and instances for several simultaneously logged-on users (fast user switching,
/// RDP sessions) run in parallel, each in its own user session.
/// </para>
/// <para>
/// <b>Requirements.</b> Creating, deleting, and stopping the task requires administrator
/// rights. The switcher executable must be readable by all users that should run it
/// (do not place it inside a single user's profile). "Highest privileges" is honored only
/// for users who are members of the Administrators group; other users get a standard token.
/// The switcher itself should guard against duplicate launches within one session.
/// </para>
/// <para>
/// <b>Threading.</b> Task Scheduler COM objects must always be used from the thread (apartment)
/// that created them: touching them from another apartment fails with "Interface not registered"
/// (0x80040155) as soon as an interface without a registered marshaler, such as
/// <c>ITaskDefinition</c>, crosses the boundary. For that reason the service owns a dedicated
/// background COM thread; the <see cref="TaskService"/> lives only on that thread and every
/// operation is executed there. Public members can safely be called from any thread; they block
/// until the COM thread has finished the work.
/// </para>
/// <para>
/// <b>Change notification.</b> Task Scheduler offers no push notifications unless the
/// TaskScheduler/Operational event log is enabled (it is disabled by default), so changes
/// are detected by lightweight polling and snapshot comparison. Every operation performed
/// through this class also refreshes the snapshot immediately.
/// </para>
/// </remarks>
public sealed class SwitcherTaskService : IDisposable
{
    private const string TaskPath = $@"\{AppConstants.SwitcherTaskName}";

    /// <summary>Well-known SID of BUILTIN\Users (locale-independent).</summary>
    private const string UsersGroupSid = "S-1-5-32-545";

    private static readonly TimeSpan s_DefaultPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_MinPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly object _sync = new();                 // guards _timer, _pollInterval, _disposed
    private readonly string _switcherPath;
    private readonly SynchronizationContext? _syncContext;

    // Dedicated COM thread. _service and _last are touched ONLY on this thread.
    private readonly BlockingCollection<System.Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private TaskService _service = null!;
    private SwitcherTaskSnapshot _last;
    private Exception? _startupError;

    private Timer? _timer;
    private TimeSpan _pollInterval;
    private volatile bool _disposed;

    /// <summary>
    /// Raised when the task state changes (existence, enabled flag, configuration validity,
    /// <see cref="TaskState"/>, running instance count, last run time or result).
    /// If the service was created on a thread with a <see cref="SynchronizationContext"/>
    /// (e.g. the UI thread), the event is marshaled to that context; otherwise it is raised
    /// on a thread-pool thread.
    /// </summary>
    public event EventHandler<TaskStatusChangedEventArgs>? TaskStatusChanged;

    /// <param name="switcherPath">Path to the switcher executable.</param>
    /// <param name="pollInterval">Polling interval for change detection. Defaults to 1 second.</param>
    /// <param name="autoStartMonitoring">Start change monitoring immediately.</param>
    public SwitcherTaskService(
        string switcherPath,
        TimeSpan? pollInterval = null,
        bool autoStartMonitoring = true)
    {
        if (string.IsNullOrWhiteSpace(switcherPath))
            throw new ArgumentException(nameof(switcherPath));

        _switcherPath = Path.GetFullPath(switcherPath);
        _pollInterval = ValidateInterval(pollInterval ?? s_DefaultPollInterval);
        _syncContext = SynchronizationContext.Current;

        // The COM thread is MTA (default for new threads) and has no message pump requirements.
        _thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "SwitcherTaskService COM thread"
        };
        _thread.Start();

        _ready.Wait();
        if (_startupError is not null)
        {
            _thread.Join();
            ExceptionDispatchInfo.Capture(_startupError).Throw();
        }

        try
        {
            _last = Invoke(ReadSnapshotCore);
        }
        catch
        {
            Shutdown();
            throw;
        }

        if (autoStartMonitoring)
            StartMonitoring();
    }

    #region Status

    /// <summary>
    /// Gets or sets whether autorun is enabled. The getter returns true only if the task
    /// exists and its configuration is valid; the setter creates or deletes the task.
    /// </summary>
    public bool IsAutorunEnabled
    {
        get => Refresh().IsConfigValid;
        set
        {
            if (value)
                Create();
            else
                Delete();
        }
    }

    /// <summary>Gets whether the task is registered in Task Scheduler.</summary>
    public bool Exists => Refresh().Exists;

    /// <summary>Gets the current task state (<see cref="TaskState.Unknown"/> if the task does not exist).</summary>
    public TaskState GetState() => Refresh().State;

    /// <summary>
    /// Reads a fresh snapshot of the task. If it differs from the previous one,
    /// <see cref="TaskStatusChanged"/> is raised.
    /// </summary>
    public SwitcherTaskSnapshot GetStatus() => Refresh();

    #endregion

    #region Commands

    /// <summary>
    /// Creates the task, or brings an existing one to the expected configuration
    /// (safe to call repeatedly; also repairs tasks created by older versions,
    /// e.g. ones bound to a single user).
    /// </summary>
    /// <param name="startNow">Start the task right after registration.</param>
    /// <exception cref="FileNotFoundException">The switcher executable does not exist.</exception>
    public void Create(bool startNow = false)
    {
        if (!File.Exists(_switcherPath))
            throw new FileNotFoundException("Switcher executable not found.", _switcherPath);

        Invoke(() =>
        {
            using var td = _service.NewTask();

            td.RegistrationInfo.Description = AppConstants.SwitcherTaskDesc;

            // No UserId: the trigger fires at logon of ANY user.
            td.Triggers.Add(new LogonTrigger());

            td.Actions.Add(new ExecAction(_switcherPath, null, Path.GetDirectoryName(_switcherPath)));

            // Run in the context of whichever member of the Users group logs on.
            td.Principal.GroupId = UsersGroupSid;
            td.Principal.LogonType = TaskLogonType.Group;
            td.Principal.RunLevel = TaskRunLevel.Highest;

            var s = td.Settings;
            s.Enabled = true;
            s.DisallowStartIfOnBatteries = false;
            s.StopIfGoingOnBatteries = false;
            s.ExecutionTimeLimit = TimeSpan.Zero;                // no time limit
            s.MultipleInstances = TaskInstancesPolicy.Parallel;  // one instance per user session
            s.AllowHardTerminate = true;
            s.StartWhenAvailable = true;
            s.Priority = ProcessPriorityClass.Normal;            // task default is BelowNormal (7)

            _service.RootFolder.RegisterTaskDefinition(
                TaskPath,
                td,
                TaskCreation.CreateOrUpdate,
                null,   // userId: not used with a group principal
                null,   // password
                TaskLogonType.Group);

            if (startNow)
                RunCore(allowParallel: false);
        });

        Refresh();
    }

    /// <summary>Deletes the task. Does nothing if it does not exist.</summary>
    /// <param name="stopIfRunning">Stop all running instances before deleting.</param>
    public void Delete(bool stopIfRunning = true)
    {
        Invoke(() =>
        {
            if (stopIfRunning)
                StopCore();

            _service.RootFolder.DeleteTask(TaskPath, exceptionOnNotExists: false);
        });

        Refresh();
    }

    /// <summary>
    /// Starts the task manually. The instance runs in the session of the calling user.
    /// By default, nothing happens if an instance is already running.
    /// </summary>
    /// <param name="allowParallel">
    /// Start a new instance even if one is already running (for example, one belonging
    /// to another logged-on user).
    /// </param>
    /// <exception cref="InvalidOperationException">The task does not exist or is disabled.</exception>
    public void Start(bool allowParallel = false)
    {
        Invoke(() => RunCore(allowParallel));

        Refresh();
    }

    /// <summary>
    /// Stops all running instances of the task (for every user). Does nothing if the task
    /// does not exist or is not running.
    /// </summary>
    public void Stop()
    {
        Invoke(StopCore);

        Refresh();
    }

    #endregion

    #region Monitoring

    /// <summary>Gets or sets the polling interval (minimum 100 ms). Changes apply from the next tick.</summary>
    public TimeSpan PollInterval
    {
        get { lock (_sync) return _pollInterval; }
        set { lock (_sync) _pollInterval = ValidateInterval(value); }
    }

    /// <summary>Gets whether change monitoring is active.</summary>
    public bool IsMonitoring
    {
        get { lock (_sync) return _timer is not null; }
    }

    /// <summary>Starts polling for task changes. Does nothing if already started.</summary>
    public void StartMonitoring()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_timer is not null) return;

            // One-shot timer re-armed after each poll, so ticks never overlap.
            _timer = new Timer(OnPoll, null, _pollInterval, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Stops polling for task changes.</summary>
    public void StopMonitoring()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnPoll(object? _)
    {
        try
        {
            Refresh();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            // The Task Scheduler service may be temporarily unavailable; keep polling.
            Debug.WriteLine($"[SwitcherTaskService] Poll failed: {ex}");
        }

        lock (_sync)
        {
            if (!_disposed)
                _timer?.Change(_pollInterval, Timeout.InfiniteTimeSpan);
        }
    }

    #endregion

    #region Internals

    private SwitcherTaskSnapshot Refresh()
    {
        // Read and swap happen on the COM thread, so snapshots are strictly ordered.
        var (previous, current) = Invoke(() =>
        {
            var snapshot = ReadSnapshotCore();
            var old = _last;
            _last = snapshot;
            return (old, snapshot);
        });

        // Raised on the caller's thread, outside any lock, so handlers may call back into the service.
        if (previous != current)
            RaiseChanged(previous, current);

        return current;
    }

    // Methods suffixed with "Core" must be called on the COM thread only (inside Invoke).

    private SwitcherTaskSnapshot ReadSnapshotCore()
    {
        using var task = _service.GetTask(TaskPath);
        if (task is null)
            return SwitcherTaskSnapshot.Missing;

        var state = task.State;

        return new SwitcherTaskSnapshot(
            Exists: true,
            IsEnabled: task.Enabled,
            IsConfigValid: IsConfigValid(task),
            State: state,
            RunningInstances: state == TaskState.Running ? CountRunningInstances() : 0,
            LastRunTime: task.LastRunTime,
            LastTaskResult: task.LastTaskResult);
    }

    /// <summary>
    /// Counts running instances of this task. Enumerating running tasks is relatively costly,
    /// so it is done only when the task is known to be running.
    /// </summary>
    private int CountRunningInstances()
    {
        try
        {
            var count = 0;

            foreach (var running in _service.GetRunningTasks())
            {
                using (running)
                {
                    if (string.Equals(running.Path, TaskPath, StringComparison.OrdinalIgnoreCase))
                        count++;
                }
            }

            // The task is known to be running; instances of other users may be invisible
            // without administrator rights, so never report less than one.
            return Math.Max(count, 1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwitcherTaskService] Failed to count instances: {ex}");
            return 1;
        }
    }

    private void RunCore(bool allowParallel)
    {
        using var task = _service.GetTask(TaskPath)
                         ?? throw new InvalidOperationException("Switcher task does not exist.");

        if (!task.Enabled)
            throw new InvalidOperationException("Switcher task is disabled.");

        if (!allowParallel && task.State is TaskState.Running or TaskState.Queued)
            return;

        task.Run();
    }

    private void StopCore()
    {
        using var task = _service.GetTask(TaskPath);
        if (task is null) return;

        if (task.State is TaskState.Running or TaskState.Queued)
            task.Stop();
    }

    /// <summary>
    /// The task is valid if it is enabled, has an enabled logon trigger for any user,
    /// runs for the Users group with the highest privileges, and its action points to
    /// this executable (not to a stale path left over after the application was moved).
    /// </summary>
    private bool IsConfigValid(SchedTask task)
    {
        if (!task.Enabled) return false;

        var def = task.Definition;

        if (def.Principal.RunLevel != TaskRunLevel.Highest)
            return false;

        if (def.Principal.LogonType != TaskLogonType.Group)
            return false;

        // A trigger bound to a specific user means "that user only", which is not what we want.
        var hasAnyUserLogonTrigger = def.Triggers
            .OfType<LogonTrigger>()
            .Any(t => t.Enabled && string.IsNullOrEmpty(t.UserId));

        if (!hasAnyUserLogonTrigger)
            return false;

        return def.Actions
            .OfType<ExecAction>()
            .Any(a => PathsEqual(a.Path, _switcherPath));
    }

    private static bool PathsEqual(string? actual, string expectedFullPath)
    {
        if (string.IsNullOrWhiteSpace(actual)) 
            return false;

        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(actual!.Trim().Trim('"')));
            return string.Equals(full, expectedFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void RaiseChanged(SwitcherTaskSnapshot previous, SwitcherTaskSnapshot current)
    {
        var handler = TaskStatusChanged;
        if (handler is null) return;

        var args = new TaskStatusChangedEventArgs(previous, current);

        if (_syncContext is null)
            handler(this, args);
        else
            _syncContext.Post(_ => handler(this, args), null);
    }

    private static TimeSpan ValidateInterval(TimeSpan value)
    {
        return value < s_MinPollInterval 
            ? throw new ArgumentOutOfRangeException(nameof(value), "Poll interval must be at least 100 ms.") 
            : value;
    }

    private void ThrowIfDisposed()
    {
        if(_disposed)
            throw new ObjectDisposedException(nameof(SwitcherTaskService));
    }

    #endregion

    #region COM thread

    private void WorkerLoop()
    {
        try
        {
            // A dedicated instance on purpose: TaskService.Instance is a shared singleton
            // bound to whichever thread first touched it.
            _service = new TaskService();
        }
        catch (Exception ex)
        {
            _startupError = ex;
            _ready.Set();
            return;
        }

        _ready.Set();

        try
        {
            foreach (var work in _queue.GetConsumingEnumerable())
                work();
        }
        finally
        {
            _service.Dispose();
        }
    }

    /// <summary>Runs <paramref name="func"/> on the COM thread and returns its result (or rethrows its exception).</summary>
    private T Invoke<T>(Func<T> func)
    {
        ThrowIfDisposed();

        // Already on the COM thread (re-entrant call): just run it.
        if (Thread.CurrentThread == _thread)
            return func();

        T result = default!;
        ExceptionDispatchInfo? error = null;

        using var done = new ManualResetEventSlim();
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    error = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    // ReSharper disable once AccessToDisposedClosure
                    done.Set();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The queue was completed by Dispose.
            throw new ObjectDisposedException(nameof(SwitcherTaskService));
        }
        
        done.Wait();
        error?.Throw();
        return result;
    }

    private void Invoke(System.Action action) =>
        Invoke<object?>(() =>
        {
            action();
            return null;
        });

    /// <summary>Stops the COM thread after it has drained the queue.</summary>
    private void Shutdown()
    {
        _queue.CompleteAdding();

        if (Thread.CurrentThread == _thread || !_thread.Join(TimeSpan.FromSeconds(5))) 
            return;

        _queue.Dispose();
        _ready.Dispose();
    }

    #endregion

    #region Dispose

    // No finalizer: the class holds no unmanaged resources directly.

    /// <summary>Stops monitoring and releases the Task Scheduler connection.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            _timer?.Dispose();
            _timer = null;
        }

        // The TaskService itself is disposed by the COM thread when it exits.
        Shutdown();
    }

    #endregion
}