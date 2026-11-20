HT File Hash Checker 1.0.0
An HTREASURE LTD product - https://htreasure.com - support@htreasure.com

Calculate, compare and verify file checksums (SHA-256, SHA-512, SHA-384, SHA-1, MD5). Works offline.

QUICK START
1. Add files: Add Files, Add Folder, or drag and drop.
2. Choose Calculate Hash (F5).
3. To verify a download, paste the publisher's checksum into "Expected hash" and choose Verify (F6).
   MATCH = identical file. MISMATCH = different or damaged file; do not use it.
4. File > Verify with Checksum Manifest checks SHA256SUMS / SHA512SUMS / MD5SUMS files.
Full help: Help > User Guide (F1).

IMPORTANT
A matching checksum shows the file is identical to the one the expected value was calculated from. It does not
prove the file is free of malware or comes from a trusted publisher. MD5 and SHA-1 are legacy algorithms.

REQUIREMENTS
Windows 7 SP1, 8.1, 10 or 11 with .NET Framework 4.8 (included in Windows 10 1903 and later and Windows 11).
Windows 8.0: use the build in the "Windows 8.0 (NET 4.6.1)" folder (portable) - Setup selects it automatically.

PORTABLE EDITION
Unzip anywhere (for example a USB drive) and run HTFileHashChecker.exe. The file HTFileHashChecker.portable keeps
settings and logs in a Data folder next to the program; nothing is written to the registry unless you turn on
Settings > Explorer Right-Click Menu yourself (turn it off again before deleting the folder).

DIGITAL SIGNATURE
The program files are signed with HTREASURE LTD's self-signed code-signing certificate (HTREASURE-CodeSigning.cer).
Windows does not trust self-signed certificates automatically, so it may show "Unknown publisher". The signature
still lets you check that the files have not been altered. See docs\CODE-SIGNING.md.
