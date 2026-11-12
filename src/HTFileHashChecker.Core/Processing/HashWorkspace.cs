using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Manifests;
using HTFileHashChecker.Core.Reports;
using HTFileHashChecker.Core.Verification;

namespace HTFileHashChecker.Core.Processing
{
    public sealed class AddOptions
    {
        public bool Recursive { get; set; }
        public bool SkipOnlineOnlyFiles { get; set; }
        public ItemSource Source { get; set; }
    }

    public sealed class AddResult
    {
        public AddResult()
        {
            Added = new List<HashItem>();
            Issues = new List<ScanIssue>();
        }

        public List<HashItem> Added { get; private set; }
        public int Duplicates { get; internal set; }
        public List<ScanIssue> Issues { get; private set; }
        public bool LimitReached { get; internal set; }
    }

    public sealed class ManifestLoadResult
    {
        public ManifestDocument Document { get; internal set; }
        public ManifestVerificationPlan Plan { get; internal set; }
        public string ManifestPath { get; internal set; }
        public string BaseDirectory { get; internal set; }
    }

    public sealed class ManifestExportPlan
    {
        public ManifestExportPlan()
        {
            Entries = new List<ManifestWriteEntry>();
            OutsideFolder = new List<HashItem>();
            WithoutHash = new List<HashItem>();
        }

        public List<ManifestWriteEntry> Entries { get; private set; }
        public List<HashItem> OutsideFolder { get; private set; }
        public List<HashItem> WithoutHash { get; private set; }
        public bool MixedAlgorithms { get; internal set; }
    }

    /// <summary>
    /// Application service behind the main window: owns the file list and runs calculation, verification, manifest
    /// import/export and report generation. Contains no user-interface code. Methods that change the list must be
    /// called from one thread (the UI thread); long-running work runs on background threads.
    /// </summary>
    public sealed class HashWorkspace
    {
        public const int MaxItems = 1000000;

        private readonly List<HashItem> items = new List<HashItem>();
        private readonly HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly BatchProcessor processor;

        public HashWorkspace(BatchProcessor processor)
        {
            if (processor == null) throw new ArgumentNullException("processor");
            this.processor = processor;
        }

        public IReadOnlyList<HashItem> Items { get { return items; } }

        /// <summary>Set while the list holds the result of a manifest import.</summary>
        public string ManifestPath { get; private set; }
        public string ManifestBaseDirectory { get; private set; }

        public BatchSummary LastSummary { get; private set; }

        public async Task<AddResult> AddPathsAsync(IEnumerable<string> inputPaths, AddOptions options, CancellationToken cancellationToken)
        {
            if (options == null) options = new AddOptions { Recursive = true };
            var list = inputPaths.ToList();
            AddResult prepared = await Task.Run(() => Prepare(list, options, cancellationToken), cancellationToken);
            // Back on the caller's thread: commit to the list.
            var result = new AddResult { Duplicates = prepared.Duplicates, LimitReached = prepared.LimitReached };
            result.Issues.AddRange(prepared.Issues);
            if (ManifestPath != null && prepared.Added.Count > 0) { ManifestPath = null; ManifestBaseDirectory = null; }
            foreach (var item in prepared.Added)
            {
                if (items.Count >= MaxItems) { result.LimitReached = true; break; }
                if (!paths.Add(item.FullPath)) { result.Duplicates++; continue; }
                items.Add(item);
                result.Added.Add(item);
            }
            return result;
        }

        private AddResult Prepare(List<string> inputPaths, AddOptions options, CancellationToken token)
        {
            var result = new AddResult();
            var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in inputPaths)
            {
                token.ThrowIfCancellationRequested();
                string error;
                if (!PathValidator.TryValidateLocalPath(raw, out error))
                {
                    result.Issues.Add(new ScanIssue(raw, error, true));
                    continue;
                }
                string full;
                try { full = Path.GetFullPath(raw); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    result.Issues.Add(new ScanIssue(raw, ErrorDescriber.Describe(ex), true));
                    continue;
                }

                if (Directory.Exists(full))
                {
                    ScanResult scan;
                    try
                    {
                        scan = FileScanner.Scan(full, new ScanOptions { Recursive = options.Recursive, SkipOnlineOnlyFiles = options.SkipOnlineOnlyFiles }, token);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        result.Issues.Add(new ScanIssue(full, ErrorDescriber.Describe(ex), true));
                        continue;
                    }
                    result.Issues.AddRange(scan.Issues);
                    if (scan.LimitReached) result.LimitReached = true;
                    foreach (var f in scan.Files)
                    {
                        if (!local.Add(f.FullPath) || paths.Contains(f.FullPath)) { result.Duplicates++; continue; }
                        result.Added.Add(new HashItem(f.FullPath, options.Source == ItemSource.Manual ? ItemSource.Folder : options.Source)
                        {
                            RelativePath = f.RelativePath,
                            Size = f.Length,
                        });
                    }
                }
                else if (File.Exists(full))
                {
                    if (!local.Add(full) || paths.Contains(full)) { result.Duplicates++; continue; }
                    var item = new HashItem(full, options.Source);
                    try { item.Size = new FileInfo(full).Length; }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                    result.Added.Add(item);
                }
                else
                {
                    result.Issues.Add(new ScanIssue(full, "Not found.", true));
                }
            }
            return result;
        }

        public void Remove(IEnumerable<HashItem> toRemove)
        {
            var set = new HashSet<HashItem>(toRemove);
            items.RemoveAll(i => set.Contains(i));
            foreach (var i in set) paths.Remove(i.FullPath);
        }

        public void Clear()
        {
            items.Clear();
            paths.Clear();
            ManifestPath = null;
            ManifestBaseDirectory = null;
            LastSummary = null;
        }

        /// <summary>
        /// Chooses the items "Calculate" should process and sets their algorithm. Items with an expected hash keep the
        /// expected hash's algorithm. Unless <paramref name="all"/> is set, items already calculated with the right
        /// algorithm are not read again.
        /// </summary>
        public List<HashItem> PrepareCalculation(HashAlgorithmKind algorithm, IEnumerable<HashItem> scope, bool all)
        {
            var selected = new List<HashItem>();
            foreach (var item in scope ?? items)
            {
                if (item.IsPlanFinal) continue;
                HashAlgorithmKind target = item.Expected != null ? item.Expected.Algorithm : algorithm;
                bool done = item.HashHex != null && item.Algorithm == target
                    && (item.Status == ItemStatus.Hashed || item.Status == ItemStatus.Match || item.Status == ItemStatus.Mismatch);
                if (done && !all) continue;
                item.Algorithm = target;
                selected.Add(item);
            }
            return selected;
        }

        /// <summary>Items to re-read for verification: every item with an expected hash that is not decided by the plan.</summary>
        public List<HashItem> PrepareVerification(IEnumerable<HashItem> scope)
        {
            var selected = new List<HashItem>();
            foreach (var item in scope ?? items)
            {
                if (item.IsPlanFinal || item.Expected == null) continue;
                item.Algorithm = item.Expected.Algorithm;
                selected.Add(item);
            }
            return selected;
        }

        public async Task<BatchSummary> RunAsync(IReadOnlyList<HashItem> toProcess, BatchOptions options, IProgress<BatchProgress> progress,
            CancellationToken cancellationToken)
        {
            BatchSummary summary = await processor.RunAsync(toProcess, options, progress, cancellationToken);
            LastSummary = summary;
            return summary;
        }

        /// <summary>Sets the expected hash on the given items. The algorithm is detected from the hash length, so a
        /// pasted SHA-512 value is verified with SHA-512 even if SHA-256 is selected.</summary>
        public ExpectedHashParseResult SetExpected(IEnumerable<HashItem> targets, string input)
        {
            var parsed = ExpectedHash.Parse(input, null);
            if (!parsed.IsValid) return parsed;
            foreach (var item in targets)
            {
                if (item.IsPlanFinal) continue;
                item.Expected = parsed.Value;
                if (item.Algorithm != parsed.Value.Algorithm)
                {
                    item.Algorithm = parsed.Value.Algorithm;
                    item.ResetResult();
                }
            }
            return parsed;
        }

        public void ClearExpected(IEnumerable<HashItem> targets)
        {
            foreach (var item in targets)
            {
                if (item.Source == ItemSource.Manifest) continue;
                item.Expected = null;
                if (item.Status == ItemStatus.Match || item.Status == ItemStatus.Mismatch) item.Status = ItemStatus.Hashed;
            }
        }

        /// <summary>Parses a manifest and replaces the list with its verification plan (nothing is read yet).</summary>
        public async Task<ManifestLoadResult> LoadManifestAsync(string manifestPath, string baseDirectory, HashAlgorithmKind? algorithm,
            bool reportUnexpected, CancellationToken cancellationToken)
        {
            string full = Path.GetFullPath(manifestPath);
            string baseDir = Path.GetFullPath(string.IsNullOrEmpty(baseDirectory) ? Path.GetDirectoryName(full) : baseDirectory);
            var loaded = await Task.Run(() =>
            {
                var doc = ManifestParser.ParseFile(full, new ManifestParseOptions { Algorithm = algorithm, FileNameHint = Path.GetFileName(full) });
                var plan = ManifestVerifier.CreatePlan(doc, new ManifestVerificationOptions
                {
                    BaseDirectory = baseDir,
                    ReportUnexpectedFiles = reportUnexpected,
                    ManifestPath = full,
                }, cancellationToken);
                return new ManifestLoadResult { Document = doc, Plan = plan, ManifestPath = full, BaseDirectory = baseDir };
            }, cancellationToken);

            Clear();
            foreach (var item in loaded.Plan.Items)
            {
                if (items.Count >= MaxItems) break;
                // Invalid entries may share a display path; keep them all, but only real paths enter the duplicate set.
                if (item.Status != ItemStatus.Invalid) paths.Add(item.FullPath);
                items.Add(item);
            }
            ManifestPath = full;
            ManifestBaseDirectory = baseDir;
            return loaded;
        }

        /// <summary>Builds manifest entries relative to the folder the manifest will be saved in.</summary>
        public ManifestExportPlan PrepareManifestExport(string manifestPath, IEnumerable<HashItem> scope)
        {
            var plan = new ManifestExportPlan();
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            var algorithms = new HashSet<HashAlgorithmKind>();
            foreach (var item in scope ?? items)
            {
                if (item.HashHex == null || item.Status == ItemStatus.Error || item.IsPlanFinal)
                {
                    plan.WithoutHash.Add(item);
                    continue;
                }
                string rel = PathValidator.MakeRelative(baseDir, item.FullPath);
                if (rel == null)
                {
                    plan.OutsideFolder.Add(item);
                    continue;
                }
                algorithms.Add(item.Algorithm);
                plan.Entries.Add(new ManifestWriteEntry(rel, item.Algorithm, item.HashHex));
            }
            plan.MixedAlgorithms = algorithms.Count > 1;
            return plan;
        }

        public ReportData BuildReport(HashAlgorithmKind selectedAlgorithm, IEnumerable<HashItem> scope)
        {
            var list = (scope ?? items).ToList();
            TimeSpan total = LastSummary != null ? LastSummary.Elapsed : TimeSpan.Zero;
            if (total == TimeSpan.Zero)
                total = TimeSpan.FromTicks(list.Where(i => i.Elapsed.HasValue).Sum(i => i.Elapsed.Value.Ticks));
            return new ReportData
            {
                SelectedAlgorithm = selectedAlgorithm,
                Items = list,
                TotalProcessingTime = total,
                ManifestPath = ManifestPath,
                BaseDirectory = ManifestBaseDirectory,
            };
        }
    }
}
