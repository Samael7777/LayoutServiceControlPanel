using System.ComponentModel;
using System.Reflection;

namespace LayoutTools.Managed;


public partial class Layouts
{
    private static readonly Assembly s_Assembly = Assembly.GetExecutingAssembly();
    private static readonly string s_AssemblyName = Path.GetFileNameWithoutExtension(s_Assembly.ManifestModule.ScopeName);

    public static uint[] GetKeyboardLayoutList()
    {
        unsafe
        {
            var len = GetKeyboardLayoutList(0, null);
            if (len < 0)
                throw new Win32Exception();

            Span<nint> buffer = stackalloc nint[len];
            fixed (nint* pBuffer = buffer)
            {
                len = GetKeyboardLayoutList(buffer.Length, pBuffer);
                if (len < 0)
                    throw new Win32Exception();
            }
            
            var result = new uint[len];
            for (var i = 0; i < len; i++)
            {
                var klid = GetKLIDFromHKL(buffer[i]);
                result[i] = (uint)klid;
            }

            return result;
        }
    }

    public static uint[] GetUserLayouts()
    {
        unsafe
        {
            var len = GetUserLayouts(null, 0);
            if (len < 0)
                throw new Win32Exception();

            Span<uint> buffer = stackalloc uint[len];
            fixed (uint* pBuffer = buffer)
            {
                len = GetUserLayouts(pBuffer, buffer.Length);
                if (len < 0)
                    throw new Win32Exception();
            }

            return [.. buffer[..len]];
        }
    }

    public static uint[] GetUserLayoutsLegacy()
    {
        unsafe
        {
            var len = GetUserLayoutsLegacy(null, 0);
            if (len < 0)
                throw new Win32Exception();

            Span<uint> buffer = stackalloc uint[len];
            fixed (uint* pBuffer = buffer)
            {
                len = GetUserLayoutsLegacy(pBuffer, buffer.Length);
                if (len < 0)
                    throw new Win32Exception();
            }
            
            return [.. buffer[..len]];
        }
    }

    public static string GetLocalizedLanguageName(ushort langId)
    {
        unsafe
        {
            var len = GetLocalizedLanguageName(langId, null, 0);
            if (len < 0)
                throw new Win32Exception();

            Span<char> buffer = stackalloc char[len];
            fixed (char* pBuffer = buffer)
            {
                len = GetLocalizedLanguageName(langId, pBuffer, buffer.Length);
                if (len < 0)
                    throw new Win32Exception();
            }
            
            return buffer[..(len - 1)].ToString();
        }
    }

    public static string GetLocalizedLayoutName(uint klId)
    {
        unsafe
        {
            var len = GetLocalizedLayoutName(klId, null, 0);
            if (len < 0)
                throw new Win32Exception();

            Span<char> buffer = stackalloc char[len];
            fixed (char* pBuffer = buffer)
            {
                len = GetLocalizedLayoutName(klId, pBuffer, buffer.Length);
                if (len < 0)
                    throw new Win32Exception();
            }
            
            return buffer[..(len - 1)].ToString();
        }
    }

    public static string GetLocale(ushort langId)
    {
        unsafe
        {
            var len = LCIDToLocaleName(langId, null, 0, 0);
            if (len < 0)
                throw new Win32Exception();

            Span<char> buffer = stackalloc char[len];
            fixed(char* lpBuffer = buffer)
            {
                len = LCIDToLocaleName(langId, lpBuffer, len, 0);
                if (len < 0)
                    throw new Win32Exception();
            }
            
            return buffer[..(len - 1)].ToString();
        }
    }

    public static byte[]? GetIconForLang(ushort langId)
    {
        var locale = GetLocale(langId);
        var countryCode = locale.Split('-')[1];
        var resourceName = $"{countryCode}.ico";
        var resourcePath = $"{s_AssemblyName}.Resources.Flags.{resourceName}";

        using var rs = s_Assembly.GetManifestResourceStream(resourcePath);
        if (rs is null)
            return null;

        using var ms = new MemoryStream();
        rs.CopyTo(ms);

        return ms.ToArray();
    }
}