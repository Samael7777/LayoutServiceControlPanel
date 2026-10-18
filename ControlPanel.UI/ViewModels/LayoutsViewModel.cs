using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlPanel.Core.Services;
using ControlPanel.UI.Models;

namespace ControlPanel.UI.ViewModels;

/// <summary>
/// View model of the "Layouts" zone: two lists (available and cycled) and the commands
/// that map one-to-one to <see cref="CycledLayoutsService"/>.
/// </summary>
public sealed partial class LayoutsViewModel : ObservableObject, IDisposable
{
    private readonly CycledLayoutsService _service;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<uint, LayoutItem> _items = [];
    private bool _disposed;

    public LayoutsViewModel(CycledLayoutsService service)
    {
        _service = service;
        Reload();
        service.Changed += OnServiceChanged;
    }

    public ObservableCollection<LayoutItem> Available { get; } = [];

    public ObservableCollection<LayoutItem> Cycled { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private LayoutItem? _selectedAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private LayoutItem? _selectedCycled;

    [ObservableProperty]
    private bool _isAvailableEmpty;

    [ObservableProperty]
    private bool _isCycledEmpty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isErrorOpen;

    #region Commands

    private bool CanAdd() => SelectedAvailable is not null;

    private bool CanRemove() => SelectedCycled is not null;

    private bool CanMoveUp() => SelectedCycled is { } item && Cycled.IndexOf(item) > 0;

    private bool CanMoveDown() => SelectedCycled is { } item && Cycled.IndexOf(item) is var i && i >= 0 && i < Cycled.Count - 1;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        if (SelectedAvailable is not { } item)
            return;

        var index = Available.IndexOf(item);

        if (!Execute(() => _service.Add(item.Id)))
            return;

        // Select the added layout in the cycled list and the neighbor in the available one,
        // so several layouts can be added in a row.
        SelectedCycled = Cycled.FirstOrDefault(x => x.Id == item.Id);
        SelectedAvailable = Available.Count > 0 ? Available[Math.Min(index, Available.Count - 1)] : null;
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedCycled is not { } item)
            return;

        var index = Cycled.IndexOf(item);

        if (!Execute(() => _service.Remove(item.Id)))
            return;

        SelectedAvailable = Available.FirstOrDefault(x => x.Id == item.Id);
        SelectedCycled = Cycled.Count > 0 ? Cycled[Math.Min(index, Cycled.Count - 1)] : null;
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (SelectedCycled is { } item)
            Execute(() => _service.MoveUp(item.Id));
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (SelectedCycled is { } item)
            Execute(() => _service.MoveDown(item.Id));
    }

    /// <summary>Re-reads the system layouts (normally done by an external watcher).</summary>
    [RelayCommand]
    private void Refresh() => Execute(() => _service.Update());

    /// <summary>Runs a service operation, reloads the lists and reports failures (e.g. settings write errors).</summary>
    private bool Execute(Func<bool> operation)
    {
        var changed = false;

        try
        {
            changed = operation();
            IsErrorOpen = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            IsErrorOpen = true;
        }

        Reload();
        return changed;
    }

    #endregion

    #region Lists

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        if (_dispatcher.CheckAccess())
            Reload();
        else
            _dispatcher.InvokeAsync(Reload);
    }

    /// <summary>Synchronizes the observable collections with the service and keeps the selection.</summary>
    private void Reload()
    {
        var snapshot = _service.GetSnapshot();
        var selectedAvailable = SelectedAvailable?.Id;
        var selectedCycled = SelectedCycled?.Id;

        Sync(Available, [.. snapshot.Available.Select(GetItem)]);
        Sync(Cycled, [.. snapshot.Cycled.Select(GetItem)]);

        SelectedAvailable = selectedAvailable is { } a ? Available.FirstOrDefault(x => x.Id == a) : null;
        SelectedCycled = selectedCycled is { } c ? Cycled.FirstOrDefault(x => x.Id == c) : null;

        IsAvailableEmpty = Available.Count == 0;
        IsCycledEmpty = Cycled.Count == 0;

        // Position-dependent commands must be re-evaluated even if the selection did not change.
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private LayoutItem GetItem(uint id)
    {
        if (!_items.TryGetValue(id, out var item))
            _items[id] = item = LayoutItem.Create(id);

        return item;
    }

    /// <summary>
    /// Makes <paramref name="target"/> equal to <paramref name="source"/> using minimal
    /// insert / move / remove operations, so unchanged items keep their selection state.
    /// </summary>
    private static void Sync(ObservableCollection<LayoutItem> target, IReadOnlyList<LayoutItem> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i < target.Count && target[i].Id == source[i].Id)
                continue;

            var existing = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (target[j].Id == source[i].Id)
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
                target.Move(existing, i);
            else
                target.Insert(i, source[i]);
        }

        while (target.Count > source.Count)
            target.RemoveAt(target.Count - 1);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _service.Changed -= OnServiceChanged;
    }
}
