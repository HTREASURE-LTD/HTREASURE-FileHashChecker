using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;

namespace HTFileHashChecker.Core.Manifests
{
    public sealed class ManifestWriteEntry
    {
        public ManifestWriteEntry(string relativePath, HashAlgorithmKind algorithm, string hashHex)
        {
            RelativePath = relativePath;
            Algorithm = algorithm;
            HashHex = hashHex;
        }

        /// <summary>Relative path ('/' or '\' separators; written with '/').</summary>
        public string RelativePath { get; private set; }
        public HashAlgorithmKind Algorithm { get; private set; }
        public string HashHex { get; private set; }
    }

    public sealed class ManifestWriteOptions
    {
        public ManifestWriteOptions()
        {
            Format = ManifestLineFormat.Gnu;
        }

        public ManifestLineFormat Format { get; set; }

        /// <summary>Write "hash *name" (binary mode marker) instead of "hash  name".</summary>
        public bool BinaryMarker { get; set; }

        /// <summary>Replace an existing file. When false an existing file is never overwritten.</summary>
        public bool Overwrite { get; set; }
    }

    /// <summary>Writes GNU-compatible checksum manifests (UTF-8 without BOM, LF line endings, sorted entries).</summary>
    public static class ManifestWriter
    {
        /// <summary>Formats entries in deterministic order (ordinal comparison of the written relative path).</summary>
        public static string Format(IEnumerable<ManifestWriteEntry> entries, ManifestWriteOptions options)
        {
            if (options == null) options = new ManifestWriteOptions();
            var list = entries.ToList();
            if (list.Count == 0) throw new ArgumentException("There is nothing to write: no file has a calculated hash.");
            if (options.Format == ManifestLineFormat.Gnu && list.Select(e => e.Algorithm).Distinct().Count() > 1)
                throw new ArgumentException("A GNU-style manifest (SHA256SUMS etc.) holds one algorithm only. Calculate all files with the same algorithm, or use the BSD tag format.");

            var prepared = new List<KeyValuePair<string, ManifestWriteEntry>>();
            foreach (var e in list)
            {
                string rel = e.RelativePath.Replace('\\', '/');
                string error;
                string ignored;
                if (!PathValidator.TryResolveManifestPath(@"C:\HTFHC-validate", rel, out ignored, out error))
                    throw new ArgumentException("Cannot write '" + rel + "': " + error);
                byte[] digest;
                if (!Hex.TryParse(e.HashHex, out digest) || digest.Length != HashAlgorithms.Get(e.Algorithm).DigestBytes)
                    throw new ArgumentException("Invalid hash for '" + rel + "'.");
                prepared.Add(new KeyValuePair<string, ManifestWriteEntry>(rel, e));
            }
            prepared.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in prepared)
                if (!keys.Add(p.Key)) throw new ArgumentException("'" + p.Key + "' appears more than once.");

            var sb = new StringBuilder();
            foreach (var p in prepared)
            {
                string name = p.Key;
                bool needsEscape = name.IndexOf('\\') >= 0 || name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0;
                if (needsEscape)
                {
                    name = name.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");
                    sb.Append('\\');
                }
                string hex = p.Value.HashHex.ToLowerInvariant();
                if (options.Format == ManifestLineFormat.Bsd)
                    sb.Append(HashAlgorithms.Get(p.Value.Algorithm).BsdTag).Append(" (").Append(name).Append(") = ").Append(hex);
                else
                    sb.Append(hex).Append(options.BinaryMarker ? " *" : "  ").Append(name);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Writes the manifest atomically (temporary file, then rename). Throws <see cref="IOException"/>
        /// when the file exists and <see cref="ManifestWriteOptions.Overwrite"/> is false.</summary>
        public static void WriteFile(string path, IEnumerable<ManifestWriteEntry> entries, ManifestWriteOptions options)
        {
            if (options == null) options = new ManifestWriteOptions();
            string content = Format(entries, options);
            AtomicFile.WriteAllText(path, content, new UTF8Encoding(false), options.Overwrite);
        }

        /// <summary>Builds entries for files inside <paramref name="baseDirectory"/>; files outside it are returned in
        /// <paramref name="outside"/> and not included.</summary>
        public static List<ManifestWriteEntry> BuildEntries(string baseDirectory,
            IEnumerable<Tuple<string, HashAlgorithmKind, string>> files, out List<string> outside)
        {
            outside = new List<string>();
            var result = new List<ManifestWriteEntry>();
            foreach (var f in files)
            {
                string rel = PathValidator.MakeRelative(baseDirectory, f.Item1);
                if (rel == null) { outside.Add(f.Item1); continue; }
                result.Add(new ManifestWriteEntry(rel, f.Item2, f.Item3));
            }
            return result;
        }
    }
}
