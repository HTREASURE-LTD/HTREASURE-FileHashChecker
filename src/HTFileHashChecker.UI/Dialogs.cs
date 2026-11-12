using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using HTFileHashChecker.Core;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.Reports;
using HTFileHashChecker.Core.Settings;

namespace HTFileHashChecker.UI
{
    /// <summary>Common base: DPI scaling, fonts, Esc to close.</summary>
    internal class DialogBase : Form
    {
        protected DialogBase()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.BaseFont();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = Theme.LoadAppIcon();
            KeyPreview = true;
        }

        protected static TableLayoutPanel Grid(int columns)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columns,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(16),
            };
            return t;
        }

        protected FlowLayoutPanel Buttons(params Button[] buttons)
        {
            var f = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 6, 8, 10),
            };
            f.Controls.AddRange(buttons);
            return f;
        }
    }

    /// <summary>Scrollable list of messages (skipped files, manifest problems) with copy support.</summary>
    internal sealed class MessageListForm : DialogBase
    {
        private MessageListForm(string title, string intro, IEnumerable<string> lines)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(480, 320);
            ClientSize = new Size(720, 420);
            var all = lines.ToList();
            const int max = 5000;
            var shown = all.Take(max).ToList();
            if (all.Count > max) shown.Add("... and " + (all.Count - max) + " more.");

            var label = new Label { Text = intro, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 12, 12, 6), MaximumSize = new Size(700, 0) };
            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F),
                Text = string.Join(Environment.NewLine, shown),
                AccessibleName = "Messages",
            };
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
            host.Controls.Add(box);
            var ok = Ui.Button("OK", Role.Primary, "Close");
            ok.DialogResult = DialogResult.OK;
            var copy = Ui.Button("&Copy", Role.Secondary, "Copy all messages");
            copy.Click += (s, e) => ClipboardHelper.TrySetText(box.Text);
            Controls.Add(host);
            Controls.Add(label);
            Controls.Add(Buttons(ok, copy));
            AcceptButton = ok;
            CancelButton = ok;
        }

        public static void Show(IWin32Window owner, string title, string intro, IEnumerable<string> lines)
        {
            using (var f = new MessageListForm(title, intro, lines))
            {
                Theme.Apply(f, Theme.Current);
                f.ShowDialog(owner);
            }
        }
    }

    /// <summary>Choose a manifest, the folder it refers to and whether to report unexpected files.</summary>
    internal sealed class ManifestVerifyForm : DialogBase
    {
        public sealed class Choice
        {
            public string ManifestPath;
            public string BaseDirectory;
            public HashAlgorithmKind? Algorithm;
            public bool ReportUnexpected;
        }

        private readonly TextBox txtManifest, txtFolder;
        private readonly ComboBox cboAlgorithm;
        private readonly CheckBox chkUnexpected;

        public ManifestVerifyForm(bool reportUnexpected)
        {
            Text = "Verify with Checksum Manifest";
            var t = Grid(3);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380F));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var intro = new Label
            {
                Text = "Choose a checksum file such as SHA256SUMS, SHA512SUMS, MD5SUMS or file.iso.sha256. "
                    + "Each listed file is read and compared with its expected hash. Files are never modified.",
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(0, 0, 0, 10),
            };
            t.Controls.Add(intro, 0, 0);
            t.SetColumnSpan(intro, 3);

            txtManifest = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Manifest file" };
            var browseManifest = Ui.Button("&Browse...", Role.Secondary, "Choose the manifest file");
            browseManifest.Click += (s, e) => BrowseManifest();
            t.Controls.Add(Ui.Label("&Manifest file:"), 0, 1);
            t.Controls.Add(txtManifest, 1, 1);
            t.Controls.Add(browseManifest, 2, 1);

            txtFolder = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Files folder" };
            var browseFolder = Ui.Button("B&rowse...", Role.Secondary, "Choose the folder containing the listed files");
            browseFolder.Click += (s, e) => BrowseFolder();
            t.Controls.Add(Ui.Label("&Files folder:"), 0, 2);
            t.Controls.Add(txtFolder, 1, 2);
            t.Controls.Add(browseFolder, 2, 2);

            var folderHint = new Label { Text = "Usually the manifest's own folder (filled in automatically).", AutoSize = true, Tag = Role.Muted, Margin = new Padding(0, 0, 0, 8) };
            t.Controls.Add(folderHint, 1, 3);

            cboAlgorithm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Manifest algorithm" };
            cboAlgorithm.Items.Add("Detect automatically");
            foreach (var info in HashAlgorithms.All) cboAlgorithm.Items.Add(info);
            cboAlgorithm.SelectedIndex = 0;
            t.Controls.Add(Ui.Label("&Algorithm:"), 0, 4);
            t.Controls.Add(cboAlgorithm, 1, 4);

            chkUnexpected = new CheckBox
            {
                Text = "&Complete folder comparison: also list files in the folder that the manifest does not mention",
                AutoSize = true,
                Checked = reportUnexpected,
                Margin = new Padding(0, 8, 0, 0),
                MaximumSize = new Size(560, 0),
            };
            t.Controls.Add(chkUnexpected, 0, 5);
            t.SetColumnSpan(chkUnexpected, 3);

            var ok = Ui.Button("&Verify", Role.Primary, "Start verification");
            ok.Click += (s, e) => Accept();
            var cancel = Ui.Button("Cancel", Role.Secondary, "Close without verifying");
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(t);
            Controls.Add(Buttons(cancel, ok));
            AcceptButton = ok;
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        public Choice Result { get; private set; }

        private void BrowseManifest()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose checksum manifest",
                Filter = "Checksum files|*SUMS*;*sums*;*.sha256;*.sha512;*.sha384;*.sha1;*.md5;*.txt|All files (*.*)|*.*",
                CheckFileExists = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                txtManifest.Text = dlg.FileName;
                txtFolder.Text = Path.GetDirectoryName(dlg.FileName);
                HashAlgorithmKind kind;
                if (HashAlgorithms.TryDetectFromFileName(dlg.FileName, out kind)) cboAlgorithm.SelectedIndex = 0;
            }
        }

        private void BrowseFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose the folder that contains the files listed in the manifest.", ShowNewFolderButton = false })
            {
                if (Directory.Exists(txtFolder.Text)) dlg.SelectedPath = txtFolder.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtFolder.Text = dlg.SelectedPath;
            }
        }

        private void Accept()
        {
            string manifest = txtManifest.Text.Trim();
            if (!File.Exists(manifest))
            {
                MessageBox.Show(this, "Choose an existing manifest file.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtManifest.Focus();
                return;
            }
            string folder = txtFolder.Text.Trim();
            if (folder.Length == 0) folder = Path.GetDirectoryName(Path.GetFullPath(manifest));
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(this, "The files folder does not exist.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtFolder.Focus();
                return;
            }
            var info = cboAlgorithm.SelectedItem as HashAlgorithmInfo;
            Result = new Choice
            {
                ManifestPath = manifest,
                BaseDirectory = folder,
                Algorithm = info != null ? info.Kind : (HashAlgorithmKind?)null,
                ReportUnexpected = chkUnexpected.Checked,
            };
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Options dialog.</summary>
    internal sealed class SettingsForm : DialogBase
    {
        private readonly ComboBox cboAlgorithm, cboTheme, cboReport;
        private readonly CheckBox chkSubfolders, chkSkipCloud, chkUnexpected, chkOpenReport, chkConfirmClear, chkExplorer;
        private readonly NumericUpDown numParallel, numLogDays;

        public SettingsForm(AppSettings settings, bool explorerEnabled, AppPaths paths)
        {
            Settings = settings;
            Text = "Options";
            var t = Grid(2);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int row = 0;

            cboAlgorithm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Default algorithm" };
            foreach (var info in HashAlgorithms.All) cboAlgorithm.Items.Add(info);
            cboAlgorithm.SelectedItem = HashAlgorithms.Get(settings.DefaultAlgorithm);
            AddRow(t, ref row, "Default &algorithm:", cboAlgorithm);

            cboTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Theme" };
            cboTheme.Items.AddRange(new object[] { "Light", "Dark" });
            cboTheme.SelectedIndex = settings.Theme == ThemePreference.Dark ? 1 : 0;
            AddRow(t, ref row, "&Theme:", cboTheme);

            cboReport = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Default report format" };
            cboReport.Items.AddRange(new object[] { "Text (TXT)", "CSV", "HTML" });
            cboReport.SelectedIndex = (int)settings.DefaultReportFormat;
            AddRow(t, ref row, "Default &report format:", cboReport);

            numParallel = new NumericUpDown { Minimum = 1, Maximum = 4, Value = settings.MaxParallelFiles, Width = 70, AccessibleName = "Files read at the same time" };
            AddRow(t, ref row, "Files read at the &same time:", numParallel);
            var parHint = new Label { Text = "1 is best for hard disks and network drives; 2-4 can be faster on SSDs.", AutoSize = true, Tag = Role.Muted };
            t.Controls.Add(parHint, 1, row++);

            numLogDays = new NumericUpDown { Minimum = 1, Maximum = 365, Value = settings.LogRetentionDays, Width = 70, AccessibleName = "Keep log files for days" };
            AddRow(t, ref row, "Keep &log files (days):", numLogDays);

            chkSubfolders = Check(t, ref row, "Include s&ubfolders when adding folders", settings.IncludeSubfolders);
            chkSkipCloud = Check(t, ref row, "Skip online-only cloud files (avoids downloading them)", settings.SkipOnlineOnlyFiles);
            chkUnexpected = Check(t, ref row, "Manifest verification: report files &not listed in the manifest", settings.ReportUnexpectedFilesByDefault);
            chkOpenReport = Check(t, ref row, "&Open reports after saving", settings.OpenReportAfterSaving);
            chkConfirmClear = Check(t, ref row, "Ask before &clearing the list", settings.ConfirmBeforeClearing);
            chkExplorer = Check(t, ref row, "Show \"Calculate Hash with HT File Hash Checker\" in the &Explorer right-click menu", explorerEnabled);

            var where = new Label
            {
                Text = "Settings file: " + paths.SettingsFile + (paths.IsPortable ? "  (portable mode)" : string.Empty),
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Tag = Role.Muted,
                Margin = new Padding(0, 10, 0, 0),
            };
            t.Controls.Add(where, 0, row);
            t.SetColumnSpan(where, 2);

            var ok = Ui.Button("OK", Role.Primary, "Save settings");
            ok.Click += (s, e) => Accept();
            var cancel = Ui.Button("Cancel", Role.Secondary, "Close without saving");
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(t);
            Controls.Add(Buttons(cancel, ok));
            AcceptButton = ok;
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        public AppSettings Settings { get; private set; }
        public bool ExplorerIntegration { get; private set; }

        private static void AddRow(TableLayoutPanel t, ref int row, string label, Control control)
        {
            control.Margin = new Padding(0, 3, 0, 3);
            t.Controls.Add(Ui.Label(label), 0, row);
            t.Controls.Add(control, 1, row);
            row++;
        }

        private static CheckBox Check(TableLayoutPanel t, ref int row, string text, bool value)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 0), MaximumSize = new Size(560, 0) };
            t.Controls.Add(c, 0, row);
            t.SetColumnSpan(c, 2);
            row++;
            return c;
        }

        private void Accept()
        {
            Settings.DefaultAlgorithm = ((HashAlgorithmInfo)cboAlgorithm.SelectedItem).Kind;
            Settings.Theme = cboTheme.SelectedIndex == 1 ? ThemePreference.Dark : ThemePreference.Light;
            Settings.DefaultReportFormat = (ReportFormat)cboReport.SelectedIndex;
            Settings.MaxParallelFiles = (int)numParallel.Value;
            Settings.LogRetentionDays = (int)numLogDays.Value;
            Settings.IncludeSubfolders = chkSubfolders.Checked;
            Settings.SkipOnlineOnlyFiles = chkSkipCloud.Checked;
            Settings.ReportUnexpectedFilesByDefault = chkUnexpected.Checked;
            Settings.OpenReportAfterSaving = chkOpenReport.Checked;
            Settings.ConfirmBeforeClearing = chkConfirmClear.Checked;
            Settings.ExplorerIntegrationEnabled = chkExplorer.Checked;
            Settings.Normalize();
            ExplorerIntegration = chkExplorer.Checked;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Built-in help (works offline).</summary>
    internal sealed class HelpForm : DialogBase
    {
        public enum Topic { Guide, Algorithms, Manifests }

        public HelpForm(Topic topic)
        {
            Text = AppInfo.ProductName + " Help";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(520, 380);
            ClientSize = new Size(760, 560);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("User Guide", HelpText.Guide));
            tabs.TabPages.Add(Page("Hash Algorithms", HelpText.Algorithms));
            tabs.TabPages.Add(Page("Manifest Formats", HelpText.Manifests));
            tabs.SelectedIndex = (int)topic;
            var close = Ui.Button("Close", Role.Primary, "Close help");
            close.DialogResult = DialogResult.OK;
            Controls.Add(tabs);
            Controls.Add(Buttons(close));
            CancelButton = close;
        }

        private static TabPage Page(string title, string text)
        {
            var page = new TabPage(title) { Padding = new Padding(8) };
            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = text.Replace("\n", Environment.NewLine),
                BorderStyle = BorderStyle.None,
                AccessibleName = title,
                Font = new Font("Segoe UI", 10F),
            };
            page.Controls.Add(box);
            return page;
        }
    }

    internal static class HelpText
    {
        public const string Guide =
@"CALCULATE A HASH
1. Add files: Add Files..., Add Folder..., drag and drop, or right-click files in File Explorer (if enabled).
2. Choose an algorithm. SHA-256 is recommended.
3. Choose Calculate Hash (F5). Progress is shown for the current file and overall. Cancel (Esc) stops safely.
4. Copy a hash with Copy Hash (Ctrl+C) or double-click a row. Save results with Export.

VERIFY A DOWNLOAD
1. Add the downloaded file.
2. Paste the checksum published by the file's source into Expected hash. Spaces, line breaks and letter case are ignored; the algorithm is detected from the length.
3. Choose Verify (F6, or press Enter in the box).
   MATCH    - File checksum matches.
   MISMATCH - File checksum does not match. Do not use the file; download it again.
   ERROR    - File could not be verified (for example, it is locked or unreadable).

VERIFY MANY FILES WITH A MANIFEST
File > Verify with Checksum Manifest... reads SHA256SUMS, SHA512SUMS, MD5SUMS and similar files, checks every listed file, and reports MATCH, MISMATCH, MISSING and INVALID ENTRY. Tick Complete folder comparison to also list files the manifest does not mention.

CREATE A MANIFEST
Calculate hashes, then choose Export > Export Checksum Manifest. Save it in the folder that contains the files (or a parent folder); paths are written relative to it, sorted, in the GNU coreutils format readable by sha256sum -c.

REPORTS
Export > Save HTML / Text / CSV Report records application version, date and time, algorithm, paths, sizes, calculated and expected hashes, status, errors and totals.

IMPORTANT
A matching checksum shows the file is identical to the one the expected value was calculated from. It does not prove the file is free of malware or comes from a trusted publisher. Obtain expected values from a trusted source, ideally the publisher's HTTPS website or a signed checksum file.

PRIVACY
Everything works offline. No file contents or hashes are uploaded, and no telemetry is collected.

KEYBOARD
Ctrl+O add files · Ctrl+Shift+O add folder · F5 calculate · Ctrl+F5 recalculate all · F6 verify · Esc cancel · Ctrl+C copy hash · Ctrl+A select all · Delete remove · Ctrl+M verify manifest · Ctrl+E export manifest · Ctrl+S save HTML report · Ctrl+L open folder · F1 help";

        public const string Algorithms =
@"SHA-256 (recommended, default)
64 hexadecimal characters. Widely published by software vendors and Linux distributions. Suitable for integrity verification.

SHA-512
128 hexadecimal characters. Same security family as SHA-256; often faster on 64-bit computers.

SHA-384
96 hexadecimal characters. A truncated variant of SHA-512.

SHA-1 (legacy)
40 hexadecimal characters. Practical collision attacks exist. Use only to compare with a value a publisher still provides in SHA-1, or to detect accidental corruption.

MD5 (legacy)
32 hexadecimal characters. Collisions can be produced easily. Not suitable for security-sensitive verification; useful only for detecting accidental corruption or matching old published values. MD5 may be unavailable on computers where Windows enforces FIPS-only algorithms.

All algorithms use Microsoft's built-in Windows cryptographic implementations. Expected and calculated hashes are compared in constant time.";

        public const string Manifests =
@"SUPPORTED READING FORMATS
GNU coreutils (sha256sum, sha512sum, md5sum...):
    <hash>  <file name>        text mode
    <hash> *<file name>        binary mode
    \<hash>  <escaped name>    names containing \ or line breaks (\\ \n \r)
BSD / --tag:
    SHA256 (<file name>) = <hash>
Lines starting with # are comments. UTF-8 (with or without BOM) and UTF-16 with BOM are accepted.

ALGORITHM DETECTION
Taken from the BSD tag, otherwise from the file name (SHA256SUMS, *.sha512, MD5SUMS...), otherwise from the hash length. You can also choose it explicitly.

SAFETY RULES
Paths are resolved relative to the chosen folder. These entries are rejected (INVALID ENTRY): absolute paths, drive letters, UNC paths, '..' traversal, ':' (alternate data streams), reserved device names (CON, NUL, COM1...), names ending in a dot or space, empty or '.' segments, and wildcard or control characters. Entries resolving outside the folder through symbolic links or junctions are not read. Duplicate lines with the same hash are ignored with a warning; conflicting duplicates (including names differing only in letter case) are rejected.

WRITING
Exports use the GNU format with '/' separators, UTF-8 without BOM, LF line endings and entries sorted by path. Existing files are never overwritten without confirmation. Mixed algorithms are written in BSD tag format.

LIMITATIONS
Windows file names cannot contain line breaks, so escaped names containing \n or \r cannot match a file. File names that are not valid UTF-8 cannot be represented. Some tools write manifests in other conventions (for example hash and name separated by a tab, or Windows absolute paths); these are reported as invalid lines rather than guessed.";
    }

    internal sealed class AboutForm : DialogBase
    {
        public AboutForm(AppPaths paths)
        {
            Text = "About " + AppInfo.ProductName;
            var t = Grid(2);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var logo = new PictureBox { Image = Theme.LoadImage("ht_logo.png"), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96), Margin = new Padding(0, 0, 16, 0), AccessibleName = "Logo" };
            var info = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Text = AppInfo.ProductName + "\nVersion " + AppInfo.Version + "  (" + (Environment.Is64BitProcess ? "64-bit" : "32-bit") + (paths.IsPortable ? ", portable" : string.Empty) + ")\n\n"
                    + AppInfo.Copyright + "\nPublished by " + AppInfo.Publisher + "\n\n"
                    + "Calculates, compares and verifies file checksums using SHA-256, SHA-512, SHA-384, SHA-1 and MD5.\n\n"
                    + "Works offline. No telemetry. No file contents or hashes leave this computer.\n\n"
                    + ".NET Framework " + Environment.Version + " on " + Environment.OSVersion.VersionString,
            };
            var title = new Label { Text = AppInfo.ProductName, AutoSize = true, Font = new Font("Segoe UI Semibold", 14F), Margin = new Padding(0, 0, 0, 6) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
            info.Text = info.Text.Substring(AppInfo.ProductName.Length + 1);
            stack.Controls.Add(title);
            stack.Controls.Add(info);
            var licence = new LinkLabel { Text = "View licence and third-party notices", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            licence.LinkClicked += (s, e) => OpenLicence();
            stack.Controls.Add(licence);
            t.Controls.Add(logo, 0, 0);
            t.Controls.Add(stack, 1, 0);
            var ok = Ui.Button("OK", Role.Primary, "Close");
            ok.DialogResult = DialogResult.OK;
            Controls.Add(t);
            Controls.Add(Buttons(ok));
            AcceptButton = ok;
            CancelButton = ok;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        private void OpenLicence()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (var name in new[] { "LICENSE.txt", "THIRD_PARTY_NOTICES.txt" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p))
                {
                    try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + p + "\"") { UseShellExecute = false }); }
                    catch (System.ComponentModel.Win32Exception) { }
                    return;
                }
            }
            MessageBox.Show(this, "The licence file was not found next to the program.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
