using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Processing;

namespace HTFileHashChecker.Core.Reports
{
    public enum ReportFormat
    {
        Text,
        Csv,
        Html,
    }

    /// <summary>Everything a report contains. Built once, rendered by any writer.</summary>
    public sealed class ReportData
    {
        public ReportData()
        {
            GeneratedAt = DateTimeOffset.Now;
            ApplicationName = AppInfo.ProductName;
            ApplicationVersion = AppInfo.Version;
            Items = new List<HashItem>();
        }

        public string ApplicationName { get; set; }
        public string ApplicationVersion { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }

        /// <summary>The algorithm selected in the application.</summary>
        public HashAlgorithmKind SelectedAlgorithm { get; set; }
        public IList<HashItem> Items { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public string ManifestPath { get; set; }
        public string BaseDirectory { get; set; }

        public int TotalFiles { get { return Items.Count; } }
        public int ProcessedFiles { get { return Items.Count(i => i.Status != ItemStatus.Pending && i.Status != ItemStatus.Cancelled); } }
        public int VerificationFailures { get { return Items.Count(i => i.IsVerificationFailure); } }
        public int Errors { get { return Items.Count(i => i.Status == ItemStatus.Error); } }
        public int Matches { get { return Items.Count(i => i.Status == ItemStatus.Match); } }
        public int Mismatches { get { return Items.Count(i => i.Status == ItemStatus.Mismatch); } }
        public int Missing { get { return Items.Count(i => i.Status == ItemStatus.Missing); } }

        public IEnumerable<string> AlgorithmsUsed
        {
            get
            {
                return Items.Where(i => i.HashHex != null || i.Expected != null)
                    .Select(i => i.Algorithm).Distinct().OrderBy(a => (int)a)
                    .Select(a => HashAlgorithms.Get(a).DisplayName);
            }
        }

        public string Timestamp
        {
            get
            {
                return GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " (UTC"
                    + GeneratedAt.ToString("zzz", CultureInfo.InvariantCulture) + ")";
            }
        }

        public const string Disclaimer =
            "A matching checksum shows that a file is identical to the file the expected value was calculated from. " +
            "It does not show that the file is free of malware or that it comes from a trusted publisher: that depends on " +
            "obtaining the expected value from a trusted source (ideally over HTTPS from the publisher, or a signed manifest).";
    }

    public static class ReportService
    {
        public static string Render(ReportData data, ReportFormat format)
        {
            switch (format)
            {
                case ReportFormat.Text: return TextReportWriter.Render(data);
                case ReportFormat.Csv: return CsvReportWriter.Render(data);
                case ReportFormat.Html: return HtmlReportWriter.Render(data);
                default: throw new ArgumentOutOfRangeException("format");
            }
        }

        /// <summary>Saves the report atomically. Existing files are only replaced when <paramref name="overwrite"/> is true.</summary>
        public static void Save(string path, ReportData data, ReportFormat format, bool overwrite)
        {
            // CSV gets a BOM so Excel opens non-English file names correctly; TXT and HTML are plain UTF-8.
            var encoding = new UTF8Encoding(format == ReportFormat.Csv);
            AtomicFile.WriteAllText(path, Render(data, format), encoding, overwrite);
        }

        public static string Extension(ReportFormat format)
        {
            switch (format)
            {
                case ReportFormat.Csv: return ".csv";
                case ReportFormat.Html: return ".html";
                default: return ".txt";
            }
        }

        internal static string FormatElapsed(TimeSpan? elapsed)
        {
            if (!elapsed.HasValue) return string.Empty;
            return elapsed.Value.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s";
        }
    }

    public static class TextReportWriter
    {
        public static string Render(ReportData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine(d.ApplicationName + " - Verification Report");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine("Application:        " + d.ApplicationName + " " + d.ApplicationVersion);
            sb.AppendLine("Publisher:          " + AppInfo.Publisher);
            sb.AppendLine("Date and time:      " + d.Timestamp);
            sb.AppendLine("Selected algorithm: " + HashAlgorithms.Get(d.SelectedAlgorithm).DisplayName);
            sb.AppendLine("Algorithms used:    " + string.Join(", ", d.AlgorithmsUsed.DefaultIfEmpty("(none)")));
            if (d.ManifestPath != null) sb.AppendLine("Manifest:           " + d.ManifestPath);
            if (d.BaseDirectory != null) sb.AppendLine("Verification folder: " + d.BaseDirectory);
            sb.AppendLine();
            sb.AppendLine("SUMMARY");
            sb.AppendLine(new string('-', 72));
            sb.AppendLine("Total files:            " + d.TotalFiles);
            sb.AppendLine("Files processed:        " + d.ProcessedFiles);
            sb.AppendLine("Matches:                " + d.Matches);
            sb.AppendLine("Mismatches:             " + d.Mismatches);
            sb.AppendLine("Missing:                " + d.Missing);
            sb.AppendLine("Errors:                 " + d.Errors);
            sb.AppendLine("Verification failures:  " + d.VerificationFailures);
            sb.AppendLine("Total processing time:  " + ReportService.FormatElapsed(d.TotalProcessingTime));
            sb.AppendLine();
            sb.AppendLine("FILES");
            sb.AppendLine(new string('-', 72));
            int n = 0;
            foreach (var i in d.Items)
            {
                n++;
                sb.AppendLine("[" + n + "] " + i.FileName);
                sb.AppendLine("    Path:       " + i.FullPath);
                sb.AppendLine("    Size:       " + (i.Size.HasValue ? i.Size.Value.ToString("N0", CultureInfo.InvariantCulture) + " bytes" : "unknown"));
                sb.AppendLine("    Algorithm:  " + HashAlgorithms.Get(i.Algorithm).DisplayName);
                sb.AppendLine("    Calculated: " + (i.HashHex ?? "-"));
                if (i.Expected != null) sb.AppendLine("    Expected:   " + i.Expected.Hex);
                sb.AppendLine("    Status:     " + HashItem.StatusLabel(i.Status));
                if (!string.IsNullOrEmpty(i.Message)) sb.AppendLine("    Details:    " + OneLine(i.Message));
                if (i.Elapsed.HasValue) sb.AppendLine("    Time:       " + ReportService.FormatElapsed(i.Elapsed));
            }
            if (n == 0) sb.AppendLine("(no files)");
            sb.AppendLine();
            sb.AppendLine("NOTE");
            sb.AppendLine(new string('-', 72));
            sb.AppendLine(Wrap(ReportData.Disclaimer, 72));
            if (d.Items.Any(i => HashAlgorithms.Get(i.Algorithm).IsLegacy))
                sb.AppendLine(Wrap("MD5 and SHA-1 are legacy algorithms and are not suitable for security-sensitive authenticity verification.", 72));
            return sb.ToString();
        }

        private static string OneLine(string s) { return s.Replace("\r", " ").Replace("\n", " "); }

        private static string Wrap(string text, int width)
        {
            var sb = new StringBuilder();
            int col = 0;
            foreach (var word in text.Split(' '))
            {
                if (col > 0 && col + 1 + word.Length > width) { sb.AppendLine(); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(word);
                col += word.Length;
            }
            return sb.ToString();
        }
    }

    public static class CsvReportWriter
    {
        public static readonly string[] Columns =
        {
            "File Name", "Full Path", "Size (bytes)", "Algorithm", "Calculated Hash", "Expected Hash", "Status", "Details", "Elapsed (s)",
        };

        public static string Render(ReportData d)
        {
            var sb = new StringBuilder();
            Row(sb, "Application", d.ApplicationName + " " + d.ApplicationVersion);
            Row(sb, "Date and time", d.Timestamp);
            Row(sb, "Selected algorithm", HashAlgorithms.Get(d.SelectedAlgorithm).DisplayName);
            Row(sb, "Total files", d.TotalFiles.ToString(CultureInfo.InvariantCulture));
            Row(sb, "Files processed", d.ProcessedFiles.ToString(CultureInfo.InvariantCulture));
            Row(sb, "Verification failures", d.VerificationFailures.ToString(CultureInfo.InvariantCulture));
            Row(sb, "Total processing time (s)", d.TotalProcessingTime.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture));
            if (d.ManifestPath != null) Row(sb, "Manifest", d.ManifestPath);
            sb.Append("\r\n");
            Row(sb, Columns);
            foreach (var i in d.Items)
            {
                Row(sb,
                    i.FileName,
                    i.FullPath,
                    i.Size.HasValue ? i.Size.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    HashAlgorithms.Get(i.Algorithm).DisplayName,
                    i.HashHex ?? string.Empty,
                    i.Expected != null ? i.Expected.Hex : string.Empty,
                    HashItem.StatusLabel(i.Status),
                    i.Message ?? string.Empty,
                    i.Elapsed.HasValue ? i.Elapsed.Value.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty);
            }
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, params string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(fields[i]));
            }
            sb.Append("\r\n");
        }

        /// <summary>RFC 4180 quoting plus protection against spreadsheet formula injection: a value starting with
        /// = + - @ tab or CR is prefixed with an apostrophe so Excel and similar programs show it as text.</summary>
        public static string Escape(string value)
        {
            if (value == null) return string.Empty;
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value.StartsWith(" ", StringComparison.Ordinal)
                || value.EndsWith(" ", StringComparison.Ordinal);
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }

    public static class HtmlReportWriter
    {
        public static string Render(ReportData d)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
            // No scripts, no external resources: the report is safe to open even if a file name contains markup.
            sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append("<title>").Append(E(d.ApplicationName)).Append(" - Verification Report</title>\n");
            sb.Append("<style>\n")
              .Append("body{font-family:'Segoe UI',Arial,sans-serif;color:#1F2937;background:#F3F6FA;margin:0}")
              .Append("header{background:#0B3D91;color:#fff;padding:20px 28px}header h1{margin:0;font-size:22px}header p{margin:4px 0 0;opacity:.85}")
              .Append("main{padding:20px 28px}section{background:#fff;border:1px solid #D6DEEA;border-radius:6px;padding:16px 20px;margin-bottom:18px}")
              .Append("h2{font-size:16px;color:#0B3D91;margin:0 0 10px}dl{display:grid;grid-template-columns:max-content 1fr;gap:4px 18px;margin:0}dt{font-weight:600}dd{margin:0}")
              .Append("table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #D6DEEA;padding:6px 8px;text-align:left;vertical-align:top}")
              .Append("th{background:#E3EEFB;color:#0B3D91}td.hash{font-family:Consolas,monospace;word-break:break-all}td.path{word-break:break-all}")
              .Append(".s{font-weight:700;white-space:nowrap}.ok{color:#1E7E34}.bad{color:#C82333}.warn{color:#B36B00}")
              .Append(".note{font-size:13px;color:#5B6472}footer{padding:0 28px 24px;font-size:12px;color:#5B6472}")
              .Append("\n</style>\n</head>\n<body>\n");
            sb.Append("<header><h1>").Append(E(d.ApplicationName)).Append(" &ndash; Verification Report</h1><p>")
              .Append(E(AppInfo.Publisher)).Append("</p></header>\n<main>\n");

            sb.Append("<section><h2>Details</h2><dl>");
            Dl(sb, "Application", d.ApplicationName + " " + d.ApplicationVersion);
            Dl(sb, "Date and time", d.Timestamp);
            Dl(sb, "Selected algorithm", HashAlgorithms.Get(d.SelectedAlgorithm).DisplayName);
            Dl(sb, "Algorithms used", string.Join(", ", d.AlgorithmsUsed.DefaultIfEmpty("(none)")));
            if (d.ManifestPath != null) Dl(sb, "Manifest", d.ManifestPath);
            if (d.BaseDirectory != null) Dl(sb, "Verification folder", d.BaseDirectory);
            sb.Append("</dl></section>\n");

            sb.Append("<section><h2>Summary</h2><dl>");
            Dl(sb, "Total files", d.TotalFiles.ToString(CultureInfo.InvariantCulture));
            Dl(sb, "Files processed", d.ProcessedFiles.ToString(CultureInfo.InvariantCulture));
            Dl(sb, "Matches", d.Matches.ToString(CultureInfo.InvariantCulture));
            Dl(sb, "Mismatches", d.Mismatches.ToString(CultureInfo.InvariantCulture));
            Dl(sb, "Missing", d.Missing.ToString(CultureInfo.InvariantCulture));
            Dl(sb, "Errors", d.Errors.ToString(CultureInfo.InvariantCulture));
            Dl(sb, "Verification failures", d.VerificationFailures.ToString(CultureInfo.InvariantCulture));
            Dl(sb, "Total processing time", ReportService.FormatElapsed(d.TotalProcessingTime));
            sb.Append("</dl></section>\n");

            sb.Append("<section><h2>Files</h2><table><thead><tr>");
            foreach (var c in new[] { "File", "Path", "Size (bytes)", "Algorithm", "Calculated hash", "Expected hash", "Status", "Details", "Time" })
                sb.Append("<th>").Append(E(c)).Append("</th>");
            sb.Append("</tr></thead><tbody>\n");
            foreach (var i in d.Items)
            {
                sb.Append("<tr><td>").Append(E(i.FileName)).Append("</td>")
                  .Append("<td class=\"path\">").Append(E(i.FullPath)).Append("</td>")
                  .Append("<td>").Append(i.Size.HasValue ? i.Size.Value.ToString("N0", CultureInfo.InvariantCulture) : string.Empty).Append("</td>")
                  .Append("<td>").Append(E(HashAlgorithms.Get(i.Algorithm).DisplayName)).Append("</td>")
                  .Append("<td class=\"hash\">").Append(E(i.HashHex ?? string.Empty)).Append("</td>")
                  .Append("<td class=\"hash\">").Append(E(i.Expected != null ? i.Expected.Hex : string.Empty)).Append("</td>")
                  .Append("<td class=\"s ").Append(StatusClass(i.Status)).Append("\">").Append(E(HashItem.StatusLabel(i.Status))).Append("</td>")
                  .Append("<td>").Append(E(i.Message ?? string.Empty)).Append("</td>")
                  .Append("<td>").Append(E(ReportService.FormatElapsed(i.Elapsed))).Append("</td></tr>\n");
            }
            sb.Append("</tbody></table></section>\n");

            sb.Append("<section><h2>Important</h2><p class=\"note\">").Append(E(ReportData.Disclaimer)).Append("</p>");
            if (d.Items.Any(i => HashAlgorithms.Get(i.Algorithm).IsLegacy))
                sb.Append("<p class=\"note\">MD5 and SHA-1 are legacy algorithms and are not suitable for security-sensitive authenticity verification.</p>");
            sb.Append("</section>\n</main>\n<footer>Generated by ").Append(E(d.ApplicationName + " " + d.ApplicationVersion))
              .Append(" &middot; ").Append(E(AppInfo.Copyright)).Append("</footer>\n</body>\n</html>\n");
            return sb.ToString();
        }

        private static void Dl(StringBuilder sb, string term, string value)
        {
            sb.Append("<dt>").Append(E(term)).Append("</dt><dd>").Append(E(value)).Append("</dd>");
        }

        private static string StatusClass(ItemStatus s)
        {
            switch (s)
            {
                case ItemStatus.Match:
                case ItemStatus.Hashed: return "ok";
                case ItemStatus.Mismatch:
                case ItemStatus.Error:
                case ItemStatus.Invalid: return "bad";
                default: return "warn";
            }
        }

        /// <summary>HTML-encodes text (&lt; &gt; &amp; " ' and non-ASCII where needed).</summary>
        public static string E(string s)
        {
            return WebUtility.HtmlEncode(s ?? string.Empty);
        }
    }
}
