# Troubleshooting

| Problem | Cause and solution |
|---|---|
| **MISMATCH** for a download | The file is different from the one the publisher hashed: incomplete download, corruption, or a different file. Download it again from the official source. Check that you copied the expected value for the right file and version. |
| "The expected hash has 32 characters, but a SHA-256 hash has exactly 64" | You pasted a value for another algorithm. The program detects it from the length; check you did not paste only part of it. |
| "not hexadecimal ... Paste only the hash value" | A file name or other text was pasted with the hash. Paste only the hash. |
| ERROR: "The file is in use by another program" | Another program has the file open exclusively. Close it and recalculate. |
| ERROR: "The file changed while it was being read" | Another program wrote to the file during hashing (for example a download still in progress). Wait until it finishes. |
| ERROR: "Access denied" | You do not have permission to read the file. The program never asks for administrator rights. |
| Files "Skipped online-only (cloud) file" | OneDrive and similar placeholders are skipped so they are not downloaded. Make them available offline, or turn off **Skip online-only cloud files** in Options. |
| Manifest: INVALID ENTRY | The line is unsafe or malformed (see [MANIFEST_FORMATS.md](MANIFEST_FORMATS.md)). The Details column says why. |
| Manifest: everything MISSING | The verification folder is wrong. Choose the folder that contains the listed files. |
| MD5 "not available on this computer" | Windows enforces FIPS-approved algorithms only. Use SHA-256. |
| Explorer menu does not appear | Turn it on in **Settings > Explorer Right-Click Menu**. On Windows 11 it is under **Show more options** (Shift+F10). If it points to an old portable copy, turn it off and on again. |
| Windows shows "Unknown publisher" or SmartScreen warns | The files are signed with a self-signed certificate. Check `SHA256SUMS.txt` and the signature thumbprint ([CODE-SIGNING.md](CODE-SIGNING.md)). |
| Setup: ".NET Framework ... is required ... needs administrator rights" | Run Setup again and choose *Install for all users*, or install .NET Framework 4.8 from Microsoft first. |
| Setup: "could not download ... securely" | Older Windows versions may lack modern TLS support. Install .NET Framework 4.8 from Microsoft's offline installer, then run Setup again. Setup never weakens security to work around this. |
| Setup: "A newer version ... is already installed" | Setup does not downgrade. Uninstall the newer version first if you really need this one. |
| Security Advisory History: "Development build: ... not signed" | You are running an internal build. Release builds carry a signed advisory snapshot. |
| Settings reset to defaults | The settings file could not be read; it was renamed to `settings.xml.corrupt` next to the original. |
| Text too small or blurred on high-DPI screens | Use the .NET Framework 4.8 build (Windows 10 1703+ for per-monitor scaling). |

## Logs

**Help > Open Log Folder**. Logs contain file names and error types only, never file contents, hashes or full paths
of your files. They are deleted after the retention period set in Options (default 14 days).

## Getting help

support@htreasure.com. Include the program version (Help > About), your Windows version, and the relevant log file.
Do not send files you are checking unless asked.
