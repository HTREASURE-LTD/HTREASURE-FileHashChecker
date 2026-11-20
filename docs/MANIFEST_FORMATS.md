# Checksum manifest formats

## Reading

| Format | Example |
|---|---|
| GNU text mode | `e3b0c442...b855  folder/file name.iso` (hash, two spaces, name) |
| GNU binary mode | `e3b0c442...b855 *setup.exe` |
| GNU escaped name | `\e3b0c442...b855  dir/back\\slash.txt` (`\\`, `\n`, `\r` in the name) |
| Single space (some tools) | `e3b0c442...b855 file.txt` |
| BSD / `--tag` | `SHA256 (file.txt) = e3b0c442...b855` |

- Blank lines and lines starting with `#` are ignored.
- Encodings: UTF-8 with or without BOM, UTF-16 with BOM (Windows PowerShell 5 writes UTF-16). Text that is not valid
  UTF-8 is read with the Windows code page and a warning is shown.
- CRLF and LF line endings.
- Limits: 256 MB per manifest, 32 KB per line, 2,000,000 entries.

### Which algorithm

1. The BSD tag (`SHA256 (...)`), if present.
2. The manifest file name: `SHA256SUMS`, `sha512sums.txt`, `MD5SUMS`, `*.sha256`, `*.md5`, ...
3. The hash length.
4. Or choose it explicitly in the verification dialog.

A BSD line that contradicts the file name (an `MD5 (...)` line in `SHA256SUMS`) is rejected.

### Safety rules

Every entry is resolved relative to the chosen folder. These entries are rejected as **INVALID ENTRY** and never read:

- absolute paths (`/etc/passwd`, `\Windows\...`), drive paths (`C:\...`, `C:file`), UNC paths (`\\server\share`);
- `..` traversal anywhere in the path;
- `:` (alternate data streams);
- reserved device names (`CON`, `NUL`, `COM1`, `LPT1`, also with extensions);
- empty or `.` segments (`a//b`, `a/./b`) and names ending in a dot or space, which Windows would silently change;
- wildcard and control characters.

A leading `./` is accepted. Entries that reach outside the folder through a symbolic link or junction are reported as
an error and are not read.

### Duplicates

- The same file listed twice with the same hash: warning, verified once.
- The same file listed with different hashes, including names that differ only in letter case (the same file on
  Windows): both rejected as conflicting.

### Complete folder comparison

When chosen, files in the folder (recursively) that the manifest does not list are reported as **NOT IN MANIFEST**.
The manifest file itself is not reported. Linked folders are not followed.

## Writing

Exports use the GNU format: lower-case hexadecimal, two spaces, relative path with `/` separators, UTF-8 without BOM,
LF line endings, entries sorted by path (ordinal), so the same files always give the same manifest. If the files were
calculated with different algorithms, the BSD tag format is used instead (`sha256sum -c` also reads it). Files outside
the manifest's folder are listed and left out. An existing manifest is only replaced after confirmation, and is written
through a temporary file so it is never left half-written.

## Limitations

- **Line breaks in file names.** The GNU format can escape `\n` and `\r`, but Windows file names cannot contain them, so
  such entries are rejected.
- **Backslashes in names.** On Windows `\` is a folder separator, so `dir\file` and `dir/file` are the same entry.
- **Non-UTF-8 names.** Names in other encodings may not match the file on disk.
- **Other conventions** are reported as invalid lines rather than guessed: tab-separated lines, Windows absolute paths
  written by some tools, `.sfv` (CRC32) and `.md5` files from older Windows tools with different layouts.
- Checksums verify file contents only, not timestamps, permissions or alternate data streams.
