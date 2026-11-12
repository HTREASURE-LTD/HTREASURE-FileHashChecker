using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HTFileHashChecker.Core.Logging
{
    /// <summary>
    /// Diagnostic log. Callers must never log file contents or hashes of user files, and should log file names
    /// rather than full paths.
    /// </summary>
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception);
    }

    public sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();
        private NullLogger() { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception exception) { }
    }

    /// <summary>Daily log files (HTFileHashChecker-yyyyMMdd.log) with automatic deletion after the retention period.</summary>
    public sealed class FileLogger : ILogger
    {
        private readonly object sync = new object();
        private readonly string directory;
        private bool disabled;

        public FileLogger(string directory, int retentionDays)
        {
            this.directory = directory;
            try
            {
                Directory.CreateDirectory(directory);
                DeleteOldLogs(retentionDays);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                disabled = true; // e.g. portable copy on read-only media: logging is optional
            }
        }

        public string LogDirectory { get { return directory; } }

        public string CurrentLogFile
        {
            get { return Path.Combine(directory, "HTFileHashChecker-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"); }
        }

        public void Info(string message) { Write("INFO ", message, null); }
        public void Warn(string message) { Write("WARN ", message, null); }
        public void Error(string message, Exception exception) { Write("ERROR", message, exception); }

        private void Write(string level, string message, Exception exception)
        {
            if (disabled) return;
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append(' ').Append(level).Append(' ').Append(Sanitize(message));
            if (exception != null)
            {
                // Type, message and stack trace only; exception messages from this application never contain file data.
                sb.Append(" | ").Append(exception.GetType().FullName).Append(": ").Append(Sanitize(exception.Message));
                if (exception.StackTrace != null) sb.AppendLine().Append(exception.StackTrace);
            }
            sb.AppendLine();
            lock (sync)
            {
                try
                {
                    File.AppendAllText(CurrentLogFile, sb.ToString(), new UTF8Encoding(false));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Never let logging break the application.
                }
            }
        }

        private static string Sanitize(string message)
        {
            if (message == null) return string.Empty;
            return message.Replace('\r', ' ').Replace('\n', ' ');
        }

        private void DeleteOldLogs(int retentionDays)
        {
            if (retentionDays <= 0) return;
            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (var file in new DirectoryInfo(directory).GetFiles("HTFileHashChecker-*.log"))
            {
                try
                {
                    if (file.LastWriteTime < cutoff) file.Delete();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }
    }
}
