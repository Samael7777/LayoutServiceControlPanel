// NOTE: the VK_KEY enum is expected in the ControlPanel.Core namespace.
// If it is declared in another namespace, add the corresponding using directive.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlPanel.Core;
using ControlPanel.UI.Models;

namespace ControlPanel.UI.ViewModels;

/// <summary>
/// View model of the "Hotkey" zone: shows the current combination, clears it and
/// appends keys picked from the VK_KEY list. Every change is saved to settings immediately.
/// </summary>
public sealed partial class HotkeyViewModel : ObservableObject
{
    private static readonly IReadOnlyList<KeyItem> AllKeys = BuildAllKeys();
    private static readonly Dictionary<uint, KeyItem> KeysByCode = AllKeys.ToDictionary(k => k.Code);

    private uint[] _combo = [];

    public HotkeyViewModel()
    {
        try
        {
            _combo = [.. Settings.Hotkey];
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }

        Rebuild();
    }

    /// <summary>Keys of the current hotkey, in order.</summary>
    [ObservableProperty]
    private IReadOnlyList<HotkeyChip> _chips = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isKeyPickerOpen;

    [ObservableProperty]
    private string _keySearch = string.Empty;

    /// <summary>Keys offered in the picker: not yet used and matching the search text.</summary>
    [ObservableProperty]
    private IReadOnlyList<KeyItem> _filteredKeys = [];

    [ObservableProperty]
    private bool _isNoKeysFound;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isErrorOpen;

    partial void OnKeySearchChanged(string value) => RefreshFilteredKeys();

    #region Commands

    private bool CanClear() => !IsEmpty;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => Save([]);

    [RelayCommand]
    private void OpenKeyPicker()
    {
        KeySearch = string.Empty;
        RefreshFilteredKeys();
        IsKeyPickerOpen = true;
    }

    [RelayCommand]
    private void SelectKey(KeyItem? key)
    {
        IsKeyPickerOpen = false;

        if (key is null || _combo.Contains(key.Code))
            return;

        Save([.. _combo, key.Code]);
    }

    #endregion

    #region Internals

    private void Save(uint[] combo)
    {
        try
        {
            Settings.Hotkey = combo;
            _combo = combo;
            IsErrorOpen = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Chips =
        [
            .. _combo
                .Select((code, index) =>
                {
                    var hex = $"0x{code:X2}";
                    var name = KeysByCode.TryGetValue(code, out var key) ? key.Name : hex;
                    return new HotkeyChip(name, hex, HasLeadingPlus: index > 0);
                })
        ];

        IsEmpty = _combo.Length == 0;
        RefreshFilteredKeys();
    }

    private void RefreshFilteredKeys()
    {
        var used = _combo.ToHashSet();
        var query = KeySearch.Trim();

        FilteredKeys =
        [
            .. AllKeys
                .Where(k => !used.Contains(k.Code) &&
                            (query.Length == 0 ||
                             k.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             k.Hex.Contains(query, StringComparison.OrdinalIgnoreCase)))
        ];

        IsNoKeysFound = FilteredKeys.Count == 0;
    }

    private void ReportError(Exception ex)
    {
        ErrorMessage = ex.Message;
        IsErrorOpen = true;
    }

    /// <summary>Builds the key list from the VK_KEY enum: one entry per distinct code, ordered by code.</summary>
    private static List<KeyItem> BuildAllKeys() =>
    [
        .. ((VK_Key[])Enum.GetValues(typeof(VK_Key)))
        .Select(value => (Code: Convert.ToUInt32(value), Name: PrettifyName(value.ToString())))
        .Where(x => x.Code != 0)
        .GroupBy(x => x.Code)
        .Select(g => g.First())
        .OrderBy(x => x.Code)
        .Select(x => new KeyItem(x.Code, x.Name))
    ];

    private static string PrettifyName(string name) =>
        name.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) ? name[3..] : name;

    #endregion
}
