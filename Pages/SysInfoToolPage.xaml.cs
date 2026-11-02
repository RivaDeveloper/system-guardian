using System.Collections.ObjectModel;
using Guardian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed class InfoRow(string key, string value)
{
    public string Key { get; set; } = key;
    public string Value { get; set; } = value;
}

public sealed partial class SysInfoToolPage : Page
{
    const string Script = """
        $os = Get-CimInstance Win32_OperatingSystem
        $cs = Get-CimInstance Win32_ComputerSystem
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $bb = Get-CimInstance Win32_BaseBoard
        $bios = Get-CimInstance Win32_BIOS
        "Computer=$($cs.Name)"
        "Operating system=$($os.Caption) $($os.OSArchitecture), build $($os.BuildNumber)"
        "Manufacturer and model=$($cs.Manufacturer) $($cs.Model)"
        "Motherboard=$($bb.Manufacturer) $($bb.Product)"
        "BIOS=$($bios.SMBIOSBIOSVersion), $($bios.ReleaseDate.ToString('yyyy-MM-dd'))"
        "Processor=$($cpu.Name.Trim()), $($cpu.NumberOfCores) cores, $($cpu.NumberOfLogicalProcessors) threads"
        "Memory=$([math]::Round($cs.TotalPhysicalMemory/1GB,1)) GB total, $([math]::Round($os.FreePhysicalMemory/1MB,1)) GB free"
        Get-CimInstance Win32_VideoController | ForEach-Object { "Graphics=$($_.Name), driver $($_.DriverVersion)" }
        Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object { "Disk $($_.DeviceID)=$([math]::Round(($_.Size-$_.FreeSpace)/1GB)) GB used of $([math]::Round($_.Size/1GB)) GB" }
        $up = (Get-Date) - $os.LastBootUpTime
        "Uptime=$($up.Days) days, $($up.Hours) hours, $($up.Minutes) minutes"
        """;

    public ObservableCollection<InfoRow> Rows { get; } = new();

    public SysInfoToolPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        Busy.IsActive = true;
        try
        {
            var r = await ProcessRunner.RunPowerShellAsync(Script);
            Rows.Clear();
            foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var i = line.IndexOf('=');
                if (i > 0) Rows.Add(new InfoRow(line[..i], line[(i + 1)..]));
            }
        }
        finally { Busy.IsActive = false; }
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(string.Join("\n", Rows.Select(r => $"{r.Key}: {r.Value}")));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
    }
}
