using System;
using System.IO;

namespace HTFileHashChecker.Core.IO
{
    /// <summary>A path was rejected by validation.</summary>
    [Serializable]
    public sealed class InvalidPathException : IOException
    {
        public InvalidPathException(string message) : base(message) { }

        private InvalidPathException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Validation for paths that come from untrusted sources (command line, drag and drop, manifests).
    /// </summary>
    public static class PathValidator
    {
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00B9", "COM\u00B2", "COM\u00B3",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
        };

        /// <summary>
        /// Accepts only ordinary absolute file-system paths (C:\..., \\server\share\...). Rejects device paths
        /// (\\.\, \\?\GLOBALROOT), alternate data streams, wildcards and control characters.
        /// </summary>
        public static bool TryValidateLocalPath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path)) { error = "The path is empty."; return false; }
            foreach (char c in path)
            {
                if (c < 0x20) { error = "The path contains control characters."; return false; }
                if (c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|') { error = "The path contains wildcard or reserved characters."; return false; }
            }

            string body = path;
            if (body.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) body = @"\\" + body.Substring(8);
            else if (body.StartsWith(@"\\?\", StringComparison.Ordinal)) body = body.Substring(4);
            else if (body.StartsWith(@"\\.\", StringComparison.Ordinal) || body.StartsWith("//./", StringComparison.Ordinal))
            {
                error = "Device paths are not supported.";
                return false;
            }
            if (body.StartsWith("GLOBALROOT", StringComparison.OrdinalIgnoreCase) || body.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                error = "Device paths are not supported.";
                return false;
            }

            bool driveRooted = body.Length >= 3 && char.IsLetter(body[0]) && body[1] == ':' && (body[2] == '\\' || body[2] == '/');
            bool unc = body.StartsWith(@"\\", StringComparison.Ordinal) || body.StartsWith("//", StringComparison.Ordinal);
            if (!driveRooted && !unc) { error = "Only full paths (such as C:\\Folder\\file.iso) are accepted."; return false; }

            int colonSearchStart = driveRooted ? 2 : 0;
            if (body.IndexOf(':', colonSearchStart) >= 0) { error = "Alternate data streams (':' in a path) are not supported."; return false; }
            return true;
        }

        internal static void ThrowIfInvalidLocalFilePath(string path)
        {
            string error;
            if (!TryValidateLocalPath(path, out error)) throw new InvalidPathException(error);
        }

        /// <summary>
        /// Resolves a relative path from a checksum manifest against <paramref name="baseDirectory"/>.
        /// Rejects absolute paths, drive-relative paths, UNC paths, ".." traversal, "." segments, empty segments,
        /// alternate data streams, reserved device names, control characters, wildcards and segments that Windows
        /// would silently alter (trailing dots or spaces), so every accepted entry names exactly one file inside
        /// the base folder.
        /// </summary>
        public static bool TryResolveManifestPath(string baseDirectory, string relativePath, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;
            if (string.IsNullOrEmpty(relativePath)) { error = "The file name is empty."; return false; }

            string p = relativePath.Replace('/', '\\');
            foreach (char c in p)
            {
                if (c < 0x20 || c == 0x7F) { error = "The file name contains control characters (for example a line break)."; return false; }
                if (c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|') { error = "The file name contains wildcard or reserved characters."; return false; }
            }
            if (p.StartsWith("\\", StringComparison.Ordinal)) { error = "Absolute paths are not allowed in a manifest."; return false; }
            if (p.Length >= 2 && p[1] == ':') { error = "Absolute or drive paths are not allowed in a manifest."; return false; }
            if (p.IndexOf(':') >= 0) { error = "':' is not allowed in a manifest path (alternate data streams)."; return false; }

            while (p.StartsWith(".\\", StringComparison.Ordinal)) p = p.Substring(2);
            if (p.Length == 0) { error = "The file name is empty."; return false; }

            string[] segments = p.Split('\\');
            foreach (string segment in segments)
            {
                if (segment.Length == 0) { error = "The path contains an empty folder name (\"//\") and is ambiguous."; return false; }
                if (segment == "..") { error = "Path traversal (\"..\") is not allowed in a manifest."; return false; }
                if (segment == ".") { error = "The path contains \".\" segments and is ambiguous."; return false; }
                char last = segment[segment.Length - 1];
                if (last == '.' || last == ' ') { error = "A file or folder name ends with a dot or space, which Windows cannot represent exactly."; return false; }
                if (IsReservedName(segment)) { error = "'" + segment + "' is a reserved Windows device name."; return false; }
            }

            string baseFull = Path.GetFullPath(baseDirectory).TrimEnd('\\') + "\\";
            string combined;
            try
            {
                combined = Path.GetFullPath(baseFull + p);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                error = "Invalid path: " + ex.Message;
                return false;
            }
            if (!combined.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) || combined.Length == baseFull.Length)
            {
                error = "The path points outside the verification folder.";
                return false;
            }
            fullPath = combined;
            return true;
        }

        /// <summary>Normalised key used to detect duplicate manifest entries (Windows paths are case-insensitive).</summary>
        public static string ManifestKey(string relativePath)
        {
            string p = relativePath.Replace('/', '\\');
            while (p.StartsWith(".\\", StringComparison.Ordinal)) p = p.Substring(2);
            return p.ToUpperInvariant();
        }

        public static bool IsReservedName(string segment)
        {
            string stem = segment;
            int dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem.Substring(0, dot);
            stem = stem.TrimEnd(' ');
            foreach (string reserved in ReservedNames)
                if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Relative path of <paramref name="fullPath"/> inside <paramref name="baseDirectory"/> using '/'
        /// separators, or null when the file is not inside the folder.</summary>
        public static string MakeRelative(string baseDirectory, string fullPath)
        {
            string baseFull = Path.GetFullPath(baseDirectory).TrimEnd('\\') + "\\";
            string file = Path.GetFullPath(fullPath);
            if (!file.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) || file.Length == baseFull.Length) return null;
            return file.Substring(baseFull.Length).Replace('\\', '/');
        }
    }
}
