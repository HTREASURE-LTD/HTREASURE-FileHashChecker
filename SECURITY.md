# Security policy

## Reporting a vulnerability

Please report suspected vulnerabilities privately to **support@htreasure.com** with "Security" in the subject, or
through GitHub's private vulnerability reporting for this repository if it is enabled. Include the version (Help >
About), Windows version, steps to reproduce and the impact you expect. Please do not open a public issue.

HTREASURE LTD will acknowledge reports, assess them, and publish confirmed issues as HTREASURE security advisories
(`HTSA-YYYY-NNNN`) through the central advisory registry, so they appear in **Help > Security Advisories** of every
affected version.

## Supported versions

| Version | Supported |
|---|---|
| 1.0.x | Yes |

## Security design

**Untrusted input.** Every path from the command line, drag and drop, dialogs and manifests is validated. Device paths
(`\\.\`), alternate data streams, wildcards and control characters are refused. Manifest entries are resolved inside the
chosen folder only: absolute paths, drive paths, UNC paths, `..` traversal, reserved device names and ambiguous names
are rejected, and the opened file's final path (after symbolic links and junctions) is checked before it is read.

**Files are never changed or run.** Files are opened read-only, streamed and never executed or loaded into memory as
a whole. A file that changes while it is being read is reported as an error, not a result. "Open containing folder"
uses `explorer.exe /select`, which shows the file without opening it.

**Cryptography.** Windows' built-in providers only; no custom algorithms. Expected hashes are compared in constant
time. MD5 and SHA-1 are labelled as legacy.

**Folder scanning.** Linked folders are never followed; depth (128 levels) and file count (1,000,000) are limited;
inaccessible folders are reported and skipped; online-only cloud files are skipped by default.

**Output.** Manifests and reports are written through a temporary file and never overwrite an existing file without
confirmation. HTML reports are HTML-encoded and contain a Content Security Policy with no scripts. CSV values that a
spreadsheet would run as formulas are prefixed with an apostrophe.

**Settings.** XML with DTD processing disabled; invalid values are corrected; a damaged file is set aside and defaults
are used.

**Privacy.** No network access in the application, no telemetry, no personal data. Logs contain file names and error
types only, never file contents, hashes or full paths.

**Privileges.** The program runs as a standard user (`asInvoker`). The optional Explorer menu is a plain registry verb
(no shell extension DLL, no service). Arguments from other instances arrive over a named pipe that only the same user
account can open, with size limits.

**Security Advisory History.** Advisory data is accepted only with a valid ECDSA P-256 signature from a key embedded in
the program, strict schema validation (unknown fields rejected), and rollback protection.

**Distribution.** Release files are signed with HTREASURE LTD's self-signed certificate (see
[docs/CODE-SIGNING.md](docs/CODE-SIGNING.md)) and published with SHA-256 checksums. Setup downloads .NET Framework only
from `download.microsoft.com`, only with consent, and runs it only if both the pinned SHA-256 and Microsoft's signature
match.

## Limits

A checksum shows that two files are identical. It does not establish that a file is safe or authentic: that depends on
the source of the expected value.
