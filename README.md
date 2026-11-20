# HT File Hash Checker

**Calculate, compare and verify file checksums on Windows.** An HTREASURE LTD product.

HT File Hash Checker checks that downloaded installers, ISO images, archives, backups and other files are exactly the
files you expect. It calculates SHA-256 (default), SHA-512, SHA-384, SHA-1 and MD5 checksums, compares them with the
values publishers provide, verifies whole folders against `SHA256SUMS`-style manifests, and saves reports. It works
completely offline: no account, no subscription, no telemetry.

> A matching checksum shows that a file is identical to the file the expected value was calculated from. It does
> **not** show that the file is free of malware or comes from a trusted publisher. That depends on getting the expected
> value from a trusted source.

## Features

- **Five algorithms** using the Windows cryptographic providers: SHA-256 (recommended), SHA-512, SHA-384, and the
  legacy SHA-1 and MD5 (clearly marked as unsuitable for security-sensitive verification).
- **Files, multiple files, folders (optionally with subfolders) and drag and drop.** Files of any size, including ISO
  images larger than 4 GB, are streamed and never loaded into memory or modified.
- **Verify against an expected hash**: paste it in any letter case, with spaces or line breaks; the algorithm is
  detected from the length. Results: **MATCH**, **MISMATCH**, **ERROR**. Comparison runs in constant time.
- **Batch processing** in the background with per-file and overall progress, counts, safe cancellation and a summary.
- **Checksum manifests**: read and write GNU `SHA256SUMS` / `SHA512SUMS` / `MD5SUMS` files (text, binary and escaped
  lines) and BSD `--tag` lines. Detects missing, modified, duplicate and unexpected files; rejects path traversal,
  absolute paths and ambiguous entries.
- **Reports** in TXT, CSV and HTML (safely escaped).
- **Optional Explorer menu**: "Calculate Hash with HT File Hash Checker" for files and folders (off by default).
- **HTREASURE standards**: corporate branding, Security Advisory History (Help > About), signed release manifest.
- Light and dark themes, keyboard shortcuts, accessible names, high-DPI layout.

## Downloads

Each GitHub release provides:

| File | Use |
|---|---|
| `HTFileHashChecker_Setup_v1.0.0.exe` | Installer (all users or only for you; installs .NET Framework from Microsoft only if missing and you agree) |
| `HTFileHashChecker_Portable_v1.0.0.zip` | Portable edition: unzip and run, nothing installed |
| `HTFileHashChecker_Source_v1.0.0.zip` | Source code, including the HTREASURE standards submodules |
| `HTFileHashChecker_Documentation_v1.0.0.zip` | This documentation |
| `SHA256SUMS.txt` | SHA-256 of every file above |

The files are signed with HTREASURE LTD's **self-signed** certificate, so Windows may show "Unknown publisher". See
[docs/CODE-SIGNING.md](docs/CODE-SIGNING.md).

## Requirements

| Windows | Build | .NET Framework |
|---|---|---|
| 7 SP1, 8.1, 10, 11 | main | 4.8 (built into Windows 10 1903+ and 11; Setup can install it) |
| 8.0 | compatibility | 4.6.1 |

x86 and x64 (Arm64 through x64 emulation). No administrator rights are needed to use the program.
See [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md) for what has and has not been tested.

## Documentation

| Document | Contents |
|---|---|
| [docs/INSTALLATION.md](docs/INSTALLATION.md) | Install, upgrade, uninstall, portable use |
| [docs/USER_GUIDE.md](docs/USER_GUIDE.md) | How to use every feature |
| [docs/HASH_ALGORITHMS.md](docs/HASH_ALGORITHMS.md) | Choosing an algorithm |
| [docs/MANIFEST_FORMATS.md](docs/MANIFEST_FORMATS.md) | Supported checksum files and their limits |
| [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Common problems |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/BUILD.md](docs/BUILD.md) | For developers |
| [docs/TEST_REPORT.md](docs/TEST_REPORT.md) | Test results for this release |
| [SECURITY.md](SECURITY.md), [CHANGELOG.md](CHANGELOG.md), [RELEASE_NOTES.md](RELEASE_NOTES.md) | Security policy and history |

## Building from source

```bash
git clone --recurse-submodules https://github.com/HTREASURE-LTD/HTREASURE-FileHashChecker.git
```

```bash
dotnet build HTFileHashChecker.sln -c Debug
```

See [docs/BUILD.md](docs/BUILD.md) for tests, release builds, signing and packaging.

## Licence

Proprietary. Copyright © 2026 HTREASURE LTD. All rights reserved. See [LICENSE](LICENSE) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

HTREASURE LTD · Company No. 17178941 · Registered in England and Wales · https://htreasure.com · support@htreasure.com
