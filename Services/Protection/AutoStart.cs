using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Guardian.Services.Protection;

public enum StartMode
{
    /// <summary>Guardian does not start with Windows.</summary>
    Off,
    /// <summary>A normal per-user Run entry: starts quietly in the tray when you sign in, without administrator rights.</summary>
    User,
    /// <summary>A scheduled task that starts Guardian at sign-in with administrator rights (no prompt at boot), so the
    /// "programs as they start" shield works from the first minute.</summary>
    Admin,
}

/// <summary>Starts Guardian quietly in the tray when you sign in to Windows (no service, no driver).</summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Guardian";
    public const string TaskName = "GuardianStartup";

    static bool HasRunEntry
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s && s.Contains("--background");
        }
    }

    static bool HasTask
    {
        get
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN {TaskName}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi)!;
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                return p.WaitForExit(5000) && p.ExitCode == 0;
            }
            catch { return false; }
        }
    }

    public static StartMode Mode => HasTask ? StartMode.Admin : HasRunEntry ? StartMode.User : StartMode.Off;

    public static bool IsEnabled => Mode != StartMode.Off;

    /// <summary>If both the task and the Run entry exist (the installer's box was ticked after the task was made), keep only the task.</summary>
    public static void Tidy()
    {
        try { if (HasRunEntry && HasTask) RemoveRunEntry(); } catch { /* best effort */ }
    }

    static void SetRunEntry()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
        k.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
    }

    static void RemoveRunEntry()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
        k.DeleteValue(ValueName, false);
    }

    /// <summary>
    /// Switches the start-up mode. Making or removing the administrator task needs one UAC prompt; if that is
    /// declined nothing changes and the reason comes back in <c>Error</c>.
    /// </summary>
    public static async Task<(bool Ok, string Error)> SetAsync(StartMode mode) => await Task.Run(() =>
    {
        try
        {
            switch (mode)
            {
                case StartMode.Off:
                    RemoveRunEntry();
                    if (HasTask) RunSchtasks($"/Delete /TN {TaskName} /F");
                    break;
                case StartMode.User:
                    if (HasTask) RunSchtasks($"/Delete /TN {TaskName} /F");
                    SetRunEntry();
                    break;
                case StartMode.Admin:
                    CreateTask();
                    RemoveRunEntry();                // the task replaces it, or Guardian would start twice
                    break;
            }
            return (true, "");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "The administrator prompt was cancelled, so nothing was changed.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    });

    internal static string BuildTaskXml(string userName, string exePath)
    {
        var user = SecurityElement.Escape(userName);
        var exe = SecurityElement.Escape(exePath);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Guardian in the taskbar corner when you sign in, with administrator rights.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{exe}"</Command><Arguments>--background</Arguments></Exec></Actions>
            </Task>
            """;
    }

    static void CreateTask()
    {
        var xml = BuildTaskXml(WindowsIdentity.GetCurrent().Name, Environment.ProcessPath!);
        var file = Path.Combine(Path.GetTempPath(), "guardian-startup-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try { RunSchtasks($"/Create /TN {TaskName} /XML \"{file}\" /F"); }
        finally { try { File.Delete(file); } catch { } }
    }

    /// <summary>Runs schtasks.exe with administrator rights (one UAC prompt unless Guardian is already elevated).</summary>
    static void RunSchtasks(string args)
    {
        var elevated = Elevation.IsElevated;
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = !elevated,
            Verb = elevated ? "" : "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start schtasks.exe.");
        if (!p.WaitForExit(30000)) throw new TimeoutException("Windows took too long to change the start-up task.");
        if (p.ExitCode != 0) throw new InvalidOperationException($"Windows could not change the start-up task (schtasks exit code {p.ExitCode}).");
    }
}
