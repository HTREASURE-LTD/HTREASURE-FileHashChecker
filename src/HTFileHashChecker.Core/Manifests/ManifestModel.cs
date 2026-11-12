using System.Collections.Generic;
using HTFileHashChecker.Core.Hashing;

namespace HTFileHashChecker.Core.Manifests
{
    public enum ManifestLineFormat
    {
        /// <summary>GNU coreutils: "&lt;hash&gt;  &lt;name&gt;" (text) or "&lt;hash&gt; *&lt;name&gt;" (binary).</summary>
        Gnu,
        /// <summary>BSD / "--tag": "SHA256 (&lt;name&gt;) = &lt;hash&gt;".</summary>
        Bsd,
    }

    public enum ManifestIssueKind
    {
        InvalidFormat,
        InvalidHash,
        UnsafePath,
        Duplicate,
        ConflictingDuplicate,
        UnsupportedAlgorithm,
        AlgorithmMismatch,
        Encoding,
        TooLarge,
    }

    public sealed class ManifestEntry
    {
        public int LineNumber { get; internal set; }

        /// <summary>The path exactly as written in the manifest (after unescaping).</summary>
        public string RelativePath { get; internal set; }

        public HashAlgorithmKind Algorithm { get; internal set; }
        public string HashHex { get; internal set; }
        public byte[] HashBytes { get; internal set; }
        public bool BinaryMode { get; internal set; }
        public ManifestLineFormat Format { get; internal set; }

        /// <summary>Set when this entry must not be verified (unsafe path, conflicting duplicate...).</summary>
        public string Problem { get; internal set; }

        public bool IsUsable { get { return Problem == null; } }
    }

    public sealed class ManifestIssue
    {
        public ManifestIssue(int lineNumber, ManifestIssueKind kind, string message, bool isError)
        {
            LineNumber = lineNumber;
            Kind = kind;
            Message = message;
            IsError = isError;
        }

        /// <summary>1-based line number (0 for whole-file issues).</summary>
        public int LineNumber { get; private set; }
        public ManifestIssueKind Kind { get; private set; }
        public string Message { get; private set; }
        public bool IsError { get; private set; }

        public override string ToString()
        {
            return (LineNumber > 0 ? "Line " + LineNumber + ": " : string.Empty) + Message;
        }
    }

    public sealed class ManifestDocument
    {
        public ManifestDocument()
        {
            Entries = new List<ManifestEntry>();
            Issues = new List<ManifestIssue>();
        }

        public List<ManifestEntry> Entries { get; private set; }
        public List<ManifestIssue> Issues { get; private set; }
        public string EncodingName { get; internal set; }
    }

    public sealed class ManifestParseOptions
    {
        /// <summary>Forces one algorithm. When null the algorithm comes from the BSD tag, the manifest file name
        /// (SHA256SUMS, *.sha512, MD5SUMS...) or the hash length, in that order.</summary>
        public HashAlgorithmKind? Algorithm { get; set; }

        /// <summary>Manifest file name, used to infer the algorithm.</summary>
        public string FileNameHint { get; set; }
    }
}
