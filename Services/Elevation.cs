using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Guardian;

namespace Guardian.Services;

public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Relaunches the app through UAC. Returns false if the user cancelled the prompt.</summary>
    public static bool RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            App.PrepareExit();
            Microsoft.UI.Xaml.Application.Current.Exit();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
