# Hash algorithms

| Algorithm | Hex length | Status | Use it for |
|---|---|---|---|
| **SHA-256** (default) | 64 | Recommended | Verifying downloads, ISO images, backups. The value most publishers give |
| **SHA-512** | 128 | Recommended | The same; often faster on 64-bit computers |
| **SHA-384** | 96 | Recommended | When a publisher provides SHA-384 |
| **SHA-1** | 40 | Legacy | Matching an old published SHA-1 value; detecting accidental damage |
| **MD5** | 32 | Legacy | Matching an old published MD5 value; detecting accidental damage |

## Why MD5 and SHA-1 are "legacy"

Practical collision attacks exist for both: someone can create two different files with the same MD5 or SHA-1
checksum. A matching MD5 or SHA-1 therefore cannot show that a file was not deliberately replaced. The program marks
these algorithms with a warning and the reports repeat it.

## What a checksum proves

A checksum compares a file with the file the expected value was calculated from. If they match (with SHA-256, SHA-384
or SHA-512), the files are identical. It does not prove who made the file or that it is safe. If an attacker can change
both the download and the published checksum (for example on the same compromised web page), the checksums will still
match. Get the expected value from a trusted place: the publisher's HTTPS website, a signed checksum file, or a second
independent source.

## Implementation

- Microsoft's built-in Windows cryptographic providers (`SHA256CryptoServiceProvider` and the matching classes). No
  custom cryptography.
- Files are read in 1 MiB blocks and streamed through the algorithm, so size is limited only by the file system.
- Expected and calculated digests are compared in constant time.
- Correctness is checked against published test vectors: FIPS 180 (SHA-1, SHA-256, SHA-384, SHA-512, including one
  million "a" characters) and RFC 1321 (MD5); see [TEST_REPORT.md](TEST_REPORT.md).
- On computers where Windows enforces FIPS-approved algorithms only, MD5 may be unavailable; the program then says so
  and suggests SHA-256.
