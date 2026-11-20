# Installation

## Check the download first

1. Download `SHA256SUMS.txt` from the same GitHub release.
2. Compare the installer's SHA-256 with the value in that file, for example in PowerShell:
   ```powershell
   Get-FileHash .\HTFileHashChecker_Setup_v1.0.0.exe -Algorithm SHA256
   ```
3. Optionally check the signature (self-signed HTREASURE LTD certificate, thumbprint
   `2F352812FE849999928F25F778A3D49AE5750EF2`; see [CODE-SIGNING.md](CODE-SIGNING.md)):
   ```powershell
   Get-AuthenticodeSignature .\HTFileHashChecker_Setup_v1.0.0.exe | Format-List Status, SignerCertificate
   ```

Because the certificate is self-signed, Windows may show "Unknown publisher" and SmartScreen may warn.

## Installer

Run `HTFileHashChecker_Setup_v1.0.0.exe`.

1. **Install mode.** *Install for all users* (Program Files, needs administrator rights) or *Install for me only*
   (`%LOCALAPPDATA%\Programs`, no administrator rights).
2. **Licence** and **privacy** pages.
3. **System Compatibility Check.** Setup shows Windows, architecture, the application build it will install
   (.NET Framework 4.8 build, or the 4.6.1 build on Windows 8.0), installed .NET and any existing installation.
4. **Prerequisite Check.** If the required .NET Framework is missing, Setup explains what it will download (from
   `download.microsoft.com`, checked against a pinned SHA-256 and Microsoft's signature) and asks for your consent.
   Installing .NET needs administrator rights; "for me only" Setup stops and tells you to use all-users mode or install
   .NET first. If Windows needs a restart after .NET, restart and run Setup again.
5. **Folder** (default `Program Files\HTREASURE\HT File Hash Checker`).
6. **Options:** desktop shortcut; *Add "Calculate Hash with HT File Hash Checker" to the File Explorer right-click menu*.
   Both are off by default. A Start menu shortcut is always created under **HTREASURE**.

Nothing else is installed: no services, no start-up entries, no advertising.

### Silent installation (administrators)

```powershell
.\HTFileHashChecker_Setup_v1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS /TASKS="explorer"
```

Use `/CURRENTUSER` for a per-user installation. A silent install never downloads .NET Framework (there is no consent
step); it stops if .NET is missing. Add `/LOG="C:\path\setup.log"` for a log.

## Upgrading

Run the newer Setup. It installs over the previous version in the same folder, keeps your options and never changes
your settings. Setup refuses to install an older version over a newer one.

## Uninstalling

**Settings > Apps** (Windows 10/11) or **Control Panel > Programs** (Windows 7/8), select *HT File Hash Checker*.
The uninstaller removes the program, shortcuts and the Explorer menu entry, then asks whether to remove your settings
and logs too (default: keep). Your files, reports and checksum manifests are never removed. Shared Microsoft components
such as .NET Framework are never removed.

## Portable edition

1. Unzip `HTFileHashChecker_Portable_v1.0.0.zip` anywhere, for example a USB drive.
2. Run `HTFileHashChecker.exe`. On Windows 8.0 use the copy in `Windows 8.0 (NET 4.6.1)`.

The file `HTFileHashChecker.portable` puts the program in portable mode: settings and logs are kept in a `Data` folder
next to it, separate from any installed copy. Portable mode writes nothing to the registry, unless you turn on
**Settings > Explorer Right-Click Menu** yourself. That adds an entry for your Windows account that points at this
folder; turn it off before you move or delete the folder.

## Where things are stored

| | Installed | Portable |
|---|---|---|
| Settings | `%APPDATA%\HTREASURE LTD\HT File Hash Checker\settings.xml` | `Data\settings.xml` |
| Logs | `%LOCALAPPDATA%\HTREASURE LTD\HT File Hash Checker\Logs` | `Data\Logs` |
| Advisory cache | `%LOCALAPPDATA%\HTREASURE\ht-file-hash-checker\security` | same |
