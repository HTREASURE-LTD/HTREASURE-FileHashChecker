using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HTFileHashChecker.Core.Hashing;

namespace HTFileHashChecker.Tests
{
    /// <summary>A unique temporary folder deleted when disposed.</summary>
    public sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HTFHC-Tests", Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string File(string relative, string content)
        {
            return File(relative, Encoding.UTF8.GetBytes(content));
        }

        public string File(string relative, byte[] content)
        {
            string full = System.IO.Path.Combine(Path, relative.Replace('/', '\\'));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, content);
            return full;
        }

        public string Combine(string relative) { return System.IO.Path.Combine(Path, relative.Replace('/', '\\')); }

        public void Dispose()
        {
            // Remove junctions first so deletion never follows them.
            try
            {
                foreach (var d in Directory.GetDirectories(Path, "*", SearchOption.AllDirectories))
                    if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(d);
                Directory.Delete(Path, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class Ref
    {
        /// <summary>SHA-256 computed independently of FileHasher (whole-file read) for cross-checks.</summary>
        public static string Sha256OfFile(string path)
        {
            using (var sha = SHA256.Create())
                return Hex.ToHex(sha.ComputeHash(System.IO.File.ReadAllBytes(path)));
        }

        public static string Hash(HashAlgorithmKind kind, byte[] data)
        {
            using (var h = HashAlgorithms.Create(kind)) return Hex.ToHex(h.ComputeHash(data));
        }

        /// <summary>Creates a directory junction (no administrator rights required).</summary>
        public static bool TryCreateJunction(string link, string target)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit(10000);
                return p.ExitCode == 0 && Directory.Exists(link);
            }
        }
    }
}
