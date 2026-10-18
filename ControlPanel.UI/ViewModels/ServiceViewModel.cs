using System.Security.Principal;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlPanel.Core.Services;
using TaskState = Microsoft.Win32.TaskScheduler.TaskState;

namespace ControlPanel.UI.ViewModels;

/// <summary>Coarse status used by the view to pick the status indicator color.</summary>
public enum StatusKind
{
    Neutral,
    Success,
    Caution
}

/// <summary>
/// View model of the "Service" zone: shows the state of the autorun task and
/// exposes create / delete / start / stop commands.
/// </summary>
public sealed partial class ServiceViewModel : ObservableObject, IDisposable
{
    private readonly SwitcherTaskService _service;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _disposed;

    public ServiceViewModel(SwitcherTaskService service)
    {
        _service = service;
        IsNotElevated = !IsElevated();

        Snapshot = service.GetStatus();
        service.TaskStatusChanged += OnTaskStatusChanged;
    }

    #region State

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(StatusKind),
        nameof(StatusText),
        nameof(StatusDescription),
        nameof(CreateText),
        nameof(IsConfigWarningOpen),
        nameof(LastRunText),
        nameof(LastResultText))]
    [NotifyCanExecuteChangedFor(
        nameof(CreateCommand),
        nameof(DeleteCommand),
        nameof(StartCommand),
        nameof(StopCommand))]
    private SwitcherTaskSnapshot _snapshot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(CreateCommand),
        nameof(DeleteCommand),
        nameof(StartCommand),
        nameof(StopCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isErrorOpen;

    /// <summary>True when the process lacks administrator rights (task changes will fail).</summary>
    public bool IsNotElevated { get; }

    public StatusKind StatusKind => Snapshot switch
    {
        { Exists: false } => StatusKind.Neutral,
        { IsEnabled: false } => StatusKind.Caution,
        { State: TaskState.Running } => StatusKind.Success,
        _ => StatusKind.Neutral
    };

    public string StatusText => Snapshot switch
    {
        { Exists: false } => "Not installed",
        { IsEnabled: false } => "Disabled",
        { State: TaskState.Running } => "Running",
        { State: TaskState.Queued } => "Starting",
        { State: TaskState.Ready } => "Stopped",
        var s => s.State.ToString()
    };

    public string StatusDescription => Snapshot switch
    {
        { Exists: false } => "The autorun task is not registered. Create it to start the switcher at sign-in.",
        { IsEnabled: false } => "The task exists but is disabled in Task Scheduler.",
        { State: TaskState.Running, RunningInstances: > 1 } s =>
            $"{s.RunningInstances} running instances, one per signed-in user.",
        { State: TaskState.Running } => "One running instance.",
        _ => "Not running. Starts automatically when any user signs in."
    };

    /// <summary>"Repair" when a task exists but needs fixing, otherwise "Create".</summary>
    public string CreateText => Snapshot.Exists ? "Repair" : "Create";

    public bool IsConfigWarningOpen => Snapshot.Exists && !Snapshot.IsConfigValid;

    public string LastRunText =>
        !Snapshot.Exists || Snapshot.LastRunTime.Year < 2000
            ? "Never"
            : Snapshot.LastRunTime.ToString("g");

    public string LastResultText
    {
        get
        {
            if (!Snapshot.Exists || Snapshot.LastRunTime.Year < 2000)
                return "-";

            var code = unchecked((uint)Snapshot.LastTaskResult);
            return code == 0 ? "0x00000000 (success)" : $"0x{code:X8}";
        }
    }

    #endregion

    #region Commands

    private bool CanCreate() => !IsBusy && (!Snapshot.Exists || !Snapshot.IsConfigValid);

    private bool CanDelete() => !IsBusy && Snapshot.Exists;

    private bool CanStart() => !IsBusy && Snapshot.Exists && Snapshot.IsEnabled && !Snapshot.IsRunning;

    private bool CanStop() => !IsBusy && Snapshot.IsRunning;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() => RunAsync(() => _service.Create());

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => RunAsync(() => _service.Delete());

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => RunAsync(() => _service.Start());

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => RunAsync(() => _service.Stop());

    /// <summary>Runs a blocking service operation off the UI thread and reports failures.</summary>
    private async Task RunAsync(Action operation)
    {
        IsBusy = true;
        IsErrorOpen = false;

        try
        {
            await Task.Run(operation);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is UnauthorizedAccessException
                ? "Access denied. Run the application as administrator."
                : ex.Message;
            IsErrorOpen = true;
        }

        IsBusy = false;

        try
        {
            // The service also raises TaskStatusChanged, this just makes the result deterministic.
            Snapshot = await Task.Run(_service.GetStatus);
        }
        catch
        {
            // Keep the last known snapshot; the next poll will refresh it.
        }
    }

    #endregion

    #region Events

    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (_dispatcher.CheckAccess())
            Snapshot = e.Current;
        else
            _dispatcher.InvokeAsync(() => Snapshot = e.Current);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _service.TaskStatusChanged -= OnTaskStatusChanged;
    }

    #endregion

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
