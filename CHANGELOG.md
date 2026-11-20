# Changelog

All notable changes to HT File Hash Checker. Versions follow [Semantic Versioning](https://semver.org/).

## [1.0.0] - unreleased

First release.

### Added
- SHA-256 (default), SHA-512, SHA-384, SHA-1 and MD5 checksums of files, multiple files and folders (optionally
  recursive), with drag and drop, using Windows' cryptographic providers and streaming reads (files over 4 GB).
- Verification against an expected hash: whitespace and letter case normalised, algorithm detected from the length,
  constant-time comparison, MATCH / MISMATCH / ERROR results.
- Background batch processing with per-file and overall progress, counts, safe cancellation and a summary.
- Checksum manifests: GNU (`SHA256SUMS` etc., text, binary, escaped) and BSD tag formats; missing, modified, duplicate,
  conflicting and unexpected files; path-traversal and unsafe-path protection; deterministic, atomic export.
- TXT, CSV and HTML reports.
- Optional Explorer right-click menu for files and folders; one window for multi-file selections.
- Settings (default algorithm, theme, report format, parallel reads, subfolders, log retention and more), kept across
  upgrades; portable mode with separate settings.
- HTREASURE branding (template kit), splash screen, standard About box, light and dark themes, fixed-size window with an
  in-window Close button.
- HTREASURE Security Advisory History (Help > About > Security Advisory History, Help > Security Advisories), working
  offline from a bundled snapshot.
- Windows 7 SP1 / 8.1 / 10 / 11 build (.NET Framework 4.8) and Windows 8.0 build (.NET Framework 4.6.1).
- Inno Setup installer with prerequisite detection and consent-based .NET download from Microsoft; portable ZIP;
  self-signed code signing; release packaging with SHA-256 checksums.
- 185 automated tests per framework.

[1.0.0]: https://github.com/HTREASURE-LTD/HTREASURE-FileHashChecker
