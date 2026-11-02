# Guardian

Windows system-care app (WinUI 3, .NET 9). It drives built-in tools rather than reimplementing them:

- **Virus scan**: Windows Defender (`Start-MpScan`) plus optional VirusTotal hash lookups (files are never uploaded).
- **Drivers**: installed-driver list and pending driver updates from Windows Update, with a restore point before installs.
- **App updates**: winget.
- **System repair**: DISM `/ScanHealth` and `/RestoreHealth`, `sfc /scannow`, `chkdsk C: /scan`.

Nothing is removed, installed or repaired without the user starting it.

## Build and run

    dotnet build -p:Platform=x64
    bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\Guardian.exe

The app starts unelevated; use "Restart as administrator" for drivers and repairs.
Add a VirusTotal API key under Settings (stored with DPAPI for the current user).
