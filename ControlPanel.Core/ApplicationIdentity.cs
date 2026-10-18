using System.Security.Principal;

namespace ControlPanel.Core;

public static class ApplicationIdentity
{
    public static bool IsAdministrator
    {
        get
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}