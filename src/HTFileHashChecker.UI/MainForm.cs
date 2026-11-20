using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HTFileHashChecker.Core;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Manifests;
using HTFileHashChecker.Core.Processing;
using HTFileHashChecker.Core.Reports;
using HTFileHashChecker.Core.Settings;
using HTFileHashChecker.Windows;
using HTREASURE.Branding.WinForms;

namespace HTFileHashChecker.UI
{
    internal sealed partial class MainForm : Form
    {
        internal enum CopyMode { Hash, HashAndName, Path }

        private readonly AppServices app;
        private readonly CommandLine startup;
        private readonly List<HashItem> view = new List<HashItem>();
        private readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer { Interval = 200 };
        private readonly System.Windows.Forms.Timer argTimer = new System.Windows.Forms.Timer { Interval = 400 };
        private readonly List<string> pendingArgs = new List<string>();
        private readonly object pendingLock = new object();
        private bool pendingCalculate;
        private bool calculateAfterRun;
        private bool closeAfterRun;
        private CancellationTokenSource cts;
        private BatchProgress latestProgress;
        private string busyVerb;
        private int sortColumn = -1;
        private bool sortAscending = true;
        private bool loading;

        public MainForm(AppServices app, CommandLine startup)
        {
            this.app = app;
            this.startup = startup;
            BuildLayout();
            WireEvents();
            ApplySettingsToUi();
            Theme.Apply(this, Palette.For(app.Settings.Theme));
            ApplyBrandAccents();
            RestoreWindowPlacement();
            RefreshAll();
        }

        private HashWorkspace Workspace { get { return app.Workspace; } }
        internal bool IsBusy { get { return cts != null; } }

        private HashAlgorithmKind SelectedAlgorithm
        {
            get
            {
                var choice = cboAlgorithm.SelectedItem as AlgorithmChoice;
                return choice != null ? choice.Info.Kind : HashAlgorithms.Default;
            }
        }

        // ------------------------------------------------------------------ setup

        private void WireEvents()
        {
            btnAddFiles.Click += (s, e) => AddFilesInteractive();
            btnAddFolder.Click += (s, e) => AddFolderInteractive();
            btnRemove.Click += (s, e) => RemoveSelected();
            btnClear.Click += (s, e) => ClearList();
            btnOpenFolder.Click += (s, e) => OpenContainingFolder();
            btnCalculate.Click += (s, e) => CalculateInteractive(false);
            btnVerify.Click += (s, e) => VerifyInteractive();
            btnCancel.Click += (s, e) => CancelRun();
            btnCopy.Click += (s, e) => CopySelected(CopyMode.Hash);
            btnExport.Click += (s, e) => exportMenu.Show(btnExport, new Point(0, btnExport.Height));
            btnPaste.Click += (s, e) => PasteExpected();
            btnClearExpected.Click += (s, e) => { txtExpected.Clear(); HideResult(); txtExpected.Focus(); };
            chkSubfolders.CheckedChanged += (s, e) =>
            {
                miSubfolders.Checked = chkSubfolders.Checked;
                if (!loading) { app.Settings.IncludeSubfolders = chkSubfolders.Checked; SaveSettingsQuietly(); }
            };
            cboAlgorithm.SelectedIndexChanged += (s, e) => OnAlgorithmChanged();
            txtExpected.TextChanged += (s, e) => OnExpectedTextChanged();
            txtExpected.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; VerifyInteractive(); } };

            list.RetrieveVirtualItem += OnRetrieveVirtualItem;
            list.DrawColumnHeader += OnDrawColumnHeader;
            list.DrawItem += (s, e) => e.DrawDefault = true;
            list.DrawSubItem += (s, e) => e.DrawDefault = true;
            list.ColumnClick += OnColumnClick;
            list.SelectedIndexChanged += (s, e) => UpdateCommandState();
            list.VirtualItemsSelectionRangeChanged += (s, e) => UpdateCommandState();
            list.KeyDown += OnListKeyDown;
            list.DoubleClick += (s, e) => CopySelected(CopyMode.Hash);
            list.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right && list.SelectedIndices.Count > 0) listMenu.Show(list, e.Location); };

            foreach (Control c in new Control[] { this, list, listHost, dropZone })
            {
                c.DragEnter += OnDragEnter;
                c.DragDrop += OnDragDrop;
            }

            uiTimer.Tick += (s, e) => RefreshProgress();
            argTimer.Tick += (s, e) => ProcessPendingArguments();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && IsBusy) { e.Handled = true; CancelRun(); } };
            Shown += OnShown;
            FormClosing += OnFormClosing;
#if NET47_OR_GREATER
            // Per-monitor DPI (Windows 10 1703+): Windows rescales the window when it moves to another monitor;
            // rescale the columns and refit the fixed size to the new screen.
            DpiChanged += (s, e) =>
            {
                ScaleListColumns(e.DeviceDpiOld, e.DeviceDpiNew);
                BeginInvoke((Action)FitToScreen);
            };
#endif
        }

        private void ApplySettingsToUi()
        {
            loading = true;
            var s = app.Settings;
            chkSubfolders.Checked = s.IncludeSubfolders;
            miSubfolders.Checked = s.IncludeSubfolders;
            SelectAlgorithm(s.DefaultAlgorithm);
            miThemeLight.Checked = s.Theme == ThemePreference.Light;
            miThemeDark.Checked = s.Theme == ThemePreference.Dark;
            stsMode.Text = app.Paths.IsPortable ? "Portable mode" : string.Empty;
            UpdateExplorerMenuState();
            loading = false;
        }

        private void SelectAlgorithm(HashAlgorithmKind kind)
        {
            for (int i = 0; i < cboAlgorithm.Items.Count; i++)
            {
                if (((AlgorithmChoice)cboAlgorithm.Items[i]).Info.Kind == kind) { cboAlgorithm.SelectedIndex = i; return; }
            }
            cboAlgorithm.SelectedIndex = 0;
        }

        /// <summary>Brand details the generic theme pass does not cover: the header subtitle and footer attribution.</summary>
        private void ApplyBrandAccents()
        {
            headerSubtitle.ForeColor = Color.FromArgb(0xC9, 0xD4, 0xE5);
            stsAttribution.ForeColor = Theme.Current.MutedText;
        }

        private void OnShown(object sender, EventArgs e)
        {
            ScaleListColumns(96, (int)Math.Round(CurrentScale() * 96));
            if (startup.Paths.Count > 0)
                ReceiveArgumentsOnUiThread(startup.Paths, startup.Calculate);
            if (startup.Unknown.Count > 0)
                SetStatus("Ignored unknown command-line option(s): " + string.Join(" ", startup.Unknown.Take(5)));
            if (app.SettingsStore.LoadWarning != null) SetStatus(app.SettingsStore.LoadWarning);
        }

        private void ScaleListColumns(int oldDpi, int newDpi)
        {
            if (oldDpi == newDpi || oldDpi <= 0) return;
            foreach (ColumnHeader c in list.Columns) c.Width = (int)Math.Round(c.Width * (double)newDpi / oldDpi);
        }

        // ------------------------------------------------------------------ command-line / Explorer forwarding

        /// <summary>Called on a background thread by <see cref="SingleInstance"/> when another instance forwards arguments.</summary>
        public void ReceiveArguments(string[] args)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    var parsed = CommandLine.Parse(args);
                    BringToFront();
                    if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                    Activate();
                    if (parsed.Paths.Count > 0) ReceiveArgumentsOnUiThread(parsed.Paths, parsed.Calculate);
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void ReceiveArgumentsOnUiThread(IEnumerable<string> paths, bool calculate)
        {
            // Explorer starts one process per selected file; collect them for a moment and add them as one batch.
            lock (pendingLock)
            {
                pendingArgs.AddRange(paths);
                pendingCalculate |= calculate;
            }
            argTimer.Stop();
            argTimer.Start();
        }

        private async void ProcessPendingArguments()
        {
            argTimer.Stop();
            if (IsBusy) { argTimer.Start(); return; }
            List<string> paths;
            bool calculate;
            lock (pendingLock)
            {
                paths = new List<string>(pendingArgs);
                calculate = pendingCalculate;
                pendingArgs.Clear();
                pendingCalculate = false;
            }
            if (paths.Count == 0) return;
            await AddPathsAsync(paths, ItemSource.CommandLine);
            if (calculate) CalculateInteractive(false);
        }

        // ------------------------------------------------------------------ adding and removing files

        internal void AddFilesInteractive()
        {
            if (IsBusy) return;
            using (var dlg = new OpenFileDialog
            {
                Title = "Add files to hash",
                Multiselect = true,
                CheckFileExists = true,
                DereferenceLinks = false,
                Filter = "All files (*.*)|*.*",
                RestoreDirectory = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var _ = AddPathsAsync(dlg.FileNames, ItemSource.Manual);
            }
        }

        internal void AddFolderInteractive()
        {
            if (IsBusy) return;
            using (var dlg = new FolderBrowserDialog
            {
                Description = chkSubfolders.Checked
                    ? "Choose a folder. All files in it and in its subfolders will be added."
                    : "Choose a folder. The files directly inside it will be added (subfolders are not included).",
                ShowNewFolderButton = false,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var _ = AddPathsAsync(new[] { dlg.SelectedPath }, ItemSource.Folder);
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (IsBusy) return;
            var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null || dropped.Length == 0) return;
            var _ = AddPathsAsync(dropped, ItemSource.Manual);
        }

        private async Task AddPathsAsync(IEnumerable<string> paths, ItemSource source)
        {
            if (IsBusy) return;
            BeginBusy("Adding files");
            AddResult result = null;
            try
            {
                result = await Workspace.AddPathsAsync(paths, new AddOptions
                {
                    Recursive = chkSubfolders.Checked,
                    SkipOnlineOnlyFiles = app.Settings.SkipOnlineOnlyFiles,
                    Source = source,
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Adding files was cancelled.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowError("The files could not be added.", ex);
            }
            finally
            {
                EndBusy();
            }
            if (result == null) return;

            RebuildView();
            var parts = new List<string> { result.Added.Count + " file(s) added" };
            if (result.Duplicates > 0) parts.Add(result.Duplicates + " already in the list");
            int skipped = result.Issues.Count(i => !i.IsError);
            int errors = result.Issues.Count(i => i.IsError);
            if (skipped > 0) parts.Add(skipped + " skipped");
            if (errors > 0) parts.Add(errors + " could not be read");
            if (result.LimitReached) parts.Add("list limit reached");
            SetStatus(string.Join(", ", parts) + ".");
            if (result.Issues.Count > 0 && (errors > 0 || result.Added.Count == 0))
            {
                MessageListForm.Show(this, "Some items were not added",
                    "These items were skipped or could not be read:",
                    result.Issues.Select(i => i.Path + "  —  " + i.Reason));
            }
            app.Logger.Info("Added " + result.Added.Count + " file(s); " + result.Duplicates + " duplicate(s); " + result.Issues.Count + " issue(s).");
        }

        internal void RemoveSelected()
        {
            if (IsBusy) return;
            var selected = SelectedItems();
            if (selected.Count == 0) return;
            Workspace.Remove(selected);
            RebuildView();
            SetStatus(selected.Count + " file(s) removed from the list. Files on disk were not changed.");
        }

        internal void ClearList()
        {
            if (IsBusy || Workspace.Items.Count == 0) return;
            if (app.Settings.ConfirmBeforeClearing &&
                MessageBox.Show(this, "Remove all " + Workspace.Items.Count + " file(s) from the list?\n\nFiles on disk are not changed.",
                    AppInfo.ProductName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return;
            Workspace.Clear();
            HideResult();
            RebuildView();
            SetStatus("List cleared.");
        }

        // ------------------------------------------------------------------ calculate / verify

        internal void CalculateInteractive(bool all)
        {
            if (IsBusy) { if (!all) calculateAfterRun = true; return; }
            if (Workspace.Items.Count == 0)
            {
                SetStatus("Add files first: use Add Files, Add Folder, or drag and drop.");
                return;
            }
            var toRun = Workspace.PrepareCalculation(SelectedAlgorithm, null, all);
            if (toRun.Count == 0)
            {
                SetStatus("All files already have a " + HashAlgorithms.Get(SelectedAlgorithm).DisplayName
                    + " hash. Use Tools > Recalculate All to read them again.");
                return;
            }
            HideResult();
            var _ = RunBatchAsync(toRun, "Calculating");
        }

        private void RecalculateSelected()
        {
            if (IsBusy) return;
            var selected = SelectedItems();
            if (selected.Count == 0) return;
            var toRun = Workspace.PrepareCalculation(SelectedAlgorithm, selected, true);
            if (toRun.Count > 0) { var _ = RunBatchAsync(toRun, "Calculating"); }
        }

        internal void VerifyInteractive()
        {
            if (IsBusy) return;
            if (Workspace.Items.Count == 0)
            {
                SetStatus("Add the file to verify first, or use File > Verify with Checksum Manifest.");
                return;
            }

            List<HashItem> scope = null;
            if (txtExpected.Text.Trim().Length > 0)
            {
                List<HashItem> targets = SelectedItems();
                if (targets.Count == 0 && Workspace.Items.Count == 1) targets = Workspace.Items.ToList();
                if (targets.Count == 0)
                {
                    ShowResult(ResultKind.Warning, "Select the file in the list that the expected hash belongs to, then choose Verify.");
                    list.Focus();
                    return;
                }
                if (targets.Count > 1 &&
                    MessageBox.Show(this, "Use this expected hash for all " + targets.Count + " selected files?", AppInfo.ProductName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                var parsed = Workspace.SetExpected(targets, txtExpected.Text);
                if (!parsed.IsValid)
                {
                    ShowResult(ResultKind.Error, parsed.Error);
                    txtExpected.Focus();
                    return;
                }
                scope = targets;
            }

            var toRun = Workspace.PrepareVerification(scope);
            if (toRun.Count == 0)
            {
                ShowResult(ResultKind.Warning, "There is nothing to compare. Paste the expected hash published by the file's source into "
                    + "Expected hash, select the file, and choose Verify, or use File > Verify with Checksum Manifest.");
                txtExpected.Focus();
                return;
            }
            var __ = RunBatchAsync(toRun, "Verifying");
        }

        private void ApplyExpectedToSelection()
        {
            if (IsBusy) return;
            var targets = SelectedItems();
            if (targets.Count == 0) { SetStatus("Select one or more files first."); return; }
            var parsed = Workspace.SetExpected(targets, txtExpected.Text);
            if (!parsed.IsValid) { ShowResult(ResultKind.Error, parsed.Error); txtExpected.Focus(); return; }
            list.Invalidate();
            SetStatus("Expected " + HashAlgorithms.Get(parsed.Value.Algorithm).DisplayName + " hash set for " + targets.Count + " file(s). Choose Verify to compare.");
        }

        private void ClearExpectedOnSelection()
        {
            if (IsBusy) return;
            Workspace.ClearExpected(SelectedItems());
            list.Invalidate();
            UpdateCounts();
        }

        private async Task RunBatchAsync(List<HashItem> toRun, string verb)
        {
            BeginBusy(verb);
            BatchSummary summary = null;
            var progress = new Progress<BatchProgress>(p => latestProgress = p);
            try
            {
                summary = await Workspace.RunAsync(toRun, new BatchOptions { MaxParallelFiles = app.Settings.MaxParallelFiles }, progress, cts.Token);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                app.Logger.Error(verb + " failed", ex);
                ShowError(verb + " stopped because of an unexpected error.", ex);
            }
            finally
            {
                EndBusy();
            }

            RebuildView();
            if (summary != null) ShowSummary(summary, toRun, verb);

            if (closeAfterRun) { Close(); return; }
            if (calculateAfterRun) { calculateAfterRun = false; CalculateInteractive(false); }
        }

        private void ShowSummary(BatchSummary s, List<HashItem> processed, string verb)
        {
            string elapsed = UiFormat.Elapsed(s.Elapsed);
            stsElapsed.Text = "Last run: " + elapsed;
            if (s.WasCancelled)
            {
                SetStatus(verb + " cancelled after " + elapsed + ". Files were not changed.");
                ShowResult(ResultKind.Warning, "Cancelled. " + processed.Count(i => i.Status == ItemStatus.Cancelled) + " file(s) were not processed.");
                return;
            }

            if (processed.Count == 1 && processed[0].Expected != null)
            {
                var item = processed[0];
                if (item.Status == ItemStatus.Match)
                    ShowResult(ResultKind.Success, "MATCH — File checksum matches.   " + item.FileName + "   (" + HashAlgorithms.Get(item.Algorithm).DisplayName + ")"
                        + (HashAlgorithms.Get(item.Algorithm).IsLegacy ? "   Note: " + HashAlgorithms.Get(item.Algorithm).DisplayName + " is a legacy algorithm." : string.Empty));
                else if (item.Status == ItemStatus.Mismatch)
                    ShowResult(ResultKind.Error, "MISMATCH — File checksum does not match.   " + item.FileName
                        + "   Do not use this file: it may be corrupted, incomplete or a different file.");
                else
                    ShowResult(ResultKind.Error, "ERROR — File could not be verified.   " + item.FileName + ": " + item.Message);
            }
            else if (processed.Any(i => i.Expected != null))
            {
                int match = processed.Count(i => i.Status == ItemStatus.Match);
                int fail = processed.Count(i => i.IsVerificationFailure);
                int failedOverall = Workspace.Items.Count(i => i.IsVerificationFailure);
                ShowResult(failedOverall == 0 ? ResultKind.Success : ResultKind.Error,
                    failedOverall == 0
                        ? "All " + match + " file(s) MATCH."
                        : match + " file(s) MATCH, " + Math.Max(fail, failedOverall) + " verification failure(s). Sort by Status to review them.");
            }
            else if (s.Errors > 0)
            {
                ShowResult(ResultKind.Warning, s.Errors + " file(s) could not be read. See the Details column.");
            }
            SetStatus(string.Format("{0} finished in {1}: {2} calculated, {3} matched, {4} mismatched, {5} error(s){6}.",
                verb, elapsed, s.Calculated, s.Matched, s.Mismatched, s.Errors,
                s.BytesHashed > 0 ? ", " + UiFormat.Size(s.BytesHashed) + " read" + (UiFormat.Rate(s.BytesHashed, s.Elapsed).Length > 0 ? " (" + UiFormat.Rate(s.BytesHashed, s.Elapsed) + ")" : string.Empty) : string.Empty));
        }

        internal void CancelRun()
        {
            if (cts == null) return;
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
                SetStatus("Cancelling...");
                btnCancel.Enabled = false;
            }
        }

        private void BeginBusy(string verb)
        {
            cts = new CancellationTokenSource();
            busyVerb = verb;
            latestProgress = null;
            pbFile.Value = 0;
            pbOverall.Value = 0;
            UseWaitCursor = false;
            SetStatus(verb + "...");
            UpdateCommandState();
            uiTimer.Start();
        }

        private void EndBusy()
        {
            uiTimer.Stop();
            RefreshProgress();
            if (cts != null) { cts.Dispose(); cts = null; }
            busyVerb = null;
            lblFile.Text = "Current file: -";
            pbFile.Value = 0;
            UpdateCommandState();
        }

        private void RefreshProgress()
        {
            var p = latestProgress;
            if (p != null)
            {
                if (p.CurrentItem != null)
                {
                    lblFile.Text = "Current file: " + p.CurrentItem.FileName
                        + (p.CurrentItemLength > 0 ? "  (" + UiFormat.Size(p.CurrentItemBytes) + " of " + UiFormat.Size(p.CurrentItemLength) + ")" : string.Empty);
                    pbFile.Value = Ratio(p.CurrentItemBytes, p.CurrentItemLength);
                }
                int overall = p.TotalBytes > 0 ? Ratio(p.BytesDone, p.TotalBytes) : Ratio(p.Finished, p.Total);
                pbOverall.Value = overall;
                lblOverall.Text = string.Format("Overall: {0} of {1} file(s)   {2} of {3}   {4}   {5} completed, {6} failed",
                    p.Finished, p.Total, UiFormat.Size(p.BytesDone), UiFormat.Size(p.TotalBytes), UiFormat.Rate(p.BytesDone, p.Elapsed),
                    p.Succeeded, p.Failed);
                stsElapsed.Text = "Elapsed: " + UiFormat.Elapsed(p.Elapsed);
            }
            list.Invalidate();
            UpdateCounts();
        }

        private static int Ratio(long done, long total)
        {
            if (total <= 0) return 0;
            return (int)Math.Max(0, Math.Min(1000, done * 1000 / total));
        }

        // ------------------------------------------------------------------ manifests and reports

        internal async void ImportManifestInteractive()
        {
            if (IsBusy) return;
            ManifestVerifyForm.Choice choice;
            using (var dlg = new ManifestVerifyForm(app.Settings.ReportUnexpectedFilesByDefault))
            {
                Theme.Apply(dlg, Theme.Current);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                choice = dlg.Result;
            }
            if (Workspace.Items.Count > 0 &&
                MessageBox.Show(this, "Verifying a manifest replaces the current file list. Continue?", AppInfo.ProductName,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            BeginBusy("Reading manifest");
            ManifestLoadResult loaded = null;
            try
            {
                loaded = await Workspace.LoadManifestAsync(choice.ManifestPath, choice.BaseDirectory, choice.Algorithm, choice.ReportUnexpected, cts.Token);
            }
            catch (OperationCanceledException) { SetStatus("Manifest import cancelled."); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                ShowError("The manifest could not be read.", ex);
            }
            finally
            {
                EndBusy();
            }
            HideResult();
            RebuildView();
            if (loaded == null) return;

            app.Logger.Info("Manifest imported: " + loaded.Document.Entries.Count + " entr(ies), " + loaded.Document.Issues.Count + " issue(s).");
            if (loaded.Document.Issues.Count > 0)
            {
                MessageListForm.Show(this, "Manifest check",
                    "The manifest was read with " + loaded.Document.Issues.Count(i => i.IsError) + " problem(s) and "
                    + loaded.Document.Issues.Count(i => !i.IsError) + " warning(s). Rejected entries are shown as INVALID ENTRY.",
                    loaded.Document.Issues.Select(i => (i.IsError ? "Error   " : "Warning ") + i));
            }
            if (loaded.Plan.Items.Count == 0)
            {
                ShowResult(ResultKind.Error, "The manifest contains no usable checksum lines.");
                return;
            }
            SelectAlgorithmFromManifest(loaded);
            var toRun = Workspace.PrepareVerification(null);
            if (toRun.Count > 0) await RunBatchAsync(toRun, "Verifying");
            else ShowSummary(BatchSummary.FromItems(Workspace.Items, TimeSpan.Zero, false), Workspace.Items.ToList(), "Verification");
        }

        private void SelectAlgorithmFromManifest(ManifestLoadResult loaded)
        {
            var first = loaded.Document.Entries.FirstOrDefault();
            if (first != null) { loading = true; SelectAlgorithm(first.Algorithm); loading = false; UpdateAlgorithmNote(); }
        }

        internal void ExportManifestInteractive()
        {
            if (IsBusy) return;
            var hashed = Workspace.Items.Where(i => i.HashHex != null && !i.IsPlanFinal && i.Status != ItemStatus.Error).ToList();
            if (hashed.Count == 0) { SetStatus("Calculate hashes first; there is nothing to export yet."); return; }
            var algorithm = hashed[0].Algorithm;
            var info = HashAlgorithms.Get(algorithm);
            using (var dlg = new SaveFileDialog
            {
                Title = "Export checksum manifest",
                FileName = info.ManifestFileName,
                Filter = info.ManifestFileName + " manifest|" + info.ManifestFileName + ";*" + info.FileExtension + "|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                OverwritePrompt = true,
                AddExtension = false,
                InitialDirectory = SuggestedFolder(hashed),
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.FileName;
                var plan = Workspace.PrepareManifestExport(path, null);
                if (plan.Entries.Count == 0)
                {
                    MessageBox.Show(this, "None of the calculated files are inside the folder you chose.\n\nSave the manifest in the folder that contains the files "
                        + "(or a parent folder), so the manifest can use relative paths.", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (plan.OutsideFolder.Count > 0 &&
                    MessageBox.Show(this, plan.OutsideFolder.Count + " file(s) are not inside the manifest's folder and will be left out:\n\n"
                        + string.Join("\n", plan.OutsideFolder.Take(8).Select(i => i.FullPath)) + (plan.OutsideFolder.Count > 8 ? "\n..." : string.Empty)
                        + "\n\nSave the manifest anyway?", AppInfo.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                var options = new ManifestWriteOptions { Overwrite = File.Exists(path) }; // the dialog already asked before overwriting
                if (plan.MixedAlgorithms)
                {
                    if (MessageBox.Show(this, "The files were calculated with different algorithms. A " + info.ManifestFileName
                            + "-style file holds one algorithm, so the manifest will use the BSD tag format (\"SHA256 (file) = hash\"), "
                            + "which tools such as sha256sum -c also read.\n\nContinue?", AppInfo.ProductName, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                        return;
                    options.Format = ManifestLineFormat.Bsd;
                }
                try
                {
                    ManifestWriter.WriteFile(path, plan.Entries, options);
                    RememberOutputFolder(path);
                    SetStatus("Manifest saved with " + plan.Entries.Count + " entr" + (plan.Entries.Count == 1 ? "y" : "ies") + ": " + path);
                    app.Logger.Info("Manifest exported with " + plan.Entries.Count + " entries.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    ShowError("The manifest could not be saved.", ex);
                }
            }
        }

        internal void SaveReportInteractive(ReportFormat format)
        {
            if (IsBusy) return;
            if (Workspace.Items.Count == 0) { SetStatus("There is nothing to report yet."); return; }
            string ext = ReportService.Extension(format);
            string filter = format == ReportFormat.Html ? "HTML report (*.html)|*.html" : format == ReportFormat.Csv ? "CSV file (*.csv)|*.csv" : "Text file (*.txt)|*.txt";
            using (var dlg = new SaveFileDialog
            {
                Title = "Save report",
                FileName = "HashReport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext,
                Filter = filter + "|All files (*.*)|*.*",
                DefaultExt = ext.TrimStart('.'),
                OverwritePrompt = true,
                InitialDirectory = SuggestedFolder(Workspace.Items),
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var data = Workspace.BuildReport(SelectedAlgorithm, null);
                    ReportService.Save(dlg.FileName, data, format, File.Exists(dlg.FileName));
                    RememberOutputFolder(dlg.FileName);
                    app.Settings.DefaultReportFormat = format;
                    SaveSettingsQuietly();
                    SetStatus("Report saved: " + dlg.FileName);
                    app.Logger.Info("Report saved (" + format + ", " + data.TotalFiles + " files).");
                    if (app.Settings.OpenReportAfterSaving) OpenWithShell(dlg.FileName);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    ShowError("The report could not be saved.", ex);
                }
            }
        }

        private string SuggestedFolder(IEnumerable<HashItem> items)
        {
            if (!string.IsNullOrEmpty(app.Settings.LastOutputFolder) && Directory.Exists(app.Settings.LastOutputFolder))
                return app.Settings.LastOutputFolder;
            if (Workspace.ManifestBaseDirectory != null) return Workspace.ManifestBaseDirectory;
            var first = items.FirstOrDefault();
            return first != null ? Path.GetDirectoryName(first.FullPath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private void RememberOutputFolder(string savedFile)
        {
            app.Settings.LastOutputFolder = Path.GetDirectoryName(savedFile);
            SaveSettingsQuietly();
        }

        // ------------------------------------------------------------------ clipboard, folders, selection

        internal void CopySelected(CopyMode mode)
        {
            var items = SelectedItems();
            if (items.Count == 0 && Workspace.Items.Count == 1) items = Workspace.Items.ToList();
            if (items.Count == 0) { SetStatus("Select one or more files to copy."); return; }
            var sb = new StringBuilder();
            int copied = 0;
            foreach (var i in items)
            {
                if (mode == CopyMode.Path) { sb.Append(i.FullPath).Append("\r\n"); copied++; continue; }
                if (i.HashHex == null) continue;
                if (mode == CopyMode.Hash && items.Count == 1) sb.Append(i.HashHex);
                else sb.Append(i.HashHex).Append("  ").Append(i.RelativePath ?? i.FileName).Append("\r\n");
                copied++;
            }
            if (copied == 0) { SetStatus("The selected files have no calculated hash yet."); return; }
            if (ClipboardHelper.TrySetText(sb.ToString()))
                SetStatus(copied == 1 && mode == CopyMode.Hash ? "Hash copied to the clipboard." : copied + " line(s) copied to the clipboard.");
            else
                SetStatus("The clipboard is in use by another program. Try again.");
        }

        private void PasteExpected()
        {
            try
            {
                if (Clipboard.ContainsText()) txtExpected.Text = Clipboard.GetText().Trim();
            }
            catch (System.Runtime.InteropServices.ExternalException) { SetStatus("The clipboard is in use by another program. Try again."); }
            txtExpected.Focus();
        }

        internal void OpenContainingFolder()
        {
            var item = SelectedItems().FirstOrDefault() ?? (Workspace.Items.Count == 1 ? Workspace.Items[0] : null);
            if (item == null) { SetStatus("Select a file first."); return; }
            if (!File.Exists(item.FullPath))
            {
                string dir = Path.GetDirectoryName(item.FullPath);
                if (Directory.Exists(dir)) OpenWithShell(dir);
                else SetStatus("The file and its folder no longer exist.");
                return;
            }
            try
            {
                // explorer.exe /select highlights the file; the file itself is never opened or executed.
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + item.FullPath + "\"") { UseShellExecute = false });
            }
            catch (Win32Exception ex) { ShowError("File Explorer could not be opened.", ex); }
        }

        private void OpenWithShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Win32Exception ex) { ShowError("The file could not be opened.", ex); }
        }

        private void OpenLogFolder()
        {
            Directory.CreateDirectory(app.Paths.LogDirectory);
            OpenWithShell(app.Paths.LogDirectory);
        }

        private void SelectAll()
        {
            if (view.Count == 0) return;
            ListViewNative.SetAllSelected(list, true);
            UpdateCommandState();
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C && !e.Shift) { CopySelected(CopyMode.Hash); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A) { SelectAll(); e.Handled = true; }
            else if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                if (list.SelectedIndices.Count > 0)
                {
                    var bounds = list.Items[list.SelectedIndices[0]].Bounds;
                    listMenu.Show(list, new Point(bounds.Left + 20, bounds.Bottom));
                }
                e.Handled = true;
            }
        }

        internal List<HashItem> SelectedItems()
        {
            var result = new List<HashItem>();
            foreach (int index in list.SelectedIndices)
                if (index >= 0 && index < view.Count) result.Add(view[index]);
            return result;
        }

        // ------------------------------------------------------------------ list view

        internal void RebuildView()
        {
            view.Clear();
            view.AddRange(Workspace.Items);
            ApplySort();
            ListViewNative.SetAllSelected(list, false);
            list.VirtualListSize = view.Count;
            list.Invalidate();
            RefreshAll();
        }

        private void ApplySort()
        {
            if (sortColumn < 0) return;
            Comparison<HashItem> cmp;
            switch (sortColumn)
            {
                case ColSize: cmp = (a, b) => Nullable.Compare(a.Size, b.Size); break;
                case ColStatus: cmp = (a, b) => StatusRank(a.Status).CompareTo(StatusRank(b.Status)); break;
                case ColTime: cmp = (a, b) => Nullable.Compare(a.Elapsed, b.Elapsed); break;
                case ColAlgorithm: cmp = (a, b) => ((int)a.Algorithm).CompareTo((int)b.Algorithm); break;
                case ColHash: cmp = (a, b) => string.CompareOrdinal(a.HashHex, b.HashHex); break;
                case ColPath: cmp = (a, b) => FileScanner.ComparePaths(a.FullPath, b.FullPath); break;
                default: cmp = (a, b) => FileScanner.ComparePaths(a.FileName, b.FileName); break;
            }
            // Stable sort with the original order as a tie-breaker.
            var indexed = view.Select((item, idx) => new { item, idx }).ToList();
            indexed.Sort((x, y) =>
            {
                int c = cmp(x.item, y.item);
                if (!sortAscending) c = -c;
                return c != 0 ? c : x.idx.CompareTo(y.idx);
            });
            view.Clear();
            view.AddRange(indexed.Select(x => x.item));
        }

        /// <summary>Problems first when sorting by status.</summary>
        private static int StatusRank(ItemStatus s)
        {
            switch (s)
            {
                case ItemStatus.Mismatch: return 0;
                case ItemStatus.Error: return 1;
                case ItemStatus.Missing: return 2;
                case ItemStatus.Invalid: return 3;
                case ItemStatus.Unexpected: return 4;
                case ItemStatus.Cancelled: return 5;
                case ItemStatus.Pending: return 6;
                case ItemStatus.Hashing: return 7;
                case ItemStatus.Skipped: return 8;
                case ItemStatus.Hashed: return 9;
                default: return 10;
            }
        }

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (IsBusy) return;
            if (sortColumn == e.Column) sortAscending = !sortAscending;
            else { sortColumn = e.Column; sortAscending = true; }
            var selected = SelectedItems();
            view.Clear();
            view.AddRange(Workspace.Items);
            ApplySort();
            ListViewNative.SetAllSelected(list, false);
            foreach (var item in selected)
            {
                int idx = view.IndexOf(item);
                if (idx >= 0) list.SelectedIndices.Add(idx);
            }
            list.Invalidate();
        }

        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= view.Count) { e.Item = new ListViewItem(new string[9]); return; }
            var i = view[e.ItemIndex];
            var p = Theme.Current;
            var status = i.Status;
            var li = new ListViewItem(i.FileName) { UseItemStyleForSubItems = false, ForeColor = p.Text, BackColor = p.Surface };
            li.SubItems.Add(i.FullPath, p.Text, p.Surface, list.Font);
            li.SubItems.Add(UiFormat.Size(i.Size), p.Text, p.Surface, list.Font);
            var algInfo = HashAlgorithms.Get(i.Algorithm);
            li.SubItems.Add(algInfo.DisplayName, algInfo.IsLegacy ? p.Warning : p.Text, p.Surface, list.Font);
            li.SubItems.Add(i.HashHex ?? string.Empty, p.Text, p.Surface, list.Font);
            li.SubItems.Add(i.Expected != null ? i.Expected.Hex : string.Empty, p.MutedText, p.Surface, list.Font);
            Color fore = p.Text, back = p.Surface;
            switch (status)
            {
                case ItemStatus.Match: fore = p.Success; back = p.SuccessTint; break;
                case ItemStatus.Hashed: fore = p.Success; break;
                case ItemStatus.Mismatch:
                case ItemStatus.Error:
                case ItemStatus.Invalid: fore = p.Error; back = p.ErrorTint; break;
                case ItemStatus.Missing:
                case ItemStatus.Unexpected:
                case ItemStatus.Cancelled: fore = p.Warning; back = p.WarningTint; break;
                case ItemStatus.Hashing: fore = p.Text; back = p.InfoTint; break;
            }
            li.SubItems.Add(HashItem.StatusLabel(status), fore, back, list.Font);
            li.SubItems.Add(UiFormat.Elapsed(i.Elapsed), p.MutedText, p.Surface, list.Font);
            li.SubItems.Add(i.Message ?? (algInfo.IsLegacy && i.HashHex != null ? "Legacy algorithm" : string.Empty), p.MutedText, p.Surface, list.Font);
            li.ToolTipText = i.FullPath + (i.Size.HasValue ? "\n" + i.Size.Value.ToString("N0") + " bytes" : string.Empty)
                + (i.ManifestLine.HasValue ? "\nManifest line " + i.ManifestLine.Value : string.Empty)
                + "\n" + HashItem.StatusDescription(status);
            e.Item = li;
        }

        private void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            var p = Theme.Current;
            if (!p.IsDark) { e.DrawDefault = true; return; }
            using (var back = new SolidBrush(p.SurfaceAlt))
            using (var line = new Pen(p.Border))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                | (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
            var r = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, r, p.Text, flags);
        }

        // ------------------------------------------------------------------ state display

        private void RefreshAll()
        {
            bool empty = Workspace.Items.Count == 0;
            dropZone.Visible = empty;
            list.Visible = !empty;
            if (empty) dropZone.BringToFront(); else list.BringToFront();
            UpdateCounts();
            UpdateCommandState();
            UpdateAlgorithmNote();
        }

        private void UpdateCounts()
        {
            var items = Workspace.Items;
            if (items.Count == 0) { lblCounts.Text = string.Empty; return; }
            int done = 0, match = 0, mismatch = 0, errors = 0, missing = 0, pending = 0;
            long total = 0;
            foreach (var i in items)
            {
                total += i.Size ?? 0;
                switch (i.Status)
                {
                    case ItemStatus.Hashed: done++; break;
                    case ItemStatus.Match: match++; break;
                    case ItemStatus.Mismatch: mismatch++; break;
                    case ItemStatus.Error:
                    case ItemStatus.Invalid: errors++; break;
                    case ItemStatus.Missing: missing++; break;
                    case ItemStatus.Pending:
                    case ItemStatus.Cancelled: pending++; break;
                }
            }
            var parts = new List<string> { items.Count + " file(s), " + UiFormat.Size(total) };
            if (done > 0) parts.Add(done + " calculated");
            if (match > 0) parts.Add(match + " match");
            if (mismatch > 0) parts.Add(mismatch + " mismatch");
            if (missing > 0) parts.Add(missing + " missing");
            if (errors > 0) parts.Add(errors + " error(s)");
            if (pending > 0) parts.Add(pending + " pending");
            lblCounts.Text = string.Join("  ·  ", parts);
        }

        private void UpdateCommandState()
        {
            bool busy = IsBusy;
            bool any = Workspace.Items.Count > 0;
            bool selection = list.SelectedIndices.Count > 0;
            bool hasHash = Workspace.Items.Any(i => i.HashHex != null);
            foreach (var b in new Control[] { btnAddFiles, btnAddFolder, chkSubfolders, cboAlgorithm }) b.Enabled = !busy;
            btnRemove.Enabled = !busy && selection;
            btnClear.Enabled = !busy && any;
            btnOpenFolder.Enabled = selection || Workspace.Items.Count == 1;
            btnCalculate.Enabled = !busy && any;
            btnVerify.Enabled = !busy && any;
            btnCancel.Enabled = busy;
            btnCopy.Enabled = hasHash;
            btnExport.Enabled = !busy && any;
            btnPaste.Enabled = !busy;

            miAddFiles.Enabled = miAddFolder.Enabled = miImportManifest.Enabled = !busy;
            miExportManifest.Enabled = miSaveReport.Enabled = !busy && any;
            miCalculate.Enabled = miRecalculate.Enabled = miVerify.Enabled = !busy && any;
            miCancel.Enabled = busy;
            miCopyHash.Enabled = miCopyHashName.Enabled = hasHash;
            miCopyPath.Enabled = miOpenFolder.Enabled = selection;
            miSetExpected.Enabled = !busy && selection;
            miRemove.Enabled = !busy && selection;
            miClear.Enabled = !busy && any;
            miSelectAll.Enabled = any;
            Text = busy && busyVerb != null ? AppInfo.ProductName + " - " + busyVerb + "..." : AppInfo.ProductName;
        }

        private void OnAlgorithmChanged()
        {
            UpdateAlgorithmNote();
            if (loading) return;
            app.Settings.DefaultAlgorithm = SelectedAlgorithm;
            SaveSettingsQuietly();
        }

        private void UpdateAlgorithmNote()
        {
            var info = HashAlgorithms.Get(SelectedAlgorithm);
            if (info.IsLegacy)
            {
                lblAlgorithmNote.Text = "⚠ " + info.DisplayName + " is a legacy algorithm: not suitable for security-sensitive verification.";
                lblAlgorithmNote.ForeColor = Theme.Current.Warning;
            }
            else
            {
                lblAlgorithmNote.Text = string.Empty;
            }
            tips.SetToolTip(lblAlgorithmNote, info.SecurityNote);
        }

        private void OnExpectedTextChanged()
        {
            HideResult();
            string text = txtExpected.Text;
            if (text.Trim().Length == 0) return;
            var parsed = Core.Verification.ExpectedHash.Parse(text, null);
            if (!parsed.IsValid) return; // shown only when the user chooses Verify, to avoid nagging while typing
            var info = HashAlgorithms.Get(parsed.Value.Algorithm);
            ShowResult(ResultKind.Info, "Expected " + info.DisplayName + " hash recognised (" + info.HexLength + " hexadecimal characters). "
                + "Select the file and choose Verify." + (info.IsLegacy ? "  Note: " + info.SecurityNote : string.Empty));
        }

        private enum ResultKind { Info, Success, Warning, Error }

        private void ShowResult(ResultKind kind, string message)
        {
            var p = Theme.Current;
            switch (kind)
            {
                case ResultKind.Success: lblResult.BackColor = p.SuccessTint; lblResult.ForeColor = p.Success; break;
                case ResultKind.Error: lblResult.BackColor = p.ErrorTint; lblResult.ForeColor = p.Error; break;
                case ResultKind.Warning: lblResult.BackColor = p.WarningTint; lblResult.ForeColor = p.Warning; break;
                default: lblResult.BackColor = p.InfoTint; lblResult.ForeColor = p.Text; break;
            }
            lblResult.Font = kind == ResultKind.Success || kind == ResultKind.Error ? new Font(Font, FontStyle.Bold) : Font;
            lblResult.Text = message;
            lblResult.AccessibleDescription = message;
            lblResult.MaximumSize = new Size(Math.Max(200, mainTable.ClientSize.Width - mainTable.Padding.Horizontal), 0);
            lblResult.Visible = true;
            if (kind != ResultKind.Info) SetStatus(message);
        }

        private void HideResult() { lblResult.Visible = false; }

        private void SetStatus(string text)
        {
            stsText.Text = text.Length > 300 ? text.Substring(0, 300) + "..." : text;
            stsText.AccessibilityObject.Name = stsText.Text;
        }

        private void ShowError(string message, Exception ex)
        {
            string detail = ErrorDescriber.Describe(ex);
            SetStatus(message + " " + detail);
            MessageBox.Show(this, message + "\n\n" + detail, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ------------------------------------------------------------------ settings, theme, Explorer

        private void SetTheme(ThemePreference theme)
        {
            app.Settings.Theme = theme;
            miThemeLight.Checked = theme == ThemePreference.Light;
            miThemeDark.Checked = theme == ThemePreference.Dark;
            Theme.Apply(this, Palette.For(theme));
            ApplyBrandAccents();
            Theme.StyleToolStrip(listMenu, Theme.Current);
            Theme.StyleToolStrip(exportMenu, Theme.Current);
            UpdateAlgorithmNote();
            list.Invalidate();
            dropZone.Invalidate();
            HideResult();
            SaveSettingsQuietly();
        }

        private void UpdateExplorerMenuState()
        {
            try
            {
                var state = app.Explorer.GetState(app.ExecutablePath);
                miExplorer.Checked = state.Enabled;
                miExplorer.ToolTipText = state.Enabled
                    ? (state.PointsToThisExecutable || state.MachineEntry ? "\"" + ExplorerIntegration.MenuText + "\" is shown in File Explorer."
                        : "The menu entry points to another copy of HT File Hash Checker. Turn it off and on again to use this copy.")
                    : "Adds \"" + ExplorerIntegration.MenuText + "\" when you right-click files or folders.";
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                miExplorer.Enabled = false;
            }
        }

        internal void ToggleExplorerIntegration()
        {
            bool enable = !miExplorer.Checked;
            if (enable && app.Paths.IsPortable &&
                MessageBox.Show(this, "This adds \"" + ExplorerIntegration.MenuText + "\" to the File Explorer right-click menu for your Windows account. "
                    + "It writes to your user registry (HKEY_CURRENT_USER) and points to this portable copy:\n\n" + app.ExecutablePath
                    + "\n\nIf you move or delete this folder later, turn the option off first. Continue?",
                    AppInfo.ProductName, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;
            SetExplorerIntegration(enable);
        }

        internal void SetExplorerIntegration(bool enable)
        {
            try
            {
                if (enable) app.Explorer.Register(app.ExecutablePath);
                else app.Explorer.Unregister();
                app.Settings.ExplorerIntegrationEnabled = enable;
                SaveSettingsQuietly();
                SetStatus(enable ? "Explorer right-click menu enabled." : "Explorer right-click menu removed.");
                app.Logger.Info("Explorer integration " + (enable ? "enabled" : "disabled") + ".");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException || ex is ArgumentException)
            {
                ShowError("The Explorer menu could not be changed.", ex);
            }
            UpdateExplorerMenuState();
        }

        private void ShowOptions()
        {
            using (var dlg = new SettingsForm(app.Settings.Clone(), miExplorer.Checked, app.Paths))
            {
                Theme.Apply(dlg, Theme.Current);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var updated = dlg.Settings;
                bool themeChanged = updated.Theme != app.Settings.Theme;
                // Keep window placement owned by this form.
                updated.WindowLeft = app.Settings.WindowLeft;
                updated.WindowTop = app.Settings.WindowTop;
                updated.WindowWidth = app.Settings.WindowWidth;
                updated.WindowHeight = app.Settings.WindowHeight;
                updated.WindowState = app.Settings.WindowState;
                app.Settings = updated;
                loading = true;
                chkSubfolders.Checked = updated.IncludeSubfolders;
                miSubfolders.Checked = updated.IncludeSubfolders;
                SelectAlgorithm(updated.DefaultAlgorithm);
                loading = false;
                if (themeChanged) SetTheme(updated.Theme);
                if (dlg.ExplorerIntegration != miExplorer.Checked) SetExplorerIntegration(dlg.ExplorerIntegration);
                SaveSettingsQuietly();
                UpdateAlgorithmNote();
                SetStatus("Settings saved.");
            }
        }

        private void ShowHelp(HelpForm.Topic topic)
        {
            using (var dlg = new HelpForm(topic))
            {
                Theme.Apply(dlg, Theme.Current);
                dlg.ShowDialog(this);
            }
        }

        /// <summary>Help > About > About this application: the standard HTREASURE About box.</summary>
        private void ShowAbout()
        {
            using (var dlg = new BrandAboutForm(AppInfo.ProductName, AppInfo.Version,
                "Calculate, compare and verify file checksums using SHA-256, SHA-512, SHA-384, SHA-1 and MD5. Works offline."))
            {
                dlg.AddDetail((Environment.Is64BitProcess ? "64-bit" : "32-bit") + " · .NET Framework " + AppInfo.TargetFrameworkDisplay
                    + (app.Paths.IsPortable ? " · portable" : string.Empty) + " · " + AppInfo.Support);
                dlg.AddLink("Licence and notices", (s, e) => AboutLinks.OpenLicence(dlg));
                dlg.ShowDialog(this);
            }
        }

        private void SaveSettingsQuietly()
        {
            string error;
            if (!app.TrySaveSettings(out error)) SetStatus("Settings could not be saved: " + error);
        }

        // ------------------------------------------------------------------ window placement and closing

        /// <summary>The window has a fixed size (no resizing, minimising or maximising). Only its position is remembered;
        /// the size is fitted to the screen each time by <see cref="FitToScreen"/>.</summary>
        private void RestoreWindowPlacement()
        {
            var s = app.Settings;
            if (s.WindowWidth <= 0 || s.WindowHeight <= 0) return;
            var probe = new Rectangle(s.WindowLeft, s.WindowTop, 200, 40);
            // Only restore if the title area would be visible on a connected screen.
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(probe))) return;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(s.WindowLeft, s.WindowTop);
        }

        /// <summary>
        /// Fixes the window at its design size, reduced when the screen is smaller (for example 1366x768 laptops at 125%
        /// scaling), and keeps it fully on the screen. The layout inside adapts to the size chosen here.
        /// </summary>
        internal void FitToScreen()
        {
            var area = Screen.FromPoint(new Point(Left + Width / 2, Top + 20)).WorkingArea;
            float scale = CurrentScale();
            var chrome = Size - ClientSize;
            int width = Math.Min((int)(DesignClientSize.Width * scale) + chrome.Width, (int)(area.Width * 0.96));
            int height = Math.Min((int)(DesignClientSize.Height * scale) + chrome.Height, (int)(area.Height * 0.96));
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            Size = new Size(width, height);
            // Lock the size (FixedSingle already prevents dragging; this also blocks programmatic and snap resizing).
            MinimumSize = Size;
            MaximumSize = Size;
            int x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            if (StartPosition == FormStartPosition.CenterScreen && !positioned)
            {
                x = area.Left + (area.Width - Width) / 2;
                y = area.Top + (area.Height - Height) / 2;
            }
            Location = new Point(x, y);
            positioned = true;
        }

        private bool positioned;

        private float CurrentScale()
        {
#if NET47_OR_GREATER
            return DeviceDpi / 96f;
#else
            using (var g = CreateGraphics()) return g.DpiX / 96f;
#endif
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitToScreen();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsBusy)
            {
                if (e.CloseReason == CloseReason.UserClosing &&
                    MessageBox.Show(this, "An operation is still running. Cancel it and close?", AppInfo.ProductName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                closeAfterRun = true;
                CancelRun();
                e.Cancel = true; // closes once the workers have stopped
                return;
            }
            // Fixed-size window: only the position matters (width/height are kept so older settings files stay valid).
            app.Settings.WindowLeft = Left;
            app.Settings.WindowTop = Top;
            app.Settings.WindowWidth = Width;
            app.Settings.WindowHeight = Height;
            app.Settings.WindowState = "Normal";
            app.Settings.DefaultAlgorithm = SelectedAlgorithm;
            string error;
            app.TrySaveSettings(out error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                uiTimer.Dispose();
                argTimer.Dispose();
                if (cts != null) cts.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
