using System.Runtime.InteropServices;

namespace LayoutTools.Managed;

public partial class Layouts
{
#if NET6_0_OR_GREATER
    [LibraryImport("User32.dll", EntryPoint = "GetKeyboardLayoutList", SetLastError = true)]
    private static unsafe partial int GetKeyboardLayoutList(int nBuff, nint* lpList);

    [LibraryImport("LayoutTools.dll", EntryPoint = "GetKLIDFromHKL", SetLastError = true)]
    private static unsafe partial int GetKLIDFromHKL(nint hkl);
    [LibraryImport("LayoutTools.dll", EntryPoint = "GetUserLayouts")]
    private static unsafe partial int GetUserLayouts(uint* buffer, int bufferLen);

    [LibraryImport("LayoutTools.dll", EntryPoint = "GetUserLayoutsLegacy")]
    private static unsafe partial int GetUserLayoutsLegacy(uint* buffer, int bufferLen);

    [LibraryImport("LayoutTools.dll", EntryPoint = "GetLocalizedLanguageName")]
    private static unsafe partial int GetLocalizedLanguageName(ushort langId, char* buffer, int bufferLen);

    [LibraryImport("LayoutTools.dll", EntryPoint = "GetLocalizedLayoutName")]
    private static unsafe partial int GetLocalizedLayoutName(uint klId, char* buffer, int bufferLen);

    [LibraryImport("kernel32.dll", EntryPoint = "LCIDToLocaleName")]
    private static unsafe partial int LCIDToLocaleName(ushort locale, char* lpName, int cchName, uint  dwFlags);
#endif
#if NETFRAMEWORK
    [DllImport("User32.dll", EntryPoint = "GetKeyboardLayoutList", SetLastError = true)]
    private static unsafe extern int GetKeyboardLayoutList(int nBuff, nint* lpList);
    
    [DllImport("LayoutTools.dll", EntryPoint = "GetKLIDFromHKL", SetLastError = true)]
    private static unsafe extern int GetKLIDFromHKL(nint hkl);

    [DllImport("LayoutTools.dll", EntryPoint = "GetUserLayouts")]
    private static unsafe extern int GetUserLayouts(uint* buffer, int bufferLen);

    [DllImport("LayoutTools.dll", EntryPoint = "GetUserLayoutsLegacy")]
    private static unsafe extern int GetUserLayoutsLegacy(uint* buffer, int bufferLen);

    [DllImport("LayoutTools.dll", EntryPoint = "GetLocalizedLanguageName")]
    private static unsafe extern int GetLocalizedLanguageName(ushort langId, char* buffer, int bufferLen);

    [DllImport("LayoutTools.dll", EntryPoint = "GetLocalizedLayoutName")]
    private static unsafe extern int GetLocalizedLayoutName(uint klId, char* buffer, int bufferLen);

    [DllImport("kernel32.dll", EntryPoint = "LCIDToLocaleName")]
    private static unsafe extern int LCIDToLocaleName(ushort locale, char* lpName, int cchName, uint  dwFlags);
#endif
}