# Guardian

A Windows system-care app: scan for viruses, find outdated drivers and apps, and repair system files. Built with WinUI 3 on .NET 9, styled after the Lumen apps.

![Overview](docs/overview.png)

## What it does

| | |
|---|---|
| **Virus scan** | Quick, full or folder scans through Windows Defender (`Start-MpScan`). An optional VirusTotal check hashes the newest executables in a folder and looks the hashes up. Files are never uploaded. |
| **Drivers** | Lists installed drivers (flags non-Microsoft ones older than 3 years) and asks Windows Update which driver updates are pending. A restore point is attempted before any install. |
| **App updates** | Finds and installs updates through `winget`, one at a time or all at once. |
| **System repair** | DISM `/ScanHealth` and `/RestoreHealth`, `sfc /scannow`, and an online `chkdsk C: /scan`, with live progress and output. |

![System repair](docs/repair.png)

## Design rules

- It wraps tools Windows already ships instead of reimplementing them. There is no custom detection engine, so Guardian is **not** a replacement for Defender or another antivirus.
- Nothing is removed, installed or repaired on its own. Every threat removal, driver install and repair is started by you, and removals show a confirmation listing what will be affected.
- The app starts without administrator rights and offers "Restart as administrator" for the parts that need it (driver installs, DISM, SFC).
- The VirusTotal API key is stored encrypted with DPAPI for the current Windows user. Only SHA-256 hashes are sent.

![Settings](docs/settings.png)

## Build and run

Requires Windows 10 1809+ (Windows 11 recommended) and the .NET 9 SDK.

```
dotnet build -p:Platform=x64
bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\Guardian.exe
```

Set `GUARDIAN_START_PAGE` to `scan`, `drivers`, `apps`, `repair` or `settings` to open straight to a page.

## Project layout

- `Services/` runs and parses the underlying tools (`DefenderService`, `DriverService`, `AppUpdateService`, `RepairService`, `VirusTotalService`).
- `Pages/` has one XAML page per section.
- `Assets/` holds the fonts and ambient background.

## Credits and licenses

The Syne and Manrope fonts in `Assets/Fonts` are licensed under the [SIL Open Font License 1.1](https://openfontlicense.org). See the upstream projects for their copyright notices: [Syne](https://github.com/googlefonts/syne) and [Manrope](https://github.com/sharanda/manrope).
