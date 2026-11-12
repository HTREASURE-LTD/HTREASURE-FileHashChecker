using System;
using System.IO;
using System.Security;
using HTFileHashChecker.Core.Hashing;

namespace HTFileHashChecker.Core.IO
{
    /// <summary>Turns exceptions into short, actionable messages for users and reports.</summary>
    public static class ErrorDescriber
    {
        private const int ERROR_SHARING_VIOLATION = 32;
        private const int ERROR_LOCK_VIOLATION = 33;

        internal static int Win32Code(Exception ex)
        {
            return ex.HResult & 0xFFFF;
        }

        public static bool IsSharingViolation(IOException ex)
        {
            return Win32Code(ex) == ERROR_SHARING_VIOLATION;
        }

        public static bool IsNotFound(Exception ex)
        {
            return ex is FileNotFoundException || ex is DirectoryNotFoundException;
        }

        public static string Describe(Exception ex)
        {
            if (ex == null) return string.Empty;
            if (ex is FileChangedDuringHashingException || ex is PathOutsideRootException
                || ex is HashAlgorithmUnavailableException || ex is InvalidPathException)
                return ex.Message;
            if (ex is FileNotFoundException) return "File not found. It may have been moved, renamed or deleted.";
            if (ex is DirectoryNotFoundException) return "Folder not found. It may have been moved, renamed or deleted.";
            if (ex is PathTooLongException) return "The path is too long for this version of Windows.";
            if (ex is UnauthorizedAccessException || ex is SecurityException)
            {
                // FileStream reports folders as "access denied"; FileHasher supplies a clearer message.
                return ex.Message.Contains("is a folder") ? ex.Message
                    : "Access denied. You do not have permission to read this file.";
            }
            var io = ex as IOException;
            if (io != null)
            {
                int code = Win32Code(io);
                if (code == ERROR_SHARING_VIOLATION) return "The file is in use by another program and cannot be read.";
                if (code == ERROR_LOCK_VIOLATION) return "Part of the file is locked by another program.";
                return "Read error: " + io.Message;
            }
            if (ex is ArgumentException || ex is NotSupportedException) return "Invalid path: " + ex.Message;
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
