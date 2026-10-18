using System.Globalization;
using LayoutTools.Managed;

namespace ControlPanel.UI.Models;

/// <summary>Display model of a keyboard layout identifier.</summary>
/// <param name="Id">Raw layout identifier as stored in settings.</param>
/// <param name="Code">Short language code shown in the badge (e.g. "EN").</param>
/// <param name="Name">Human-readable language name.</param>
/// <param name="Hex">Raw identifier formatted as hexadecimal text.</param>
public sealed record LayoutItem(uint Id, string Code, string Name, string Hex)
{
    /// <summary>
    /// Builds a display model. The low word of both an HKL and a KLID is the LANGID,
    /// which is used to resolve the language name. Adjust here if your identifiers differ.
    /// </summary>
    public static LayoutItem Create(uint id)
    {
        var hex = $"0x{id:X8}";
        var langId = (int)(id & 0xFFFF);

        if (langId > 0)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(langId);
                if (culture.Name.Length > 0)
                {
                    var code = culture.TwoLetterISOLanguageName.ToUpperInvariant();
                    var name = Layouts.GetLocalizedLayoutName(id);
                    return new LayoutItem(id, code, name, hex);
                }
            }
            catch (ArgumentException)
            {
                // Covers CultureNotFoundException and out-of-range LANGIDs:
                // fall through to the generic entry below.
            }
        }

        return new LayoutItem(id, "??", "Unknown layout", hex);
    }
}
