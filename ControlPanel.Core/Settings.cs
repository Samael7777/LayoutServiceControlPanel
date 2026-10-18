using ControlPanel.Core.Extensions;
using Microsoft.Win32;

namespace ControlPanel.Core;

public static class Settings 
{
    private static readonly RegistryKey s_Root = RegistryKey.OpenBaseKey(AppConstants.ApplicationRegRoot, RegistryView.Default);
    private static readonly ReaderWriterLockSlim s_Lock = new();
    
    public static IReadOnlyCollection<uint> Hotkey
    {
        get => ReadArrayLock<uint>(AppConstants.HotkeyComboKey);
        set => WriteArrayLock(AppConstants.HotkeyComboKey, [.. value]);
    }

    public static IReadOnlyCollection<uint> CycledLayouts
    {
        get => ReadArrayLock<uint>(AppConstants.LayoutsKey);
        set => WriteArrayLock(AppConstants.LayoutsKey, [.. value]);
    }

    private static T[] ReadArrayLock<T>(string name)
        where T: unmanaged
    {
        s_Lock.EnterReadLock();
        try
        {
            using var regKey = OpenCreateAppKey();
            return regKey.ReadArray<T>(name) ?? [];
        }
        finally
        {
            s_Lock.ExitReadLock();
        }
    }

    private static void WriteArrayLock<T>(string name, ReadOnlySpan<T> data)
        where T: unmanaged
    {
        s_Lock.EnterWriteLock();
        try
        {
            using var regKey = OpenCreateAppKey();
            T[] array = [.. data];
            regKey.WriteArray(name, array);
        }
        finally
        {
            s_Lock.ExitWriteLock();
        }
    }

    private static RegistryKey OpenCreateAppKey()
    {
        return s_Root.CreateSubKey(AppConstants.ApplicationRegSubkey) 
               ?? throw new ApplicationException($"Can't create {AppConstants.ApplicationRegSubkey}");
    }
}