using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Processing;
using HTFileHashChecker.Core.Verification;

namespace HTFileHashChecker.Core.Manifests
{
    public sealed class ManifestVerificationOptions
    {
        /// <summary>Folder the manifest's relative paths are resolved against (normally the manifest's own folder).</summary>
        public string BaseDirectory { get; set; }

        /// <summary>Complete folder comparison: also list files in the folder that the manifest does not mention.</summary>
        public bool ReportUnexpectedFiles { get; set; }

        /// <summary>Full path of the manifest, excluded from the unexpected-file check.</summary>
        public string ManifestPath { get; set; }
    }

    public sealed class ManifestVerificationPlan
    {
        public ManifestVerificationPlan()
        {
            Items = new List<HashItem>();
            ScanIssues = new List<ScanIssue>();
        }

        /// <summary>One item per manifest entry (pending, missing or invalid) plus unexpected files.</summary>
        public List<HashItem> Items { get; private set; }
        public List<ScanIssue> ScanIssues { get; private set; }
    }

    /// <summary>Turns a parsed manifest into items for <see cref="BatchProcessor"/>. Missing, invalid and unexpected
    /// entries are decided here; the remaining items are hashed and compared by the processor.</summary>
    public static class ManifestVerifier
    {
        public static ManifestVerificationPlan CreatePlan(ManifestDocument manifest, ManifestVerificationOptions options,
            CancellationToken cancellationToken)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (options == null || string.IsNullOrEmpty(options.BaseDirectory)) throw new ArgumentException("A base folder is required.");
            string baseDir = Path.GetFullPath(options.BaseDirectory);
            if (!Directory.Exists(baseDir)) throw new DirectoryNotFoundException("Verification folder not found: " + baseDir);

            var plan = new ManifestVerificationPlan();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string full;
                string error;
                bool resolved = PathValidator.TryResolveManifestPath(baseDir, entry.RelativePath, out full, out error);
                var item = new HashItem(resolved ? full : Path.Combine(baseDir, SafeDisplayName(entry.RelativePath)), ItemSource.Manifest)
                {
                    RelativePath = entry.RelativePath.Replace('\\', '/'),
                    Algorithm = entry.Algorithm,
                    Expected = ExpectedHash.FromBytes(entry.Algorithm, entry.HashBytes),
                    ManifestLine = entry.LineNumber,
                    ContainmentRoot = baseDir,
                };
                if (!entry.IsUsable || !resolved)
                {
                    item.Status = ItemStatus.Invalid;
                    item.Message = entry.Problem ?? error;
                }
                else
                {
                    listed.Add(full);
                    if (Directory.Exists(full))
                    {
                        item.Status = ItemStatus.Invalid;
                        item.Message = "The manifest entry names a folder, not a file.";
                    }
                    else if (!File.Exists(full))
                    {
                        item.Status = ItemStatus.Missing;
                        item.Message = HashItem.StatusDescription(ItemStatus.Missing);
                    }
                    else
                    {
                        try { item.Size = new FileInfo(full).Length; }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                    }
                }
                plan.Items.Add(item);
            }

            if (options.ReportUnexpectedFiles)
            {
                var scan = FileScanner.Scan(baseDir, new ScanOptions { Recursive = true, SkipOnlineOnlyFiles = false }, cancellationToken);
                plan.ScanIssues.AddRange(scan.Issues);
                string manifestFull = options.ManifestPath != null ? Path.GetFullPath(options.ManifestPath) : null;
                foreach (var file in scan.Files)
                {
                    if (listed.Contains(file.FullPath)) continue;
                    if (manifestFull != null && string.Equals(file.FullPath, manifestFull, StringComparison.OrdinalIgnoreCase)) continue;
                    plan.Items.Add(new HashItem(file.FullPath, ItemSource.Manifest)
                    {
                        RelativePath = file.RelativePath,
                        Size = file.Length,
                        Status = ItemStatus.Unexpected,
                        Message = HashItem.StatusDescription(ItemStatus.Unexpected),
                    });
                }
            }
            return plan;
        }

        private static string SafeDisplayName(string relativePath)
        {
            var chars = relativePath.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c < 0x20 || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|') chars[i] = '_';
            }
            string s = new string(chars).Replace('/', '_').Replace('\\', '_');
            return s.Length == 0 ? "_" : s;
        }
    }
}
