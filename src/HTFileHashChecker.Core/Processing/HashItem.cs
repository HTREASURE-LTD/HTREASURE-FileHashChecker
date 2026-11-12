using System;
using System.Threading;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.Verification;

namespace HTFileHashChecker.Core.Processing
{
    public enum ItemStatus
    {
        /// <summary>Not processed yet.</summary>
        Pending,
        Hashing,
        /// <summary>Hash calculated; no expected value to compare with.</summary>
        Hashed,
        /// <summary>MATCH: calculated hash equals the expected hash.</summary>
        Match,
        /// <summary>MISMATCH: calculated hash differs from the expected hash.</summary>
        Mismatch,
        /// <summary>ERROR: the file could not be read or verified.</summary>
        Error,
        /// <summary>Listed in a manifest but not found on disk.</summary>
        Missing,
        /// <summary>Found in the folder but not listed in the manifest (complete folder comparison).</summary>
        Unexpected,
        /// <summary>Manifest entry rejected (unsafe path, invalid checksum, conflicting duplicate).</summary>
        Invalid,
        Skipped,
        Cancelled,
    }

    public enum ItemSource
    {
        Manual,
        Folder,
        Manifest,
        CommandLine,
    }

    /// <summary>One row of the file list. Updated by the batch processor on a background thread and read by the UI.</summary>
    public sealed class HashItem
    {
        private static int nextId;
        private int status;

        public HashItem(string fullPath, ItemSource source)
        {
            if (fullPath == null) throw new ArgumentNullException("fullPath");
            Id = Interlocked.Increment(ref nextId);
            FullPath = fullPath;
            Source = source;
            Algorithm = HashAlgorithms.Default;
            Status = ItemStatus.Pending;
        }

        public int Id { get; private set; }
        public string FullPath { get; private set; }
        public string FileName { get { return System.IO.Path.GetFileName(FullPath); } }
        public ItemSource Source { get; private set; }

        /// <summary>Path relative to the folder or manifest it came from ('/' separators), when known.</summary>
        public string RelativePath { get; set; }

        /// <summary>Size in bytes when known.</summary>
        public long? Size { get; set; }

        /// <summary>Algorithm of <see cref="HashHex"/> (or of the next calculation).</summary>
        public HashAlgorithmKind Algorithm { get; set; }

        public string HashHex { get; set; }
        public byte[] HashBytes { get; set; }
        public ExpectedHash Expected { get; set; }

        public ItemStatus Status
        {
            get { return (ItemStatus)Volatile.Read(ref status); }
            set { Volatile.Write(ref status, (int)value); }
        }

        public string Message { get; set; }
        public TimeSpan? Elapsed { get; set; }
        public int? ManifestLine { get; set; }

        /// <summary>When set, the file's resolved path must stay inside this folder (manifest verification).</summary>
        public string ContainmentRoot { get; set; }

        /// <summary>True when the status was decided before hashing (missing, unexpected, invalid) and hashing must not change it.</summary>
        public bool IsPlanFinal
        {
            get
            {
                var s = Status;
                return s == ItemStatus.Missing || s == ItemStatus.Unexpected || s == ItemStatus.Invalid;
            }
        }

        public bool IsVerificationFailure
        {
            get
            {
                var s = Status;
                return s == ItemStatus.Mismatch || s == ItemStatus.Missing || s == ItemStatus.Invalid
                    || s == ItemStatus.Unexpected || (s == ItemStatus.Error && Expected != null);
            }
        }

        public void ResetResult()
        {
            HashHex = null;
            HashBytes = null;
            Message = null;
            Elapsed = null;
            Status = ItemStatus.Pending;
        }

        /// <summary>Upper-case label used in the list and reports.</summary>
        public static string StatusLabel(ItemStatus status)
        {
            switch (status)
            {
                case ItemStatus.Pending: return "Pending";
                case ItemStatus.Hashing: return "Hashing...";
                case ItemStatus.Hashed: return "Calculated";
                case ItemStatus.Match: return "MATCH";
                case ItemStatus.Mismatch: return "MISMATCH";
                case ItemStatus.Error: return "ERROR";
                case ItemStatus.Missing: return "MISSING";
                case ItemStatus.Unexpected: return "NOT IN MANIFEST";
                case ItemStatus.Invalid: return "INVALID ENTRY";
                case ItemStatus.Skipped: return "Skipped";
                case ItemStatus.Cancelled: return "Cancelled";
                default: return status.ToString();
            }
        }

        public static string StatusDescription(ItemStatus status)
        {
            switch (status)
            {
                case ItemStatus.Match: return "File checksum matches.";
                case ItemStatus.Mismatch: return "File checksum does not match.";
                case ItemStatus.Error: return "File could not be verified.";
                case ItemStatus.Missing: return "File listed in the manifest was not found.";
                case ItemStatus.Unexpected: return "File is in the folder but not listed in the manifest.";
                case ItemStatus.Invalid: return "Manifest entry was rejected.";
                default: return StatusLabel(status);
            }
        }
    }
}
