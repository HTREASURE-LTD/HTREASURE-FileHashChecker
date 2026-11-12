using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using HTFileHashChecker.Core;
using HTFileHashChecker.Core.Hashing;

namespace HTFileHashChecker.UI
{
    internal sealed partial class MainForm
    {
        internal const int ColName = 0, ColPath = 1, ColSize = 2, ColAlgorithm = 3, ColHash = 4, ColExpected = 5, ColStatus = 6, ColTime = 7, ColDetails = 8;

        private MenuStrip menu;
        private TableLayoutPanel header;
        private TableLayoutPanel mainTable;
        private FlowLayoutPanel toolbar;
        internal Button btnAddFiles, btnAddFolder, btnRemove, btnClear, btnOpenFolder;
        internal CheckBox chkSubfolders;
        internal ComboBox cboAlgorithm;
        private Label lblAlgorithmNote;
        private Panel listHost;
        internal ListView list;
        private Panel dropZone;
        private TextBox txtExpected;
        private Button btnPaste, btnClearExpected;
        private Label lblResult;
        internal Button btnCalculate, btnVerify, btnCancel, btnCopy, btnExport;
        private Label lblCounts;
        private ProgressBar pbFile, pbOverall;
        private Label lblFile, lblOverall;
        private StatusStrip status;
        private ToolStripStatusLabel stsText, stsMode, stsElapsed;
        private ToolTip tips;
        private ContextMenuStrip listMenu, exportMenu;

        // Menu items whose state changes with selection / busy state.
        private ToolStripMenuItem miAddFiles, miAddFolder, miImportManifest, miExportManifest, miSaveReport, miCalculate, miRecalculate,
            miVerify, miCancel, miCopyHash, miCopyHashName, miCopyPath, miOpenFolder, miRemove, miClear, miSelectAll, miSetExpected,
            miSubfolders, miThemeLight, miThemeDark, miExplorer;

        private void BuildLayout()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.BaseFont();
            Text = AppInfo.ProductName;
            Icon = Theme.LoadAppIcon();
            MinimumSize = new Size(720, 540);
            ClientSize = new Size(1120, 720);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            KeyPreview = true;
            AccessibleName = AppInfo.ProductName;
            tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ShowAlways = true };

            menu = BuildMenu();
            header = BuildHeader();

            mainTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 10, 12, 6) };
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTable.Controls.Add(BuildToolbar(), 0, 0);
            mainTable.Controls.Add(BuildList(), 0, 1);
            mainTable.Controls.Add(BuildExpectedPanel(), 0, 2);
            mainTable.Controls.Add(BuildActions(), 0, 3);
            mainTable.Controls.Add(BuildProgress(), 0, 4);

            status = BuildStatus();

            // Docking is applied in reverse order of addition: status (bottom), menu (top), header (top), table (fill).
            Controls.Add(mainTable);
            Controls.Add(header);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;
            ResumeLayout(false);
            PerformLayout();
        }

        private TableLayoutPanel BuildHeader()
        {
            var h = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(12, 8, 16, 8),
                Tag = Role.Header,
            };
            h.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            h.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var logo = new PictureBox
            {
                Image = Theme.LoadImage("ht_logo_64.png"),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(44, 44),
                Margin = new Padding(0, 0, 10, 0),
                AccessibleRole = AccessibleRole.Graphic,
                AccessibleName = "HT File Hash Checker logo",
                Tag = Role.HeaderText,
            };
            var title = new Label
            {
                Text = AppInfo.ProductName,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Regular, GraphicsUnit.Point),
                Margin = new Padding(0, 0, 0, 0),
                Tag = Role.HeaderText,
            };
            var subtitle = new Label
            {
                Text = "Calculate, compare and verify file checksums",
                AutoSize = true,
                Margin = new Padding(2, 0, 0, 0),
                Tag = Role.HeaderText,
            };
            var company = new Label
            {
                Text = AppInfo.Publisher,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                Tag = Role.HeaderText,
            };
            h.Controls.Add(logo, 0, 0);
            h.SetRowSpan(logo, 2);
            h.Controls.Add(title, 1, 0);
            h.Controls.Add(subtitle, 1, 1);
            h.Controls.Add(company, 2, 0);
            h.SetRowSpan(company, 2);
            return h;
        }

        private FlowLayoutPanel BuildToolbar()
        {
            toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4),
            };
            btnAddFiles = Ui.Button("&Add Files...", Role.Secondary, "Choose one or more files to hash");
            btnAddFolder = Ui.Button("Add &Folder...", Role.Secondary, "Choose a folder; its files are added");
            chkSubfolders = new CheckBox { Text = "Include s&ubfolders", AutoSize = true, Margin = new Padding(0, 7, 16, 0), AccessibleName = "Include subfolders" };
            btnRemove = Ui.Button("&Remove", Role.Secondary, "Remove the selected files from the list");
            btnClear = Ui.Button("Clear &List", Role.Secondary, "Remove all files from the list");
            btnOpenFolder = Ui.Button("Open Folder", Role.Secondary, "Open the selected file's folder in File Explorer");
            var lblAlg = Ui.Label("Al&gorithm:");
            cboAlgorithm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new Padding(0, 3, 8, 0), AccessibleName = "Hash algorithm" };
            foreach (var info in HashAlgorithms.All) cboAlgorithm.Items.Add(new AlgorithmChoice(info));
            lblAlgorithmNote = new Label { AutoSize = true, Margin = new Padding(0, 7, 0, 0), MaximumSize = new Size(420, 0) };

            tips.SetToolTip(btnAddFiles, "Add files (Ctrl+O)");
            tips.SetToolTip(btnAddFolder, "Add all files in a folder (Ctrl+Shift+O)");
            tips.SetToolTip(chkSubfolders, "When adding a folder, also add files in its subfolders");
            tips.SetToolTip(btnRemove, "Remove selected files from the list (Delete). Files on disk are not changed.");
            tips.SetToolTip(btnClear, "Remove all files from the list. Files on disk are not changed.");
            tips.SetToolTip(btnOpenFolder, "Show the selected file in File Explorer");
            tips.SetToolTip(cboAlgorithm, "SHA-256 is recommended. MD5 and SHA-1 are for legacy compatibility only.");

            toolbar.Controls.AddRange(new Control[] { btnAddFiles, btnAddFolder, chkSubfolders, btnRemove, btnClear, btnOpenFolder });
            toolbar.Controls.Add(Spacer(8));
            toolbar.Controls.AddRange(new Control[] { lblAlg, cboAlgorithm, lblAlgorithmNote });
            toolbar.SetFlowBreak(btnOpenFolder, false);
            return toolbar;
        }

        private static Control Spacer(int width)
        {
            return new Panel { Width = width, Height = 1, Margin = Padding.Empty };
        }

        private Panel BuildList()
        {
            listHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6), AllowDrop = true };
            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                VirtualMode = true,
                OwnerDraw = true,
                AllowDrop = true,
                ShowItemToolTips = true,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "File list",
                AccessibleDescription = "Files, their hashes and verification status",
            };
            AddColumn("File Name", 200, HorizontalAlignment.Left);
            AddColumn("Full Path", 280, HorizontalAlignment.Left);
            AddColumn("Size", 80, HorizontalAlignment.Right);
            AddColumn("Algorithm", 78, HorizontalAlignment.Left);
            AddColumn("Calculated Hash", 430, HorizontalAlignment.Left);
            AddColumn("Expected Hash", 200, HorizontalAlignment.Left);
            AddColumn("Status", 120, HorizontalAlignment.Left);
            AddColumn("Time", 70, HorizontalAlignment.Right);
            AddColumn("Details", 280, HorizontalAlignment.Left);
            // Display order keeps the status next to the file name on small screens; data order stays as above.
            int[] order = { ColName, ColStatus, ColSize, ColAlgorithm, ColHash, ColExpected, ColTime, ColPath, ColDetails };
            for (int d = 0; d < order.Length; d++) list.Columns[order[d]].DisplayIndex = d;

            dropZone = new Panel { Dock = DockStyle.Fill, AllowDrop = true, Tag = Role.Card, AccessibleName = "Drop zone" };
            var dropLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Drag and drop files or folders here\r\n\r\nor use Add Files... / Add Folder...  (double-click to add files)",
                Tag = Role.Muted,
                AllowDrop = true,
                AccessibleName = "Drag and drop files or folders here",
            };
            dropZone.Controls.Add(dropLabel);
            dropZone.Paint += PaintDropZone;
            dropZone.Resize += (s, e) => dropZone.Invalidate();
            dropLabel.DoubleClick += (s, e) => AddFilesInteractive();
            dropLabel.DragEnter += OnDragEnter;
            dropLabel.DragDrop += OnDragDrop;

            listHost.Controls.Add(list);
            listHost.Controls.Add(dropZone);
            return listHost;
        }

        private void AddColumn(string text, int logicalWidth, HorizontalAlignment align)
        {
            list.Columns.Add(new ColumnHeader { Text = text, Width = logicalWidth, TextAlign = align });
        }

        private void PaintDropZone(object sender, PaintEventArgs e)
        {
            var r = dropZone.ClientRectangle;
            r.Inflate(-LogicalToDeviceUnits(6), -LogicalToDeviceUnits(6));
            using (var pen = new Pen(Theme.Current.Border, Math.Max(1, LogicalToDeviceUnits(2))) { DashStyle = DashStyle.Dash })
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(pen, r);
            }
        }

        private TableLayoutPanel BuildExpectedPanel()
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 6),
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var label = Ui.Label("&Expected hash:");
            txtExpected = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 8, 0),
                Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point),
                AccessibleName = "Expected hash",
                AccessibleDescription = "Paste the checksum published by the file's source, then choose Verify",
                MaxLength = 4096,
            };
            btnPaste = Ui.Button("&Paste", Role.Secondary, "Paste the expected hash from the clipboard");
            btnClearExpected = Ui.Button("Clear", Role.Secondary, "Clear the expected hash");
            btnPaste.Margin = new Padding(0, 0, 8, 0);
            btnClearExpected.Margin = new Padding(0);
            lblResult = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 0),
                Padding = new Padding(8, 5, 8, 5),
                Visible = false,
                Tag = Role.Banner,
                AccessibleRole = AccessibleRole.StatusBar,
                AccessibleName = "Verification result",
            };
            tips.SetToolTip(txtExpected, "Paste the checksum published by the file's source. Spaces, line breaks and letter case are ignored; the algorithm is detected from the length.");
            t.Controls.Add(label, 0, 0);
            t.Controls.Add(txtExpected, 1, 0);
            t.Controls.Add(btnPaste, 2, 0);
            t.Controls.Add(btnClearExpected, 3, 0);
            t.Controls.Add(lblResult, 0, 1);
            t.SetColumnSpan(lblResult, 4);
            return t;
        }

        private FlowLayoutPanel BuildActions()
        {
            // A single wrapping flow directly in the main table (a nested percentage column would be measured at
            // zero width and reserve the height of fully wrapped buttons).
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0) };
            btnCalculate = Ui.Button("&Calculate Hash", Role.Primary, "Calculate hashes for the files in the list (F5)");
            btnVerify = Ui.Button("&Verify", Role.Primary, "Compare calculated hashes with expected hashes (F6)");
            btnCancel = Ui.Button("Cance&l", Role.Secondary, "Stop the current operation safely (Esc)");
            btnCopy = Ui.Button("C&opy Hash", Role.Secondary, "Copy the selected hashes to the clipboard (Ctrl+C)");
            btnExport = Ui.Button("E&xport ▾", Role.Secondary, "Save a report or a checksum manifest");
            btnCalculate.MinimumSize = new Size(128, 32);
            btnVerify.MinimumSize = new Size(100, 32);
            tips.SetToolTip(btnCalculate, "Calculate hashes (F5)");
            tips.SetToolTip(btnVerify, "Verify against the expected hash or an imported manifest (F6)");
            tips.SetToolTip(btnCancel, "Cancel (Esc). Files are never modified.");
            tips.SetToolTip(btnCopy, "Copy selected hashes (Ctrl+C)");
            tips.SetToolTip(btnExport, "Save a TXT, CSV or HTML report, or a SHA256SUMS-style manifest");
            flow.Controls.AddRange(new Control[] { btnCalculate, btnVerify, btnCancel, btnCopy, btnExport });
            lblCounts = new Label { AutoSize = true, Margin = new Padding(8, 8, 0, 0), Tag = Role.Muted, AccessibleName = "Summary" };
            flow.Controls.Add(lblCounts);
            return flow;
        }

        private TableLayoutPanel BuildProgress()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 2, 0, 0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));
            lblFile = new Label { AutoSize = true, AutoEllipsis = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 8, 3), Text = "Current file: -", AccessibleName = "Current file progress" };
            lblOverall = new Label { AutoSize = true, AutoEllipsis = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 8, 3), Text = "Overall: -", AccessibleName = "Overall progress" };
            pbFile = new ProgressBar { Dock = DockStyle.Fill, Maximum = 1000, Height = 16, Margin = new Padding(0, 3, 0, 3), AccessibleName = "Current file progress" };
            pbOverall = new ProgressBar { Dock = DockStyle.Fill, Maximum = 1000, Height = 16, Margin = new Padding(0, 3, 0, 3), AccessibleName = "Overall progress" };
            t.Controls.Add(lblFile, 0, 0);
            t.Controls.Add(pbFile, 1, 0);
            t.Controls.Add(lblOverall, 0, 1);
            t.Controls.Add(pbOverall, 1, 1);
            return t;
        }

        private StatusStrip BuildStatus()
        {
            var s = new StatusStrip { SizingGrip = true, AccessibleName = "Status" };
            stsText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready" };
            stsMode = new ToolStripStatusLabel { Text = string.Empty };
            stsElapsed = new ToolStripStatusLabel { Text = string.Empty };
            s.Items.AddRange(new ToolStripItem[] { stsText, stsMode, stsElapsed });
            return s;
        }

        private MenuStrip BuildMenu()
        {
            var m = new MenuStrip { AccessibleName = "Main menu" };

            var file = new ToolStripMenuItem("&File");
            miAddFiles = Item("&Add Files...", Keys.Control | Keys.O, (s, e) => AddFilesInteractive());
            miAddFolder = Item("Add &Folder...", Keys.Control | Keys.Shift | Keys.O, (s, e) => AddFolderInteractive());
            miImportManifest = Item("&Verify with Checksum Manifest...", Keys.Control | Keys.M, (s, e) => ImportManifestInteractive());
            miExportManifest = Item("&Export Checksum Manifest...", Keys.Control | Keys.E, (s, e) => ExportManifestInteractive());
            miSaveReport = new ToolStripMenuItem("Save &Report");
            miSaveReport.DropDownItems.Add(Item("&HTML Report...", Keys.Control | Keys.S, (s, e) => SaveReportInteractive(Core.Reports.ReportFormat.Html)));
            miSaveReport.DropDownItems.Add(Item("&Text Report (TXT)...", Keys.None, (s, e) => SaveReportInteractive(Core.Reports.ReportFormat.Text)));
            miSaveReport.DropDownItems.Add(Item("&CSV Report...", Keys.None, (s, e) => SaveReportInteractive(Core.Reports.ReportFormat.Csv)));
            file.DropDownItems.AddRange(new ToolStripItem[]
            {
                miAddFiles, miAddFolder, new ToolStripSeparator(), miImportManifest, miExportManifest, miSaveReport,
                new ToolStripSeparator(), Item("E&xit", Keys.Alt | Keys.F4, (s, e) => Close()),
            });

            var tools = new ToolStripMenuItem("&Tools");
            miCalculate = Item("&Calculate Hash", Keys.F5, (s, e) => CalculateInteractive(false));
            miRecalculate = Item("&Recalculate All", Keys.Control | Keys.F5, (s, e) => CalculateInteractive(true));
            miVerify = Item("&Verify", Keys.F6, (s, e) => VerifyInteractive());
            miCancel = Item("C&ancel", Keys.None, (s, e) => CancelRun());
            miCancel.ShortcutKeyDisplayString = "Esc";
            // Ctrl+C, Ctrl+A and Delete are handled by the list itself (see OnListKeyDown) so they keep working normally
            // in the expected-hash text box; the menu only displays them.
            miCopyHash = Item("&Copy Hash", Keys.None, (s, e) => CopySelected(CopyMode.Hash));
            miCopyHash.ShortcutKeyDisplayString = "Ctrl+C";
            miCopyHashName = Item("Copy Hash and File &Name", Keys.Control | Keys.Shift | Keys.C, (s, e) => CopySelected(CopyMode.HashAndName));
            miCopyPath = Item("Copy Full &Path", Keys.None, (s, e) => CopySelected(CopyMode.Path));
            miSetExpected = Item("Set &Expected Hash for Selection", Keys.None, (s, e) => ApplyExpectedToSelection());
            miOpenFolder = Item("&Open Containing Folder", Keys.Control | Keys.L, (s, e) => OpenContainingFolder());
            miSelectAll = Item("Select &All", Keys.None, (s, e) => SelectAll());
            miSelectAll.ShortcutKeyDisplayString = "Ctrl+A";
            miRemove = Item("Re&move Selected", Keys.None, (s, e) => RemoveSelected());
            miRemove.ShortcutKeyDisplayString = "Del";
            miClear = Item("C&lear List", Keys.None, (s, e) => ClearList());
            tools.DropDownItems.AddRange(new ToolStripItem[]
            {
                miCalculate, miRecalculate, miVerify, miCancel, new ToolStripSeparator(),
                miCopyHash, miCopyHashName, miCopyPath, miSetExpected, new ToolStripSeparator(),
                miOpenFolder, miSelectAll, miRemove, miClear,
            });

            var settings = new ToolStripMenuItem("&Settings");
            miSubfolders = Item("Include &Subfolders When Adding Folders", Keys.None, (s, e) => { chkSubfolders.Checked = !chkSubfolders.Checked; });
            var theme = new ToolStripMenuItem("&Theme");
            miThemeLight = Item("&Light", Keys.None, (s, e) => SetTheme(Core.Settings.ThemePreference.Light));
            miThemeDark = Item("&Dark", Keys.None, (s, e) => SetTheme(Core.Settings.ThemePreference.Dark));
            theme.DropDownItems.AddRange(new ToolStripItem[] { miThemeLight, miThemeDark });
            miExplorer = Item("&Explorer Right-Click Menu", Keys.None, (s, e) => ToggleExplorerIntegration());
            settings.DropDownItems.AddRange(new ToolStripItem[]
            {
                miSubfolders, theme, miExplorer, new ToolStripSeparator(),
                Item("&Options...", Keys.Control | Keys.Oemcomma, (s, e) => ShowOptions()),
            });

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("&User Guide", Keys.F1, (s, e) => ShowHelp(HelpForm.Topic.Guide)),
                Item("About &Hash Algorithms", Keys.None, (s, e) => ShowHelp(HelpForm.Topic.Algorithms)),
                Item("Checksum &Manifest Formats", Keys.None, (s, e) => ShowHelp(HelpForm.Topic.Manifests)),
                new ToolStripSeparator(),
                Item("Open &Log Folder", Keys.None, (s, e) => OpenLogFolder()),
            });

            var about = new ToolStripMenuItem("&About");
            about.DropDownItems.Add(Item("&About HT File Hash Checker...", Keys.None, (s, e) => ShowAbout()));

            m.Items.AddRange(new ToolStripItem[] { file, tools, settings, help, about });

            listMenu = new ContextMenuStrip();
            listMenu.Items.AddRange(new ToolStripItem[]
            {
                Item("Copy Hash", Keys.None, (s, e) => CopySelected(CopyMode.Hash)),
                Item("Copy Hash and File Name", Keys.None, (s, e) => CopySelected(CopyMode.HashAndName)),
                Item("Copy Full Path", Keys.None, (s, e) => CopySelected(CopyMode.Path)),
                new ToolStripSeparator(),
                Item("Set Expected Hash from Text Box", Keys.None, (s, e) => ApplyExpectedToSelection()),
                Item("Clear Expected Hash", Keys.None, (s, e) => ClearExpectedOnSelection()),
                Item("Recalculate Selected", Keys.None, (s, e) => RecalculateSelected()),
                new ToolStripSeparator(),
                Item("Open Containing Folder", Keys.None, (s, e) => OpenContainingFolder()),
                Item("Remove from List", Keys.None, (s, e) => RemoveSelected()),
            });

            exportMenu = new ContextMenuStrip();
            exportMenu.Items.AddRange(new ToolStripItem[]
            {
                Item("Save HTML Report...", Keys.None, (s, e) => SaveReportInteractive(Core.Reports.ReportFormat.Html)),
                Item("Save Text Report (TXT)...", Keys.None, (s, e) => SaveReportInteractive(Core.Reports.ReportFormat.Text)),
                Item("Save CSV Report...", Keys.None, (s, e) => SaveReportInteractive(Core.Reports.ReportFormat.Csv)),
                new ToolStripSeparator(),
                Item("Export Checksum Manifest (SHA256SUMS style)...", Keys.None, (s, e) => ExportManifestInteractive()),
            });
            return m;
        }

        private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text);
            if (shortcut != Keys.None)
            {
                try { item.ShortcutKeys = shortcut; }
                catch (System.ComponentModel.InvalidEnumArgumentException) { }
            }
            item.Click += onClick;
            return item;
        }

        /// <summary>Combo-box entry for an algorithm.</summary>
        internal sealed class AlgorithmChoice
        {
            public AlgorithmChoice(HashAlgorithmInfo info) { Info = info; }
            public HashAlgorithmInfo Info { get; private set; }
            public override string ToString()
            {
                if (Info.Kind == HashAlgorithms.Default) return Info.DisplayName + " (recommended)";
                return Info.IsLegacy ? Info.DisplayName + " (legacy)" : Info.DisplayName;
            }
        }
    }
}
