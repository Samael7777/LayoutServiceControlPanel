using System.Collections.Immutable;

namespace ControlPanel.Core.Services;

/// <summary>
/// Atomic view of both layout lists. Both lists are immutable and always consistent
/// with each other (they were taken under the same lock).
/// </summary>
/// <param name="Available">System layouts that are not in the cycled list, in system order.</param>
/// <param name="Cycled">Layouts used for cyclic switching, in switching order.</param>
public sealed record CycledLayoutsSnapshot(IReadOnlyList<uint> Available, IReadOnlyList<uint> Cycled);

/// <summary>
/// Maintains the list of layouts used for cyclic switching, built on top of the
/// (read-only) list of layouts installed in the system.
/// </summary>
/// <remarks>
/// <para>
/// Two lists are exposed: <see cref="Available"/> (system layouts minus cycled ones) and
/// <see cref="Cycled"/>. Both are immutable snapshots; the only way to change them is
/// through the methods of this service. Every successful change of the cycled list is
/// persisted to the settings immediately.
/// </para>
/// <para>
/// Invariants that always hold: the cycled list contains no duplicates, and every cycled
/// layout is also present in the system list. <see cref="Available"/> is always exactly
/// "system layouts not in the cycled list".
/// </para>
/// <para>
/// The service does not watch the system by itself. An external component that detects
/// changes of the installed layouts must call <see cref="Update"/>.
/// </para>
/// <para>This class is thread-safe.</para>
/// </remarks>
public sealed class CycledLayoutsService
{
    private readonly object _sync = new();
    private readonly Func<uint[]> _getUserLayouts;
    private readonly Action<IReadOnlyCollection<uint>> _saveCycled;

    private ImmutableArray<uint> _system;
    private ImmutableArray<uint> _cycled;
    private ImmutableArray<uint> _available;

    /// <summary>
    /// Raised after the content of either list has changed (including changes caused by
    /// <see cref="Update"/>). Raised synchronously on the thread that made the change,
    /// outside the internal lock, so handlers may safely call back into the service.
    /// </summary>
    public event EventHandler? Changed;

    /// <param name="getUserLayouts">Returns the layouts currently installed in the system (GetUserLayouts).</param>
    /// <param name="loadCycled">
    /// Loads the persisted cycled layouts. Defaults to <see cref="Settings.CycledLayouts"/>.
    /// </param>
    /// <param name="saveCycled">
    /// Persists the cycled layouts. Defaults to assigning <see cref="Settings.CycledLayouts"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">The system reported no layouts.</exception>
    public CycledLayoutsService(
        Func<uint[]> getUserLayouts,
        Func<IReadOnlyCollection<uint>>? loadCycled = null,
        Action<IReadOnlyCollection<uint>>? saveCycled = null)
    {
        if (getUserLayouts is null)
            throw new ArgumentNullException(nameof(getUserLayouts));

        _getUserLayouts = getUserLayouts;
        _saveCycled = saveCycled ?? (value => Settings.CycledLayouts = value);

        var load = loadCycled ?? (() => Settings.CycledLayouts);

        var system = ReadSystemLayouts();
        if (system.IsEmpty)
            throw new InvalidOperationException("The system reported no keyboard layouts.");

        var stored = load();
        var cycled = Sanitize(stored, system);

        _system = system;
        _cycled = cycled;
        _available = ComputeAvailable(system, cycled);

        // Persisted data was stale (removed layouts, duplicates): write the cleaned list back.
        if (stored.Count != cycled.Length)
            _saveCycled(cycled);
    }

    #region Lists

    /// <summary>
    /// System layouts that are not in the cycled list, in the order reported by the system.
    /// The returned list is an immutable snapshot.
    /// </summary>
    public IReadOnlyList<uint> Available
    {
        get { lock (_sync) return _available; }
    }

    /// <summary>
    /// Layouts used for cyclic switching, in switching order.
    /// The returned list is an immutable snapshot.
    /// </summary>
    public IReadOnlyList<uint> Cycled
    {
        get { lock (_sync) return _cycled; }
    }

    /// <summary>
    /// Returns both lists taken at the same moment. Prefer this over reading
    /// <see cref="Available"/> and <see cref="Cycled"/> separately when both are needed,
    /// because a change may happen between two property reads.
    /// </summary>
    public CycledLayoutsSnapshot GetSnapshot()
    {
        lock (_sync) return new CycledLayoutsSnapshot(_available, _cycled);
    }

    #endregion

    #region Commands

    /// <summary>Appends an available layout to the end of the cycled list.</summary>
    /// <returns>
    /// True if added; false if the layout is not available (unknown to the system or
    /// already cycled).
    /// </returns>
    public bool Add(uint layout)
    {
        lock (_sync)
        {
            if (!_available.Contains(layout))
                return false;

            Apply(_cycled.Add(layout));
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Removes a layout from the cycled list; it becomes available again.</summary>
    /// <returns>True if removed; false if the layout was not in the cycled list.</returns>
    public bool Remove(uint layout)
    {
        lock (_sync)
        {
            var index = _cycled.IndexOf(layout);
            if (index < 0)
                return false;

            Apply(_cycled.RemoveAt(index));
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Moves a cycled layout one position towards the beginning of the list.</summary>
    /// <returns>True if moved; false if the layout is not cycled or is already first.</returns>
    public bool MoveUp(uint layout) => Move(layout, -1);

    /// <summary>Moves a cycled layout one position towards the end of the list.</summary>
    /// <returns>True if moved; false if the layout is not cycled or is already last.</returns>
    public bool MoveDown(uint layout) => Move(layout, +1);

    /// <summary>
    /// Re-reads the system layouts and reconciles the service with them: cycled layouts that
    /// no longer exist in the system are removed (and the cleaned list is persisted), and the
    /// available list is rebuilt. The order of the remaining cycled layouts is preserved.
    /// </summary>
    /// <remarks>
    /// An empty result from the system is treated as a transient failure and ignored,
    /// so a glitch can never wipe the user's cycled layouts.
    /// </remarks>
    /// <returns>True if either list changed; otherwise false.</returns>
    public bool Update()
    {
        lock (_sync)
        {
            // Read inside the lock so concurrent Update calls cannot commit stale data
            // over fresher data.
            var system = ReadSystemLayouts();
            if (system.IsEmpty)
                return false;

            var cycled = Sanitize(_cycled, system);

            var systemChanged = !Enumerable.SequenceEqual(system, _system);
            var cycledChanged = cycled.Length != _cycled.Length;

            if (!systemChanged && !cycledChanged)
                return false;

            // Persist first: if saving throws, the in-memory state stays unchanged.
            if (cycledChanged)
                _saveCycled(cycled);

            _system = system;
            _cycled = cycled;
            _available = ComputeAvailable(system, cycled);
        }

        RaiseChanged();
        return true;
    }

    #endregion

    #region Internals

    private bool Move(uint layout, int delta)
    {
        lock (_sync)
        {
            var from = _cycled.IndexOf(layout);
            if (from < 0)
                return false;

            var to = from + delta;
            if (to < 0 || to >= _cycled.Length)
                return false;

            var builder = _cycled.ToBuilder();
            (builder[from], builder[to]) = (builder[to], builder[from]);

            Apply(builder.ToImmutable());
        }

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Persists and publishes a new cycled list, then rebuilds the available list.
    /// Must be called while holding <see cref="_sync"/>. If persisting throws,
    /// the in-memory state is left unchanged.
    /// </summary>
    private void Apply(ImmutableArray<uint> cycled)
    {
        _saveCycled(cycled);

        _cycled = cycled;
        _available = ComputeAvailable(_system, cycled);
    }

    /// <summary>Reads system layouts, dropping duplicates while preserving order.</summary>
    private ImmutableArray<uint> ReadSystemLayouts()
    {
        var raw = _getUserLayouts();
        return raw is null or { Length: 0 }
            ? ImmutableArray<uint>.Empty
            : raw.Distinct().ToImmutableArray();
    }

    /// <summary>Keeps only layouts present in the system, drops duplicates, preserves order.</summary>
    private static ImmutableArray<uint> Sanitize(IEnumerable<uint> source, ImmutableArray<uint> system)
    {
        var systemSet = new HashSet<uint>(system);
        return source.Where(systemSet.Contains).Distinct().ToImmutableArray();
    }

    private static ImmutableArray<uint> ComputeAvailable(ImmutableArray<uint> system, ImmutableArray<uint> cycled)
    {
        var cycledSet = new HashSet<uint>(cycled);
        return system.Where(layout => !cycledSet.Contains(layout)).ToImmutableArray();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    #endregion
}