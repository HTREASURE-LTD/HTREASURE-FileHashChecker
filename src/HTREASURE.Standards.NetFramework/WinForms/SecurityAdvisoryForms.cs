using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using HTREASURE.Standards.Security;

namespace HTREASURE.Standards.WinForms
{
    /// <summary>HTREASURE corporate palette plus severity and status colours (port of SecurityTheme).</summary>
    public static class SecurityTheme
    {
        public static readonly Color DeepNavy = ColorTranslator.FromHtml("#0B1F3A");
        public static readonly Color TechnologyBlue = ColorTranslator.FromHtml("#0B3D91");
        public static readonly Color ElectricBlue = ColorTranslator.FromHtml("#168BFF");
        public static readonly Color PageGrey = ColorTranslator.FromHtml("#F4F6F9");
        public static readonly Color LineGrey = ColorTranslator.FromHtml("#DDE3EB");
        public static readonly Color MutedText = ColorTranslator.FromHtml("#5A6577");
        public static readonly Color SilverGrey = ColorTranslator.FromHtml("#AAB4C3");
        public static readonly Color Red = ColorTranslator.FromHtml("#B42318");
        public static readonly Color Orange = ColorTranslator.FromHtml("#C4320A");
        public static readonly Color Amber = ColorTranslator.FromHtml("#B54708");
        public static readonly Color AmberBg = ColorTranslator.FromHtml("#FFF6E5");
        public static readonly Color Green = ColorTranslator.FromHtml("#067647");
        public static readonly Color Slate = ColorTranslator.FromHtml("#475467");
        public static readonly Color BlueBg = ColorTranslator.FromHtml("#EAF3FF");
        public static readonly Color RedBg = ColorTranslator.FromHtml("#FEF3F2");

        public static Font Ui(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style); }

        public static Color For(AdvisorySeverity s)
        {
            switch (s)
            {
                case AdvisorySeverity.Critical: return Red;
                case AdvisorySeverity.High: return Orange;
                case AdvisorySeverity.Medium: return Amber;
                default: return TechnologyBlue;
            }
        }

        public static Color For(AdvisoryStatus s)
        {
            switch (s)
            {
                case AdvisoryStatus.Affected: return Red;
                case AdvisoryStatus.UnderReview: return Amber;
                case AdvisoryStatus.Unknown: return Slate;
                default: return Green;
            }
        }

        public static string Text(AdvisorySeverity s) { return s.ToString(); }
        public static string Text(AdvisoryStatus s) { return SecurityReport.StatusText(s); }

        public static Button PrimaryButton(string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, MinimumSize = new Size(150, 36), FlatStyle = FlatStyle.Flat,
                BackColor = TechnologyBlue, ForeColor = Color.White, Font = Ui(9.5f, FontStyle.Bold),
                Margin = new Padding(8, 0, 0, 0), Padding = new Padding(10, 2, 10, 2), Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.EnabledChanged += (s, e) => b.BackColor = b.Enabled ? TechnologyBlue : SilverGrey;
            return b;
        }

        public static Button SecondaryButton(string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, MinimumSize = new Size(110, 36), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, ForeColor = DeepNavy, Font = Ui(9.5f),
                Margin = new Padding(8, 0, 0, 0), Padding = new Padding(10, 2, 10, 2), Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = SilverGrey;
            return b;
        }

        public static Panel Header(string title, string subtitle)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = DeepNavy, Padding = new Padding(24, 12, 24, 10) };
            var t = new Label { Text = title, AutoSize = true, ForeColor = Color.White, Font = Ui(17, FontStyle.Bold), Location = new Point(22, 10), BackColor = Color.Transparent, UseMnemonic = false };
            var s = new Label { Text = subtitle, AutoSize = true, ForeColor = ColorTranslator.FromHtml("#C9D4E5"), Font = Ui(9.5f), Location = new Point(24, 46), BackColor = Color.Transparent, UseMnemonic = false };
            var brand = new Label { Text = "HTREASURE LTD", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#C9D4E5"), Font = Ui(9, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.Transparent };
            panel.Controls.AddRange(new Control[] { t, s, brand });
            panel.Layout += (o, e) => brand.Location = new Point(panel.ClientSize.Width - brand.Width - 24, 16);
            return panel;
        }

        public static Label Badge(string text, Color colour)
        {
            return new Label
            {
                Text = text.ToUpperInvariant(), AutoSize = true, ForeColor = Color.White, BackColor = colour,
                Font = Ui(8.5f, FontStyle.Bold), Padding = new Padding(6, 3, 6, 3), Margin = new Padding(0, 0, 8, 0),
            };
        }
    }

    /// <summary>
    /// Help → About → Security Advisory History (and Help → Security Advisories). Opens instantly from local data,
    /// so it works offline. Online actions are explicit buttons.
    /// </summary>
    public sealed class SecurityAdvisoryHistoryForm : Form
    {
        private readonly SecurityAdvisoryCenter center;
        private readonly bool updateServiceConfigured;
        private readonly Panel banner = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(24, 0, 24, 0) };
        private readonly Label bannerText = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = SecurityTheme.Ui(9.5f, FontStyle.Bold), UseMnemonic = false };
        private readonly TableLayoutPanel cards = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, RowCount = 2, Height = 200, Padding = new Padding(18, 12, 18, 4), BackColor = SecurityTheme.PageGrey };
        private readonly ListView list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BorderStyle = BorderStyle.None, Font = SecurityTheme.Ui(9.5f), HeaderStyle = ColumnHeaderStyle.Nonclickable, AccessibleName = "Advisory history",
        };
        private readonly Panel emptyState = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Visible = false };
        private readonly Label listTitle = new Label { Dock = DockStyle.Top, Height = 34, Font = SecurityTheme.Ui(11.5f, FontStyle.Bold), ForeColor = SecurityTheme.DeepNavy, TextAlign = ContentAlignment.BottomLeft };
        private readonly Label warnings = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 0, ForeColor = SecurityTheme.Amber, Font = SecurityTheme.Ui(8.75f), UseMnemonic = false };
        private readonly Button checkAdvisories = SecurityTheme.SecondaryButton("Check for Advisories");
        private readonly Button checkUpdates = SecurityTheme.PrimaryButton("Check for Security Updates");
        private readonly Button export = SecurityTheme.SecondaryButton("Export Security Report…");
        private readonly Button details = SecurityTheme.SecondaryButton("View Details");
        private readonly Label status = new Label { AutoSize = true, ForeColor = SecurityTheme.MutedText, Font = SecurityTheme.Ui(9), Margin = new Padding(0, 10, 0, 0), UseMnemonic = false };
        private SecurityAdvisoryView view;

        public SecurityAdvisoryHistoryForm(SecurityAdvisoryCenter center, bool updateServiceConfigured, Icon icon = null)
        {
            this.center = center;
            this.updateServiceConfigured = updateServiceConfigured;
            Text = "Security Advisory History - " + center.Application.Name;
            if (icon != null) Icon = icon;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1100, 740);
            MinimumSize = new Size(860, 600);
            BackColor = SecurityTheme.PageGrey;
            Font = SecurityTheme.Ui(9.5f);
            ShowInTaskbar = false;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            list.Columns.Add("Advisory ID", 150);
            list.Columns.Add("Severity", 80);
            list.Columns.Add("Title", 290);
            list.Columns.Add("Affected versions", 150);
            list.Columns.Add("Fixed version", 110);
            list.Columns.Add("Published", 90);
            list.Columns.Add("Status", 110);
            list.DoubleClick += (s, e) => ShowDetails();
            list.SelectedIndexChanged += (s, e) => details.Enabled = list.SelectedItems.Count > 0;
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ShowDetails(); };

            var emptyTitle = new Label { Text = "No verified advisory records loaded", AutoSize = true, Font = SecurityTheme.Ui(12, FontStyle.Bold), ForeColor = SecurityTheme.DeepNavy, Location = new Point(8, 40) };
            var emptyBody = new Label
            {
                AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = SecurityTheme.MutedText, Font = SecurityTheme.Ui(9.5f), Location = new Point(8, 72),
                Text = "Security advisories and previously fixed vulnerabilities will appear here when verified records are available. "
                     + "An empty list is not proof that this software is free from vulnerabilities.",
            };
            emptyState.Controls.AddRange(new Control[] { emptyTitle, emptyBody });

            var listCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16, 0, 16, 12) };
            listCard.Controls.Add(list);
            listCard.Controls.Add(emptyState);
            listCard.Controls.Add(listTitle);
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 8), BackColor = SecurityTheme.PageGrey };
            listHost.Controls.Add(listCard);

            var policy = new Label { Text = SecurityAdvisoryView.PolicyStatement, AutoSize = true, ForeColor = SecurityTheme.MutedText, Font = SecurityTheme.Ui(8.75f, FontStyle.Italic), Margin = new Padding(0, 0, 0, 6) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var close = SecurityTheme.SecondaryButton("Close");
            close.Click += (s, e) => Close();
            buttons.Controls.AddRange(new Control[] { checkAdvisories, export, details, checkUpdates, close });
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(24, 8, 24, 14), BackColor = SecurityTheme.PageGrey };
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            left.Controls.AddRange(new Control[] { policy, status });
            footer.Controls.Add(left);
            footer.Controls.Add(buttons);
            footer.Controls.Add(warnings);

            banner.Controls.Add(bannerText);
            Controls.Add(listHost);
            Controls.Add(footer);
            Controls.Add(cards);
            Controls.Add(banner);
            Controls.Add(SecurityTheme.Header("Security Advisory History", center.Application.Name + " · " + center.Application.Version));

            checkAdvisories.Click += async (s, e) =>
            {
                SetBusy(true, "Checking the HTREASURE advisory service…");
                try
                {
                    var outcome = await center.SyncAsync(System.Threading.CancellationToken.None);
                    view = outcome.View;
                    Render();
                    status.Text = outcome.Message;
                }
                finally { SetBusy(false, null); }
            };
            checkUpdates.Click += (s, e) => MessageBox.Show(this,
                "The HTREASURE update service is not configured for this product yet, so no security update can be checked or offered.\n\n"
                + "Nothing will be downloaded or installed from this window until a signed release feed is provisioned.",
                "Security updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            export.Click += (s, e) => ExportReport();
            details.Click += (s, e) => ShowDetails();
            details.Enabled = false;
            checkUpdates.Enabled = true;
            AcceptButton = details;
            CancelButton = close;

            view = center.LoadOffline();
            Render();
        }

        public SecurityAdvisoryView CurrentView { get { return view; } }

        private void Render()
        {
            var app = view.Application;
            switch (view.Source)
            {
                case AdvisorySource.LiveVerified:
                    SetBanner(SecurityTheme.BlueBg, SecurityTheme.TechnologyBlue, "Live advisory information verified " + Fmt(view.LastSuccessfulCheck ?? view.DataGeneratedAt) + ".");
                    break;
                case AdvisorySource.TestFixture:
                    SetBanner(SecurityTheme.RedBg, SecurityTheme.Red, "TEST FIXTURE DATA - illustrative records for testing only. Not real security information.");
                    break;
                case AdvisorySource.BundledSnapshot:
                    SetBanner(SecurityTheme.AmberBg, SecurityTheme.Amber, "Offline history: snapshot bundled with this release on " + FmtDate(view.DataGeneratedAt)
                        + ". This is historical information, not a current security assessment.");
                    break;
                case AdvisorySource.LiveCachedHistorical:
                    SetBanner(SecurityTheme.AmberBg, SecurityTheme.Amber, "Showing advisory data last verified on " + FmtDate(view.DataGeneratedAt)
                        + ". It may be out of date; check for advisories when online.");
                    break;
                default:
                    SetBanner(SecurityTheme.AmberBg, SecurityTheme.Amber, "No verified advisory data is available. The security status of this installation is Unknown.");
                    break;
            }

            cards.SuspendLayout();
            cards.Controls.Clear();
            var outstanding = view.Outstanding.ToList();
            int affected = outstanding.Count(a => a.Status == AdvisoryStatus.Affected);
            cards.Controls.Add(Card("Installed version", app.Version, app.Channel + " channel · " + app.Platform, null), 0, 0);
            cards.Controls.Add(Card("Advisory assessment", view.OverallAssessment, AssessmentNote(), AssessmentColour()), 1, 0);
            cards.Controls.Add(Card("Last advisory check", view.LastSuccessfulCheck == null ? "Never" : Fmt(view.LastSuccessfulCheck),
                view.LastCheckError ?? (view.AdvisoryServiceConfigured ? "HTREASURE advisory service" : "Advisory service not configured"), null), 2, 0);
            cards.Controls.Add(Card("Advisory data source", SourceTitle(), view.SourceDescription, null), 3, 0);
            cards.Controls.Add(Card("Security fixes included", view.FixesIncluded.Count().ToString(CultureInfo.CurrentCulture), "Verified as corrected in this version", SecurityTheme.Green), 0, 1);
            cards.Controls.Add(Card("Outstanding advisories", outstanding.Count.ToString(CultureInfo.CurrentCulture),
                outstanding.Count == 0 ? "None known in this data" : affected + " affected · " + (outstanding.Count - affected) + " under review or unknown",
                affected > 0 ? SecurityTheme.Red : outstanding.Count > 0 ? (Color?)SecurityTheme.Amber : null), 1, 1);
            cards.Controls.Add(Card("Security update availability", "Not checked",
                updateServiceConfigured ? "Requires your approval to install" : "Update service not configured", null), 2, 1);
            cards.Controls.Add(Card("Bundled snapshot", view.BundledSnapshotDate == null ? "None" : FmtDate(view.BundledSnapshotDate), "Available offline", null), 3, 1);
            cards.ResumeLayout();

            list.BeginUpdate();
            list.Items.Clear();
            foreach (var a in view.Assessments)
            {
                var adv = a.Advisory;
                string ids = adv.Id + (adv.Aliases != null && adv.Aliases.Count > 0 ? " / " + string.Join(", ", adv.Aliases.Select(x => x.Id)) : "");
                var item = new ListViewItem(ids) { Tag = a, UseItemStyleForSubItems = false };
                item.SubItems.Add(SecurityTheme.Text(adv.SeverityLevel), SecurityTheme.For(adv.SeverityLevel), Color.White, SecurityTheme.Ui(9.5f, FontStyle.Bold));
                item.SubItems.Add(adv.Title);
                item.SubItems.Add(adv.AffectedVersionsText);
                item.SubItems.Add(adv.FixedVersionText);
                item.SubItems.Add(adv.Published);
                item.SubItems.Add(SecurityTheme.Text(a.Status), SecurityTheme.For(a.Status), Color.White, SecurityTheme.Ui(9.5f, FontStyle.Bold));
                list.Items.Add(item);
            }
            list.EndUpdate();
            listTitle.Text = view.Assessments.Count == 0 ? "Advisory history" : "Advisory history (" + view.Assessments.Count + ")";
            bool empty = view.Assessments.Count == 0;
            emptyState.Visible = empty;
            list.Visible = !empty;
            if (list.Items.Count > 0) list.Items[0].Selected = true;
            warnings.Text = string.Join("  ", view.Warnings.Distinct());
            // Font height already reflects the display scale (LogicalToDeviceUnits needs .NET Framework 4.7+).
            warnings.Height = warnings.Text.Length == 0 ? 0 : warnings.Font.Height + 6;
        }

        private void SetBanner(Color back, Color fore, string text)
        {
            banner.BackColor = back;
            bannerText.ForeColor = fore;
            bannerText.Text = text;
            bannerText.AccessibleDescription = text;
        }

        private string SourceTitle()
        {
            switch (view.Source)
            {
                case AdvisorySource.LiveVerified: return "Live (verified)";
                case AdvisorySource.LiveCachedHistorical: return "Cached (historical)";
                case AdvisorySource.BundledSnapshot: return "Bundled snapshot";
                case AdvisorySource.TestFixture: return "Test fixtures";
                default: return "None";
            }
        }

        private string AssessmentNote()
        {
            return view.Source == AdvisorySource.None ? "No verified advisory data" : view.IsHistorical ? "Based on historical data" : "Based on live verified data";
        }

        private Color? AssessmentColour()
        {
            switch (view.OverallAssessment)
            {
                case "Action required": return SecurityTheme.Red;
                case "Review pending":
                case "Unknown": return SecurityTheme.Amber;
                default: return null;
            }
        }

        private static Control Card(string label, string value, string note, Color? accent)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(6), Padding = new Padding(0, 8, 8, 4) };
            var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
            var l = new Label { Text = label.ToUpperInvariant(), Dock = DockStyle.Top, Height = 17, Font = SecurityTheme.Ui(8, FontStyle.Bold), ForeColor = SecurityTheme.MutedText };
            var v = new Label { Text = value, Dock = DockStyle.Top, Height = 28, AutoEllipsis = true, Font = SecurityTheme.Ui(value.Length > 20 ? 10.5f : 12.5f, FontStyle.Bold), ForeColor = accent ?? SecurityTheme.DeepNavy, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            var n = new Label { Text = note, Dock = DockStyle.Fill, AutoEllipsis = true, Font = SecurityTheme.Ui(8.5f), ForeColor = SecurityTheme.MutedText, UseMnemonic = false };
            var bar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent ?? SecurityTheme.ElectricBlue };
            inner.Controls.AddRange(new Control[] { n, v, l });
            p.Controls.AddRange(new Control[] { inner, bar });
            p.AccessibleName = label + ": " + value + ". " + note;
            var tip = new ToolTip();
            tip.SetToolTip(n, note);
            tip.SetToolTip(v, value);
            return p;
        }

        private void ShowDetails()
        {
            if (list.SelectedItems.Count == 0) return;
            var a = list.SelectedItems[0].Tag as AdvisoryAssessment;
            if (a == null) return;
            using (var dlg = new AdvisoryDetailsForm(a, view, Icon)) dlg.ShowDialog(this);
        }

        private void ExportReport()
        {
            using (var save = new SaveFileDialog
            {
                Title = "Export Security Report",
                Filter = "Markdown report (*.md)|*.md|JSON report (*.json)|*.json",
                FileName = view.Application.AppId + "-" + view.Application.Version + "-security-report-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".md",
                OverwritePrompt = true,
                AddExtension = true,
            })
            {
                if (save.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var now = DateTimeOffset.Now;
                    File.WriteAllText(save.FileName, save.FilterIndex == 2 ? SecurityReport.ToJson(view, now) : SecurityReport.ToMarkdown(view, now), new UTF8Encoding(false));
                    status.Text = "Security report saved to " + save.FileName;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show(this, ex.Message, "Cannot save report", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SetBusy(bool busy, string message)
        {
            UseWaitCursor = busy;
            checkAdvisories.Enabled = !busy;
            if (message != null) status.Text = message;
        }

        private static string Fmt(DateTimeOffset? d) { return d.HasValue ? d.Value.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture) : "Never"; }
        private static string FmtDate(DateTimeOffset? d) { return d.HasValue ? d.Value.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) : "Unknown"; }
    }

    /// <summary>Full details of one advisory, rendered in the internal Markdown viewer.</summary>
    public sealed class AdvisoryDetailsForm : Form
    {
        public AdvisoryDetailsForm(AdvisoryAssessment assessment, SecurityAdvisoryView view, Icon icon)
        {
            var a = assessment.Advisory;
            Text = a.Id + " - Advisory details";
            if (icon != null) Icon = icon;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 680);
            MinimumSize = new Size(640, 480);
            BackColor = SecurityTheme.PageGrey;
            Font = SecurityTheme.Ui(9.5f);
            ShowInTaskbar = false;
            MinimizeBox = false;

            var badges = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(22, 10, 22, 0), BackColor = Color.White, WrapContents = false };
            badges.Controls.Add(SecurityTheme.Badge("Severity: " + SecurityTheme.Text(a.SeverityLevel), SecurityTheme.For(a.SeverityLevel)));
            badges.Controls.Add(SecurityTheme.Badge("Status: " + SecurityTheme.Text(assessment.Status), SecurityTheme.For(assessment.Status)));
            if (view.Source == AdvisorySource.TestFixture) badges.Controls.Add(SecurityTheme.Badge("Test fixture data", SecurityTheme.Slate));
            else if (view.IsHistorical) badges.Controls.Add(SecurityTheme.Badge("Historical data", SecurityTheme.Amber));

            var md = new MarkdownView { Dock = DockStyle.Fill, Markdown = BuildMarkdown(assessment, view) };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 8, 14, 8), BackColor = Color.White };
            body.Controls.Add(md);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 12, 16, 10), BackColor = SecurityTheme.PageGrey };
            var close = SecurityTheme.SecondaryButton("Close");
            close.Click += (s, e) => Close();
            buttons.Controls.Add(close);
            CancelButton = close;
            Controls.Add(body);
            Controls.Add(buttons);
            Controls.Add(badges);
            Controls.Add(SecurityTheme.Header(a.Id, a.Title));
        }

        internal static string BuildMarkdown(AdvisoryAssessment assessment, SecurityAdvisoryView view)
        {
            var a = assessment.Advisory;
            var sb = new StringBuilder();
            sb.AppendLine("| Field | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Advisory ID | " + a.Id + " |");
            if (a.Aliases != null && a.Aliases.Count > 0) sb.AppendLine("| External references | " + string.Join(", ", a.Aliases.Select(x => x.Id)) + " |");
            sb.AppendLine("| Severity | " + SecurityTheme.Text(a.SeverityLevel) + (a.Cvss != null ? " (CVSS " + a.Cvss.Version + " " + a.Cvss.BaseScore.ToString("0.0", CultureInfo.InvariantCulture) + ")" : "") + " |");
            sb.AppendLine("| Affected versions | " + a.AffectedVersionsText + " |");
            sb.AppendLine("| Fixed version | " + a.FixedVersionText + " |");
            sb.AppendLine("| Published | " + a.Published + " |");
            sb.AppendLine("| Last updated | " + a.Updated + " |");
            sb.AppendLine("| Status | " + SecurityTheme.Text(assessment.Status) + " |");
            sb.AppendLine("| Installed | " + view.Application.Version + " (" + view.Application.Channel + ", " + view.Application.Platform + ") |");
            sb.AppendLine();
            sb.AppendLine("## Why this status");
            sb.AppendLine(assessment.Reason);
            if (view.IsHistorical)
            {
                sb.AppendLine();
                sb.AppendLine("> This status is based on historical advisory data and is not a current security assessment.");
            }
            sb.AppendLine();
            sb.AppendLine("## Details");
            sb.AppendLine(a.Summary);
            sb.AppendLine();
            sb.AppendLine("## Remediation");
            sb.AppendLine(a.Remediation);
            if (!string.IsNullOrWhiteSpace(a.Mitigation)) { sb.AppendLine(); sb.AppendLine("## Mitigation"); sb.AppendLine(a.Mitigation); }
            if (!string.IsNullOrWhiteSpace(a.ReleaseNotes)) { sb.AppendLine(); sb.AppendLine("## Release notes"); sb.AppendLine(a.ReleaseNotes); }
            if (a.References != null && a.References.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## References");
                foreach (var r in a.References) sb.AppendLine("- " + r.Type + ": " + r.Url);
            }
            return sb.ToString();
        }
    }

    /// <summary>Internal Markdown viewer: headings, paragraphs, lists, quotes, code, tables (monospace) and bold.
    /// No HTML, scripts, images or remote content; links are shown as text and never opened automatically.</summary>
    public sealed class MarkdownView : RichTextBox
    {
        private static readonly Regex Bold = new Regex(@"\*\*([^*\n]+)\*\*", RegexOptions.CultureInvariant);
        private readonly Font body = SecurityTheme.Ui(10);
        private readonly Font bold = SecurityTheme.Ui(10, FontStyle.Bold);
        private readonly Font italic = SecurityTheme.Ui(10, FontStyle.Italic);
        private readonly Font mono = new Font("Consolas", 9.5f);
        private readonly Font h1 = SecurityTheme.Ui(15, FontStyle.Bold);
        private readonly Font h2 = SecurityTheme.Ui(12.5f, FontStyle.Bold);
        private string source = string.Empty;

        public MarkdownView()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            BackColor = Color.White;
            ForeColor = SecurityTheme.DeepNavy;
            DetectUrls = false;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            WordWrap = true;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Markdown
        {
            get { return source; }
            set { source = value ?? string.Empty; if (IsHandleCreated) Render(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Render();
        }

        private void Append(string text, Font font, Color? colour = null)
        {
            SelectionStart = TextLength;
            SelectionLength = 0;
            SelectionFont = font;
            SelectionColor = colour ?? SecurityTheme.DeepNavy;
            AppendText(text);
        }

        private void Inline(string text, Font font)
        {
            int last = 0;
            foreach (Match m in Bold.Matches(text))
            {
                Append(text.Substring(last, m.Index - last), font);
                Append(m.Groups[1].Value, bold);
                last = m.Index + m.Length;
            }
            Append(text.Substring(last), font);
        }

        private void Render()
        {
            Clear();
            bool code = false;
            foreach (string raw in source.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("```", StringComparison.Ordinal)) { code = !code; continue; }
                if (code) { Append(line + "\n", mono, SecurityTheme.Slate); continue; }
                if (line.StartsWith("# ", StringComparison.Ordinal)) { Append(line.Substring(2) + "\n", h1); continue; }
                if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
                {
                    Append("\n" + line.TrimStart('#').Trim() + "\n", h2);
                    continue;
                }
                if (line.StartsWith("> ", StringComparison.Ordinal)) { Append(line.Substring(2) + "\n", italic, SecurityTheme.Amber); continue; }
                if (line.StartsWith("|", StringComparison.Ordinal))
                {
                    if (Regex.IsMatch(line, @"^\|[\s\-|:]+\|$")) continue;
                    var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    if (cells.Length == 2) { Append(cells[0].PadRight(22), bold); Append(cells[1] + "\n", body); }
                    else Append(string.Join("   ", cells) + "\n", mono);
                    continue;
                }
                if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                {
                    Append("  • ", body);
                    Inline(line.Substring(2) + "\n", body);
                    continue;
                }
                Inline(line + "\n", body);
            }
            SelectionStart = 0;
            SelectionLength = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { body.Dispose(); bold.Dispose(); italic.Dispose(); mono.Dispose(); h1.Dispose(); h2.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>One-line integration: Help → About submenu and Help → Security Advisories.</summary>
    public static class SecurityAdvisoryUi
    {
        public const string HistoryMenuText = "Security Advisory &History";
        public const string AdvisoriesMenuText = "Security &Advisories";

        public static ToolStripMenuItem CreateAboutMenu(IWin32Window owner, Action showAbout, Func<HTStandardsRuntime> runtime, Icon icon = null)
        {
            var about = new ToolStripMenuItem("&About");
            about.DropDownItems.Add("&About this application", null, (s, e) => showAbout());
            about.DropDownItems.Add(new ToolStripSeparator());
            about.DropDownItems.Add(HistoryMenuText, null, (s, e) => ShowHistory(owner, runtime(), icon));
            return about;
        }

        public static ToolStripMenuItem CreateAdvisoriesMenuItem(IWin32Window owner, Func<HTStandardsRuntime> runtime, Icon icon = null)
        {
            return new ToolStripMenuItem(AdvisoriesMenuText, null, (s, e) => ShowHistory(owner, runtime(), icon));
        }

        public static void ShowHistory(IWin32Window owner, HTStandardsRuntime runtime, Icon icon = null)
        {
            using (var form = new SecurityAdvisoryHistoryForm(runtime.Advisories, runtime.ReleaseFeedUrl != null, icon)) form.ShowDialog(owner);
        }
    }
}
