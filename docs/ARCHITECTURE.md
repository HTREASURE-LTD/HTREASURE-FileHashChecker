# Architecture

```
src/
  HTFileHashChecker.Core            domain and application services (no UI)
    Hashing/       HashAlgorithms (Windows providers), Hex (constant-time compare), FileHasher (streaming, read-only)
    Verification/  ExpectedHash (normalisation, validation, detection)
    IO/            PathValidator (untrusted paths), FileScanner (safe recursion), AtomicFile, ErrorDescriber, NativeMethods
    Manifests/     ManifestParser, ManifestWriter, ManifestVerifier (plan: missing / invalid / unexpected)
    Processing/    HashItem, BatchProcessor (bounded background workers), HashWorkspace (application service)
    Reports/       TXT, CSV, HTML writers
    Settings/      AppSettings + SettingsStore (XML), AppPaths (installed vs portable)
    Logging/       ILogger, FileLogger
  HTFileHashChecker.Windows         ExplorerIntegration (registry verb), SingleInstance (mutex + user-only pipe), CommandLine
  HTFileHashChecker.UI              WinForms: Program (composition root, splash, --selftest), MainForm, dialogs, Theme
  HTREASURE.Branding.NetFramework   .NET Framework port of the HTREASURE branding libraries
  HTREASURE.Standards.NetFramework  .NET Framework port of the Security Advisory History client
tests/HTFileHashChecker.Tests       xUnit, net48 and net462
installer/                          Inno Setup script, prerequisite manifest and decisions (reused from HT ISO Ripper)
scripts/                            build-release, sign-file, pin-prerequisites
external/                           ht-software-standards and HT-Software-Template (git submodules)
```

## Flow

1. `Program` parses the command line, enforces a single window per user session (later instances forward their
   arguments over a named pipe) and creates the services (`AppServices`): settings, logger, `HashWorkspace`,
   `ExplorerIntegration`, the standards runtime.
2. `MainForm` event handlers stay thin: they call `HashWorkspace` (`AddPathsAsync`, `PrepareCalculation`,
   `SetExpected`, `PrepareVerification`, `LoadManifestAsync`, `PrepareManifestExport`, `BuildReport`) and display results.
3. `BatchProcessor` hashes items on 1-4 dedicated worker threads (default 1, to avoid parallel disk thrashing),
   reporting throttled progress through `IProgress`. A `CancellationToken` stops reading within one 1 MiB block.
4. `FileHasher` opens read-only (`FileShare.Read`, falling back to shared read if another program is writing), checks
   containment for manifest entries via the handle's final path, streams through the algorithm, and compares the
   file's size and last-write time before and after.
5. Manifests: `ManifestParser` (untrusted text to entries + issues) -> `ManifestVerifier` (entries to a plan with
   missing/invalid/unexpected decided up front) -> `BatchProcessor`.

## Decisions

| Decision | Reason |
|---|---|
| .NET Framework 4.8 + 4.6.1 builds | Windows 7 and 8 support; .NET 10 needs Windows 10 1809+ |
| Ports of the HTREASURE .NET 10 libraries | Same behaviour and conformance tests on .NET Framework; proposed upstream in ht-software-standards PR #1 |
| Windows CryptoServiceProvider classes | FIPS-validated, hardware accelerated, available on every target |
| Single worker by default | Hard disks and network drives are slower with parallel reads |
| Registry verb, no shell extension | No DLL loaded into Explorer, no COM, trivial removal |
| Settings as XML in `%APPDATA%` | Survives upgrades and uninstall; portable mode keeps its own |
| Inno Setup | Native setup runs without .NET; prerequisite logic already proven in HT ISO Ripper |
| No network code in the application | Offline by design; the advisory transport waits for a provisioned service |
