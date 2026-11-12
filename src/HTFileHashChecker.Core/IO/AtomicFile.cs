using System;
using System.IO;
using System.Text;

namespace HTFileHashChecker.Core.IO
{
    /// <summary>Writes files via a temporary file in the same folder so a crash never leaves a half-written file,
    /// and never overwrites an existing file unless explicitly asked to.</summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string content, Encoding encoding, bool overwrite)
        {
            if (path == null) throw new ArgumentNullException("path");
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!overwrite && File.Exists(full))
                throw new IOException("'" + Path.GetFileName(full) + "' already exists and was not overwritten.");

            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, encoding))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(full))
                {
                    if (!overwrite) throw new IOException("'" + Path.GetFileName(full) + "' already exists and was not overwritten.");
                    File.Replace(temp, full, null, true);
                }
                else
                {
                    // File.Move fails if another process created the file in the meantime: no silent overwrite.
                    File.Move(temp, full);
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
