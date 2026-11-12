using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HTFileHashChecker.Core.IO
{
    public sealed class ScanOptions
    {
        public ScanOptions()
        {
            Recursive = true;
            MaxDepth = 128;
            MaxFiles = 1000000;
            SkipOnlineOnlyFiles = true;
        }

        /// <summary>Include files in subfolders.</summary>
        public bool Recursive { get; set; }

        /// <summary>Folder nesting limit (protects against pathological or looping structures).</summary>
        public int MaxDepth { get; set; }

        /// <summary>Stop after this many files (protects memory when a very large tree is selected by mistake).</summary>
        public int MaxFiles { get; set; }

        /// <summary>Skip cloud "online-only" placeholder files, which Windows would download if they were read.</summary>
        public bool SkipOnlineOnlyFiles { get; set; }
    }

    public sealed class ScannedFile
    {
        public ScannedFile(string fullPath, string relativePath, long length)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            Length = length;
        }

        public string FullPath { get; private set; }

        /// <summary>Path relative to the scanned root, with '/' separators.</summary>
        public string RelativePath { get; private set; }

        public long Length { get; private set; }
    }

    public sealed class ScanIssue
    {
        public ScanIssue(string path, string reason, bool isError)
        {
            Path = path;
            Reason = reason;
            IsError = isError;
        }

        public string Path { get; private set; }
        public string Reason { get; private set; }
        public bool IsError { get; private set; }
    }

    public sealed class ScanResult
    {
        public ScanResult()
        {
            Files = new List<ScannedFile>();
            Issues = new List<ScanIssue>();
        }

        public List<ScannedFile> Files { get; private set; }
        public List<ScanIssue> Issues { get; private set; }
        public bool LimitReached { get; internal set; }
    }

    /// <summary>
    /// Enumerates the files of a folder without recursion on the call stack. Linked folders (symbolic links and
    /// junctions) are never followed, which prevents loops and escaping the selected folder. Inaccessible folders
    /// are reported and skipped. Results are sorted by relative path for deterministic output.
    /// </summary>
    public static class FileScanner
    {
        private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
        private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

        public static ScanResult Scan(string rootDirectory, ScanOptions options, CancellationToken cancellationToken)
        {
            if (options == null) options = new ScanOptions();
            PathValidator.ThrowIfInvalidLocalFilePath(rootDirectory);
            var root = new DirectoryInfo(Path.GetFullPath(rootDirectory));
            if (!root.Exists) throw new DirectoryNotFoundException("Folder not found: " + root.FullName);

            string rootPrefix = root.FullName.TrimEnd('\\') + "\\";
            var result = new ScanResult();
            var pending = new Stack<KeyValuePair<DirectoryInfo, int>>();
            pending.Push(new KeyValuePair<DirectoryInfo, int>(root, 0));

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                DirectoryInfo dir = current.Key;
                int depth = current.Value;

                IEnumerable<FileSystemInfo> entries;
                List<FileSystemInfo> list;
                try
                {
                    entries = dir.EnumerateFileSystemInfos();
                    list = new List<FileSystemInfo>(entries);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
                {
                    result.Issues.Add(new ScanIssue(dir.FullName, "Folder could not be read: " + ErrorDescriber.Describe(ex), true));
                    continue;
                }

                foreach (FileSystemInfo entry in list)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes;
                    try { attributes = entry.Attributes; }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        result.Issues.Add(new ScanIssue(entry.FullName, ErrorDescriber.Describe(ex), true));
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!options.Recursive) continue;
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            result.Issues.Add(new ScanIssue(entry.FullName, "Skipped linked folder (symbolic link or junction).", false));
                            continue;
                        }
                        if (depth + 1 > options.MaxDepth)
                        {
                            result.Issues.Add(new ScanIssue(entry.FullName, "Skipped: folder nesting deeper than " + options.MaxDepth + " levels.", false));
                            continue;
                        }
                        pending.Push(new KeyValuePair<DirectoryInfo, int>((DirectoryInfo)entry, depth + 1));
                        continue;
                    }

                    if (options.SkipOnlineOnlyFiles && (attributes & (FileAttributes.Offline | RecallOnDataAccess | RecallOnOpen)) != 0)
                    {
                        result.Issues.Add(new ScanIssue(entry.FullName, "Skipped online-only (cloud) file; reading it would download it.", false));
                        continue;
                    }

                    if (result.Files.Count >= options.MaxFiles)
                    {
                        result.LimitReached = true;
                        result.Issues.Add(new ScanIssue(root.FullName, "Stopped after " + options.MaxFiles + " files (limit reached).", false));
                        pending.Clear();
                        break;
                    }

                    long length = 0;
                    try { length = ((FileInfo)entry).Length; }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }

                    string full = entry.FullName;
                    string relative = full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                        ? full.Substring(rootPrefix.Length).Replace('\\', '/')
                        : entry.Name;
                    result.Files.Add(new ScannedFile(full, relative, length));
                }
            }

            result.Files.Sort((a, b) => ComparePaths(a.RelativePath, b.RelativePath));
            return result;
        }

        /// <summary>Deterministic, culture-independent ordering of relative paths.</summary>
        public static int ComparePaths(string a, string b)
        {
            int c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }
    }
}
