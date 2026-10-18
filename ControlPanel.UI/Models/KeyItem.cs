namespace ControlPanel.UI.Models;

/// <summary>A virtual key that can be added to the hotkey combination.</summary>
public sealed record KeyItem(uint Code, string Name)
{
    public string Hex => $"0x{Code:X2}";
}

/// <summary>One key of the current hotkey as shown in the UI.</summary>
/// <param name="Name">Key name.</param>
/// <param name="Hex">Key code as hexadecimal text (tooltip).</param>
/// <param name="HasLeadingPlus">True for every key except the first, to draw a "+" before it.</param>
public sealed record HotkeyChip(string Name, string Hex, bool HasLeadingPlus);
