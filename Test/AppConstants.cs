using Microsoft.Win32;

namespace Test;

public class AppConstants
{
    public const RegistryHive ApplicationRegRoot = RegistryHive.CurrentUser;
    public const string ApplicationRegSubkey = @"Software\Phoenix Tools\Layout Service";
    public const string HotkeyComboKey = "Hotkey combination";
    public const string LayoutsKey = "Layouts";
    public const string SwitcherTaskName = "LayoutService";
    public const string SwitcherTaskDesc = "LayoutService task";
}