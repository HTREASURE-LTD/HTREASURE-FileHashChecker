# Compatibility

HT File Hash Checker 1.0.0 is built twice from the same source.

| Build | .NET Framework | Windows | Minimum OS (ht-compatibility.json) | Notes |
|---|---|---|---|---|
| `net48` (main) | 4.8 or later | 7 SP1, 8.1, 10, 11 (x86, x64, Arm64 under emulation) | 6.1.7601 | Per-monitor DPI (Windows 10 1703+), long paths (> 260 characters) where Windows enables them |
| `net461` (compatibility) | 4.6.1 or later | 8.0 | 6.2.9200 | Windows 8.0 cannot install .NET Framework 4.6.2 or later. System DPI awareness; no long-path support |

Both builds are AnyCPU without *Prefer 32-bit*: a 64-bit process on 64-bit Windows, a 32-bit process on 32-bit Windows.

## Why .NET Framework and not .NET 10

The HTREASURE application template and the `ht-software-standards` libraries target .NET 10, which supports
Windows 10 1809 and later only. Windows 7 and 8 are a requirement for this product, so it uses .NET Framework ports of
the template's branding components and of the standards' Security Advisory History client
(`src/HTREASURE.Branding.NetFramework`, `src/HTREASURE.Standards.NetFramework`). See [HTREASURE-STANDARDS.md](HTREASURE-STANDARDS.md).

## Validation status

| Item | Status |
|---|---|
| Both builds compile with zero warnings (warnings are errors) | Verified (SDK 11.0.100, Visual Studio 2026 MSBuild) |
| Automated tests: `net48`, and `net462` running the `net461` libraries | 185 tests each, all passing |
| Self-test (`--selftest`) of both Release executables | Pass on Windows 11 Pro 10.0.26300, x64 |
| Interface at 100% scaling, light and dark theme | Checked by screenshot on Windows 11 |
| Windows 11 x64 | Verified on the development machine |
| Windows 10, 8.1, 8.0, 7 SP1; x86; Arm64; 125/150/200% scaling | **Not yet tested** on real systems or virtual machines |

Windows 7, 8 and 8.1 no longer receive security updates from Microsoft, and .NET Framework 4.6.1 is out of
Microsoft support. The application works there, but those systems are at risk.

## Window

The main window has a fixed size and no title-bar buttons (no minimise, maximise or close). It is closed with the
**Close** button in the window, **File > Exit** or **Alt+F4**. Its size is the design size (1120 x 700 logical pixels)
reduced to fit the screen's working area, so it remains fully visible on small laptop screens at higher scaling.
