# User guide

The same guide is built into the program (**Help > User Guide**, F1).

## The window

- **Header:** product name and supported algorithms.
- **Toolbar:** Add Files, Add Folder, Include subfolders, Remove, Clear List, Open Folder, Algorithm.
- **File list:** File Name, Status, Size, Algorithm, Calculated Hash, Expected Hash, Time, Full Path, Details.
  Click a column header to sort (sorting by Status puts problems first). Right-click a row for more actions.
- **Expected hash** box with Paste and Clear, and a result banner.
- **Actions:** Calculate Hash, Verify, Cancel, Copy Hash, Export.
- **Progress:** current file and overall progress, with completed and failed counts.
- **Close** (bottom right). The window has a fixed size and no title-bar buttons; you can also close it with
  **File > Exit** or **Alt+F4**.
- **Footer:** status messages, portable mode, timing.

## Calculate checksums

1. Add files: **Add Files**, **Add Folder**, drag and drop onto the window, or right-click in File Explorer (if the
   Explorer menu is turned on).
2. Choose the algorithm. SHA-256 is recommended. MD5 and SHA-1 show a warning: use them only to match an old published
   value or to detect accidental damage.
3. **Calculate Hash** (F5). Files already calculated with the same algorithm are not read again; use
   **Tools > Recalculate All** (Ctrl+F5) to force it.
4. **Cancel** (Esc) stops at once. Files are never modified.

Copy results with **Copy Hash** (Ctrl+C) or a double-click. **Copy Hash and File Name** gives `hash  name` lines.

## Verify a download

1. Add the downloaded file.
2. Paste the checksum published by the file's source into **Expected hash**. Spaces, line breaks, letter case and a
   `0x` prefix are ignored. The algorithm is detected from the length (64 characters = SHA-256, 128 = SHA-512,
   96 = SHA-384, 40 = SHA-1, 32 = MD5).
3. Choose **Verify** (F6, or Enter in the box). If several files are in the list, select the one the hash belongs to.

| Result | Meaning | What to do |
|---|---|---|
| **MATCH** | File checksum matches. | The file is identical to the one the publisher hashed. |
| **MISMATCH** | File checksum does not match. | Do not use the file. Download it again from the official source. |
| **ERROR** | File could not be verified. | Read the Details column (for example "in use by another program"). |

Malformed values (wrong length, non-hexadecimal characters, a file name pasted with the hash) are rejected with an
explanation.

## Verify a folder with a manifest

**File > Verify with Checksum Manifest** (Ctrl+M). Choose the `SHA256SUMS`-style file; the folder defaults to the
manifest's own folder. Tick **Complete folder comparison** to also list files the manifest does not mention.
Statuses: MATCH, MISMATCH, **MISSING** (listed but not found), **NOT IN MANIFEST**, **INVALID ENTRY** (unsafe or
malformed line, never read). A summary of problems in the manifest itself is shown first. See
[MANIFEST_FORMATS.md](MANIFEST_FORMATS.md).

## Create a manifest

Calculate hashes, then **Export > Export Checksum Manifest** (Ctrl+E). Save it in the folder that contains the files
(or a parent folder): paths are written relative to it, sorted, in the GNU format that `sha256sum -c` reads. An
existing file is only replaced after you confirm.

## Reports

**Export > Save HTML / Text / CSV Report** (Ctrl+S for HTML). Reports contain the application name and version, date and
time, algorithm, paths, sizes, calculated and expected hashes, statuses, errors, totals, failures and processing time.
CSV files are protected against spreadsheet formula injection; HTML reports contain no scripts.

## Explorer right-click menu

**Settings > Explorer Right-Click Menu** turns "Calculate Hash with HT File Hash Checker" on or off for your Windows
account (no administrator rights needed). Selecting several files opens one window with all of them.

## Settings

**Settings > Options** (Ctrl+,): default algorithm, theme (light or dark), default report format, files read at the same
time (1 for hard disks; 2-4 can help on SSDs), log retention, include subfolders, skip online-only cloud files,
complete folder comparison by default, open reports after saving, confirm before clearing the list.

## Security Advisory History

**Help > About > Security Advisory History** or **Help > Security Advisories** shows HTREASURE security advisories for
this version, from data bundled with the release (it works offline). An empty list is not proof that the software is
free from vulnerabilities. **Export Security Report** saves the assessment as Markdown or JSON.

## Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+O / Ctrl+Shift+O | Add files / add folder |
| F5 / Ctrl+F5 | Calculate / recalculate all |
| F6 | Verify |
| Esc | Cancel |
| Ctrl+C / Ctrl+Shift+C | Copy hash / copy hash and file name |
| Ctrl+A, Delete | Select all, remove from list |
| Ctrl+M / Ctrl+E / Ctrl+S | Verify manifest / export manifest / save HTML report |
| Ctrl+L | Open containing folder |
| F1 | User guide |
| Alt+F4 | Close |
