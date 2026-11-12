using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;

namespace HTFileHashChecker.Core.Manifests
{
    /// <summary>
    /// Reads SHA256SUMS / SHA512SUMS / MD5SUMS style checksum manifests (GNU coreutils text format, including the
    /// backslash-escaped form) and BSD "--tag" lines. All content is treated as untrusted input.
    /// </summary>
    public static class ManifestParser
    {
        public const long MaxManifestBytes = 256L * 1024 * 1024;
        public const int MaxLineLength = 32 * 1024;
        public const int MaxEntries = 2000000;

        public static ManifestDocument ParseFile(string path, ManifestParseOptions options)
        {
            if (options == null) options = new ManifestParseOptions();
            if (options.FileNameHint == null) options.FileNameHint = Path.GetFileName(path);
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("Manifest not found.", path);
            if (info.Length > MaxManifestBytes)
            {
                var doc = new ManifestDocument();
                doc.Issues.Add(new ManifestIssue(0, ManifestIssueKind.TooLarge,
                    "The manifest is larger than " + (MaxManifestBytes / (1024 * 1024)) + " MB and was not loaded.", true));
                return doc;
            }
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bytes = new byte[stream.Length];
                int offset = 0, read;
                while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
            }
            return Parse(bytes, options);
        }

        public static ManifestDocument Parse(byte[] content, ManifestParseOptions options)
        {
            string encodingName;
            ManifestIssue encodingIssue;
            string text = Decode(content, out encodingName, out encodingIssue);
            var doc = Parse(text, options);
            doc.EncodingName = encodingName;
            if (encodingIssue != null) doc.Issues.Insert(0, encodingIssue);
            return doc;
        }

        /// <summary>UTF-8 (with or without BOM) is the standard; UTF-16 with BOM is accepted because Windows
        /// PowerShell 5 writes it by default. Invalid UTF-8 falls back to the Windows ANSI code page with a warning.</summary>
        private static string Decode(byte[] content, out string encodingName, out ManifestIssue issue)
        {
            issue = null;
            if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            {
                encodingName = "UTF-8 (BOM)";
                return StrictUtf8(content, 3, out issue);
            }
            if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
            {
                encodingName = "UTF-16 LE";
                return new UnicodeEncoding(false, false, true).GetString(content, 2, content.Length - 2);
            }
            if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
            {
                encodingName = "UTF-16 BE";
                return new UnicodeEncoding(true, false, true).GetString(content, 2, content.Length - 2);
            }
            encodingName = "UTF-8";
            return StrictUtf8(content, 0, out issue);
        }

        private static string StrictUtf8(byte[] content, int offset, out ManifestIssue issue)
        {
            issue = null;
            try
            {
                return new UTF8Encoding(false, true).GetString(content, offset, content.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                Encoding ansi = Encoding.Default;
                issue = new ManifestIssue(0, ManifestIssueKind.Encoding,
                    "The manifest is not valid UTF-8; it was read using the Windows code page " + ansi.WebName
                    + ". File names with non-English characters may not match.", false);
                return ansi.GetString(content, offset, content.Length - offset);
            }
        }

        public static ManifestDocument Parse(string text, ManifestParseOptions options)
        {
            if (options == null) options = new ManifestParseOptions();
            var doc = new ManifestDocument();
            HashAlgorithmKind? fileAlgorithm = options.Algorithm;
            if (!fileAlgorithm.HasValue && options.FileNameHint != null)
            {
                HashAlgorithmKind fromName;
                if (HashAlgorithms.TryDetectFromFileName(options.FileNameHint, out fromName)) fileAlgorithm = fromName;
            }

            var seen = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            int lineNumber = 0;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (lineNumber == 1 && line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1);
                    if (line.Trim().Length == 0) continue;
                    if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue; // comment
                    if (line.Length > MaxLineLength)
                    {
                        doc.Issues.Add(new ManifestIssue(lineNumber, ManifestIssueKind.InvalidFormat, "Line is too long and was ignored.", true));
                        continue;
                    }
                    if (doc.Entries.Count >= MaxEntries)
                    {
                        doc.Issues.Add(new ManifestIssue(lineNumber, ManifestIssueKind.TooLarge, "Too many entries; the rest of the manifest was ignored.", true));
                        break;
                    }

                    ManifestEntry entry;
                    string error;
                    ManifestIssueKind kind;
                    if (!TryParseLine(line, fileAlgorithm, out entry, out error, out kind))
                    {
                        doc.Issues.Add(new ManifestIssue(lineNumber, kind, error, true));
                        continue;
                    }
                    entry.LineNumber = lineNumber;

                    string pathError;
                    if (!IsSafeRelativePath(entry.RelativePath, out pathError))
                    {
                        entry.Problem = pathError;
                        doc.Issues.Add(new ManifestIssue(lineNumber, ManifestIssueKind.UnsafePath, pathError + " (" + Shorten(entry.RelativePath) + ")", true));
                        doc.Entries.Add(entry);
                        continue;
                    }

                    string key = PathValidator.ManifestKey(entry.RelativePath);
                    ManifestEntry first;
                    if (seen.TryGetValue(key, out first))
                    {
                        if (first.Algorithm == entry.Algorithm && Hex.FixedTimeEquals(first.HashBytes, entry.HashBytes))
                        {
                            doc.Issues.Add(new ManifestIssue(lineNumber, ManifestIssueKind.Duplicate,
                                "Duplicate entry for '" + Shorten(entry.RelativePath) + "' (same as line " + first.LineNumber + "); ignored.", false));
                        }
                        else
                        {
                            string problem = "Conflicting entries for this file on lines " + first.LineNumber + " and " + lineNumber
                                + " (file names differing only in letter case refer to the same file on Windows).";
                            if (first.Problem == null) first.Problem = problem;
                            doc.Issues.Add(new ManifestIssue(lineNumber, ManifestIssueKind.ConflictingDuplicate, problem, true));
                        }
                        continue;
                    }
                    seen.Add(key, entry);
                    doc.Entries.Add(entry);
                }
            }
            return doc;
        }

        private static bool IsSafeRelativePath(string relativePath, out string error)
        {
            // Validate against a neutral base folder; the real folder is checked again at verification time.
            string ignored;
            return PathValidator.TryResolveManifestPath(@"C:\HTFHC-validate", relativePath, out ignored, out error);
        }

        internal static bool TryParseLine(string line, HashAlgorithmKind? fileAlgorithm, out ManifestEntry entry, out string error,
            out ManifestIssueKind kind)
        {
            entry = null;
            error = null;
            kind = ManifestIssueKind.InvalidFormat;
            bool escaped = false;
            string body = line;
            if (body.StartsWith("\\", StringComparison.Ordinal))
            {
                escaped = true;
                body = body.Substring(1);
            }

            // BSD / --tag format: ALG (name) = hash
            int open = body.IndexOf(" (", StringComparison.Ordinal);
            int close = body.LastIndexOf(") = ", StringComparison.Ordinal);
            if (open > 0 && close > open)
            {
                string tag = body.Substring(0, open);
                HashAlgorithmKind tagged;
                if (IsAlgorithmTag(tag))
                {
                    if (!HashAlgorithms.TryParse(tag, out tagged))
                    {
                        error = "Unsupported algorithm '" + Shorten(tag) + "'.";
                        kind = ManifestIssueKind.UnsupportedAlgorithm;
                        return false;
                    }
                    string name = body.Substring(open + 2, close - open - 2);
                    string hashText = body.Substring(close + 4).TrimEnd();
                    if (fileAlgorithm.HasValue && fileAlgorithm.Value != tagged)
                    {
                        error = "The line uses " + HashAlgorithms.Get(tagged).DisplayName + " but the manifest is " + HashAlgorithms.Get(fileAlgorithm.Value).DisplayName + ".";
                        kind = ManifestIssueKind.AlgorithmMismatch;
                        return false;
                    }
                    return Build(hashText, tagged, name, escaped, false, ManifestLineFormat.Bsd, out entry, out error, out kind);
                }
            }

            // GNU format: hash, one space, then ' ' (text) or '*' (binary) and the name.
            int space = body.IndexOf(' ');
            if (space <= 0)
            {
                error = "Not a checksum line (expected '<hash>  <file name>').";
                return false;
            }
            string hash = body.Substring(0, space);
            string rest = body.Substring(space + 1);
            bool binary = false;
            if (rest.StartsWith("*", StringComparison.Ordinal)) { binary = true; rest = rest.Substring(1); }
            else if (rest.StartsWith(" ", StringComparison.Ordinal)) { rest = rest.Substring(1); }
            // else: single-space variant written by some tools; the name starts immediately.

            HashAlgorithmKind algorithm;
            if (fileAlgorithm.HasValue) algorithm = fileAlgorithm.Value;
            else if (!HashAlgorithms.TryDetectFromHexLength(hash.Length, out algorithm))
            {
                bool hex = AllHex(hash);
                error = hex
                    ? "The hash has " + hash.Length + " characters, which matches no supported algorithm."
                    : "Not a checksum line (the first word is not a hexadecimal hash).";
                kind = hex ? ManifestIssueKind.InvalidHash : ManifestIssueKind.InvalidFormat;
                return false;
            }
            return Build(hash, algorithm, rest, escaped, binary, ManifestLineFormat.Gnu, out entry, out error, out kind);
        }

        private static bool Build(string hashText, HashAlgorithmKind algorithm, string name, bool escaped, bool binary,
            ManifestLineFormat format, out ManifestEntry entry, out string error, out ManifestIssueKind kind)
        {
            entry = null;
            error = null;
            kind = ManifestIssueKind.InvalidHash;
            var info = HashAlgorithms.Get(algorithm);
            if (!AllHex(hashText))
            {
                error = "The hash value is not hexadecimal.";
                return false;
            }
            if (hashText.Length != info.HexLength)
            {
                error = "The hash has " + hashText.Length + " characters; a " + info.DisplayName + " hash has " + info.HexLength + ".";
                return false;
            }
            byte[] bytes;
            Hex.TryParse(hashText, out bytes);
            kind = ManifestIssueKind.InvalidFormat;

            if (escaped)
            {
                string decoded;
                if (!TryUnescape(name, out decoded))
                {
                    error = "Invalid escape sequence in the file name.";
                    return false;
                }
                name = decoded;
            }
            if (name.Length == 0)
            {
                error = "The file name is missing.";
                return false;
            }

            entry = new ManifestEntry
            {
                Algorithm = algorithm,
                HashHex = hashText.ToLowerInvariant(),
                HashBytes = bytes,
                RelativePath = name,
                BinaryMode = binary,
                Format = format,
            };
            return true;
        }

        /// <summary>GNU escaping: "\\" is a backslash, "\n" a line feed, "\r" a carriage return.</summary>
        internal static bool TryUnescape(string value, out string result)
        {
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\') { sb.Append(c); continue; }
                if (i + 1 >= value.Length) { result = null; return false; }
                char n = value[++i];
                if (n == '\\') sb.Append('\\');
                else if (n == 'n') sb.Append('\n');
                else if (n == 'r') sb.Append('\r');
                else { result = null; return false; }
            }
            result = sb.ToString();
            return true;
        }

        private static bool IsAlgorithmTag(string tag)
        {
            if (tag.Length == 0 || tag.Length > 16) return false;
            foreach (char c in tag)
                if (!(char.IsLetterOrDigit(c) || c == '-')) return false;
            return char.IsLetter(tag[0]);
        }

        private static bool AllHex(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s)
                if (!Hex.IsHexDigit(c)) return false;
            return true;
        }

        internal static string Shorten(string s)
        {
            if (s == null) return string.Empty;
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (sb.Length >= 120) { sb.Append("..."); break; }
                sb.Append(char.IsControl(c) ? '?' : c);
            }
            return sb.ToString();
        }
    }
}
