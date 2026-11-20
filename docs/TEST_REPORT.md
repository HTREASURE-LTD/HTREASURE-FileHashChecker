# Test report - HT File Hash Checker 1.0.0

Date: 20 November 2026. Source: commit `6677090`. Machine: Windows 11 Pro 10.0.26300, x64, .NET Framework 4.8.1
(release 534296), 100% display scaling. Toolchain: .NET SDK 11.0.100, Inno Setup 6.7.

Only results that were actually executed are reported. Anything not run is listed under *Not tested*.

## 1. Build

| Check | Result |
|---|---|
| `dotnet build HTFileHashChecker.sln -c Release` (net48 and net461), warnings treated as errors | **Pass**, 0 warnings, 0 errors |
| Release build without the advisory signing key is refused by `HTREASURE.Standards.targets` | **Pass** (expected error) |
| Generated `ht-compatibility.json`: net48 min OS 6.1.7601, net461 min OS 6.2.9200 | **Pass** |

## 2. Automated tests

`dotnet test HTFileHashChecker.sln -c Release` runs every test twice: on `net48`, and on `net462` against the `net461`
(Windows 8) libraries.

| Framework | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| net48 | 185 | 185 | 0 | 0 |
| net462 (net461 libraries) | 185 | 185 | 0 | 0 |

Three further consecutive full runs after the last code change: 185/185 on both frameworks each time.

| Area | Tests | Covers |
|---|---|---|
| Known answers | 42 | FIPS 180 vectors for SHA-1/256/384/512 (empty, "abc", 448/896-bit, one million "a" streamed in 4 KB blocks), RFC 1321 MD5 suite, metadata, algorithm detection from names, hex and constant-time comparison |
| File hashing | 17 | Unicode names, paths over 260 characters, missing, exclusively locked, folder, unsafe paths, cancellation before and during reading (handle released), file changed during hashing, file unchanged after hashing (bytes and timestamp), odd sizes, junction escape, **4 GiB + 12,345 byte file** (sparse, last byte non-zero) |
| Expected hash | 16 | Whitespace, case, `0x`, zero-width characters, wrong length, non-hex, pasted file names, detection by length, exact matching |
| Batch processing | 6 | MATCH/MISMATCH/MISSING/ERROR, algorithm mismatch, 40 files with 1 and 3 workers, cancellation, planned statuses untouched |
| Manifest parsing | 35 | GNU text/binary/escaped, BSD tags, spaces and Unicode, UTF-8 (BOM), UTF-16, CRLF, comments, 17 unsafe-path forms (traversal, absolute, drive, UNC, ADS, device names, wildcards...), duplicates and conflicts (including case), invalid lines with line numbers, algorithm mismatch, invalid UTF-8 |
| Manifest writing | 7 | Sorted GNU output, determinism, round trip, mixed algorithms (BSD), unsafe and duplicate paths, no silent overwrite, UTF-8 without BOM and LF |
| Manifest verification | 4 | End-to-end with match, mismatch (modified file), missing, invalid and unexpected files; junction escape blocked |
| Reports | 4 | Required fields in TXT, CSV quoting and formula injection, HTML encoding with no scripts, no silent overwrite |
| Settings | 7 | Round trip, defaults, corrupt file recovery, out-of-range values, DTD rejected, portable paths, log retention |
| Explorer integration | 6 | Register (quoted command, files and folders, multi-select), unregister, hiding an all-users entry, unsafe executable paths (in a scratch registry key) |
| Command line and single instance | 4 | Argument parsing, Unicode pipe messages, oversized/truncated messages, forwarding to the first instance |
| Workspace and scanner | 5 | Recursive sorted scan, junctions skipped, depth and file limits, de-duplication, calculate then verify, manifest export paths |
| HTREASURE standards conformance | 29 | All 21 cases of the shared `expected-statuses.json`, fixture validation, ECDSA P-256 signatures (valid, tampered, unknown key, wrong key, missing, malformed), production trusted keys, offline snapshot handling, reports, strict JSON, SemVer |
| Build integration | 3 | HTAppId/channel/version and embedded keys, snapshot and manifest in the output, branding and copyright from the kit |

## 3. Application checks

| Check | Result |
|---|---|
| `--selftest` of the net48 and net461 Release executables (SHA-256, SHA-1, MD5 known answers) | **Pass** |
| Main window, light and dark themes, empty and populated states (screenshots) | Pass (visual review) |
| Window has no system menu, minimise/maximise/close or resize frame; closes normally | **Pass** (window styles read back) |
| About box, Security Advisory History, splash screen (screenshots) | Pass (visual review) |

## 4. Installer (per-user mode, `/CURRENTUSER`, silent)

36 automated checks on the final `HTFileHashChecker_Setup_v1.0.0.exe`: **36 passed, 0 failed.**

Install (exit 0, .NET 4.8 build selected, no portable marker, signed exe and uninstaller, documents and advisory
snapshot present, Apps & features entry with publisher and comment, Start menu and desktop shortcuts, Explorer verb for
files and folders with correct quoting and multi-select, self-test, settings path); upgrade (existing version detected,
folder, settings and Explorer option kept, self-test); downgrade refused (exit 1); uninstall (exit 0; folder, entry,
shortcuts and Explorer verbs removed; settings kept). Setup wizard pages reviewed by screenshot.

## 5. Portable edition

Extracted from the ZIP with `unzip`: both builds run in portable mode (settings in `Data\`), pass the self-test, are
signed; interactive run created `Data\settings.xml`; no change to `%APPDATA%` or the registry.

## 6. Release packages

All four packages and `SHA256SUMS.txt` built by `scripts\build-release.ps1`; checksums verified with GNU `sha256sum -c`
(independent of the program); ZIP entries use `/`; source ZIP contains both submodules; no private key, password or
key file in any package. Setup signed (SHA-256, DigiCert timestamp) by thumbprint `2F352812...0EF2`.

## Not tested

- Windows 10, 8.1, 8.0 and 7 SP1; x86 Windows; Arm64.
- Display scaling of 125%, 150% and 200%, and multiple monitors.
- All-users installation (needs an elevated session) and the .NET Framework download path (needs a computer without
  .NET 4.8), including restart handling.
- The Explorer menu used interactively from File Explorer (the registry entries and the forwarding pipe are tested).
- Screen readers.
- A build with the signed advisory snapshot (the signing key was not available; packages are internal builds).

## Defects found and fixed during testing

| Defect | Fix |
|---|---|
| `HashItem.FileName` threw for paths containing characters such as `<` | Name taken from the last separator |
| Non-hexadecimal first word classified as "invalid hash" | Classified as "not a checksum line" |
| Setup did not detect an existing installation (wrong registry key), disabling downgrade protection | Separate key define for `[Code]` |
| ZIP entries written with `\` separators | Entries written with `/` |
| Single-instance listener could start late on a busy thread pool | Dedicated thread |
| Unit tests left an empty registry key | Tests remove empty parent keys |
