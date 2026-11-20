using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using Xunit;

namespace HTFileHashChecker.Tests
{
    public class FileHasherTests
    {
        [Fact]
        public void UnicodeFileName_IsHashed()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("Ünïcødé 文件 файл ✓ (copy).txt", "abc");
                Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                    new FileHasher().ComputeHash(path, HashAlgorithmKind.Sha256, CancellationToken.None).Hex);
            }
        }

        [Fact]
        public void LongPath_Over260Characters_IsHashed()
        {
            using (var dir = new TempDir())
            {
                string rel = string.Join("\\", Enumerable.Range(0, 6).Select(i => new string((char)('a' + i), 60))) + "\\long-path-file.txt";
                string path = dir.Combine(rel);
                Assert.True(path.Length > 300);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "abc");
                var outcome = new FileHasher().ComputeHash(path, HashAlgorithmKind.Sha256, CancellationToken.None);
                Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", outcome.Hex);
            }
        }

        [Fact]
        public void MissingFile_ThrowsFileNotFound_WithFriendlyMessage()
        {
            using (var dir = new TempDir())
            {
                var ex = Assert.Throws<FileNotFoundException>(() =>
                    new FileHasher().ComputeHash(dir.Combine("nope.bin"), HashAlgorithmKind.Sha256, CancellationToken.None));
                Assert.Contains("not found", ErrorDescriber.Describe(ex));
            }
        }

        [Fact]
        public void ExclusivelyLockedFile_ReportsInUse()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("locked.bin", "data");
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var ex = Assert.ThrowsAny<IOException>(() => new FileHasher().ComputeHash(path, HashAlgorithmKind.Sha256, CancellationToken.None));
                    Assert.Contains("in use", ErrorDescriber.Describe(ex));
                }
            }
        }

        [Fact]
        public void Folder_IsRejectedAsNotAFile()
        {
            using (var dir = new TempDir())
            {
                var ex = Assert.Throws<UnauthorizedAccessException>(() => new FileHasher().ComputeHash(dir.Path, HashAlgorithmKind.Sha256, CancellationToken.None));
                Assert.Contains("folder", ErrorDescriber.Describe(ex));
            }
        }

        [Theory]
        [InlineData(@"\\.\PhysicalDrive0")]
        [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1\x")]
        [InlineData(@"C:\folder\file.txt:hidden")]
        [InlineData(@"relative\file.txt")]
        [InlineData(@"C:\a\*.txt")]
        public void UnsafePaths_AreRejected(string path)
        {
            Assert.Throws<InvalidPathException>(() => new FileHasher().ComputeHash(path, HashAlgorithmKind.Sha256, CancellationToken.None));
        }

        [Fact]
        public void PreCancelledToken_ThrowsWithoutReading()
        {
            using (var dir = new TempDir())
            using (var cts = new CancellationTokenSource())
            {
                string path = dir.File("a.bin", new byte[10000]);
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => new FileHasher().ComputeHash(path, HashAlgorithmKind.Sha256, cts.Token));
            }
        }

        [Fact]
        public void CancellationDuringRead_StopsAndReleasesFile()
        {
            using (var dir = new TempDir())
            using (var cts = new CancellationTokenSource())
            {
                string path = dir.File("big.bin", new byte[4 * 1024 * 1024]);
                var hasher = new FileHasher(64 * 1024) { ReportEveryBlock = true };
                long seen = 0;
                var progress = new InlineProgress(done => { seen = done; if (done >= 256 * 1024) cts.Cancel(); });
                Assert.ThrowsAny<OperationCanceledException>(() => hasher.ComputeHash(path, HashAlgorithmKind.Sha256, cts.Token, progress));
                Assert.True(seen < 4 * 1024 * 1024, "Reading should stop early");
                // The file handle was released: the file can be opened exclusively and deleted.
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(path);
            }
        }

        [Fact]
        public void FileModifiedWhileHashing_IsDetected()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("growing.log", new byte[1024 * 1024]);
                // Another program already has the file open for writing (like a log writer).
                using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    var hasher = new FileHasher(64 * 1024) { ReportEveryBlock = true };
                    bool wrote = false;
                    var progress = new InlineProgress(done =>
                    {
                        if (wrote || done < 128 * 1024) return;
                        wrote = true;
                        writer.Seek(0, SeekOrigin.End);
                        writer.Write(new byte[100000], 0, 100000);
                        writer.Flush(true);
                    });
                    Assert.Throws<FileChangedDuringHashingException>(() => hasher.ComputeHash(path, HashAlgorithmKind.Sha256, CancellationToken.None, progress));
                }
            }
        }

        [Fact]
        public void HashingNeverModifiesTheFile()
        {
            using (var dir = new TempDir())
            {
                var rnd = new Random(42);
                var data = new byte[3 * 1024 * 1024 + 17];
                rnd.NextBytes(data);
                string path = dir.File("payload.bin", data);
                var before = new FileInfo(path);
                DateTime written = before.LastWriteTimeUtc;
                string sha = Ref.Sha256OfFile(path);

                foreach (var algorithm in HashAlgorithms.All)
                    new FileHasher().ComputeHash(path, algorithm.Kind, CancellationToken.None);

                var after = new FileInfo(path);
                Assert.Equal(data.Length, after.Length);
                Assert.Equal(written, after.LastWriteTimeUtc);
                Assert.Equal(sha, Ref.Sha256OfFile(path));
                Assert.Equal(data, File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void StreamingResult_EqualsWholeBufferResult_ForOddSizes()
        {
            using (var dir = new TempDir())
            {
                var rnd = new Random(7);
                foreach (int size in new[] { 1, 4095, 4096, 4097, 65535, 1048577 })
                {
                    var data = new byte[size];
                    rnd.NextBytes(data);
                    string path = dir.File("s" + size + ".bin", data);
                    foreach (var a in HashAlgorithms.All)
                        Assert.Equal(Ref.Hash(a.Kind, data), new FileHasher(4096).ComputeHash(path, a.Kind, CancellationToken.None).Hex);
                }
            }
        }

        [Fact]
        public void ContainmentRoot_RejectsJunctionEscape()
        {
            using (var outside = new TempDir())
            using (var root = new TempDir())
            {
                outside.File("secret.txt", "secret");
                string link = root.Combine("escape");
                Assert.True(Ref.TryCreateJunction(link, outside.Path), "Could not create a directory junction (the temp folder must be on NTFS).");
                string viaLink = Path.Combine(link, "secret.txt");
                Assert.Throws<PathOutsideRootException>(() =>
                    new FileHasher().ComputeHash(viaLink, HashAlgorithmKind.Sha256, CancellationToken.None, null, root.Path));
                // A normal file inside the root is accepted.
                string inside = root.File("ok.txt", "abc");
                new FileHasher().ComputeHash(inside, HashAlgorithmKind.Sha256, CancellationToken.None, null, root.Path);
            }
        }

        /// <summary>
        /// Integration test with a real file larger than 4 GiB (offsets beyond 32 bits). The file is created sparse
        /// (zero-filled, fast) and its digest is compared with an independent computation over the same zero bytes.
        /// </summary>
        [Fact]
        [Trait("Category", "LargeFile")]
        public void LargeFile_Over4GB_IsHashedCorrectly()
        {
            const long size = 4L * 1024 * 1024 * 1024 + 12345;
            using (var dir = new TempDir())
            {
                string path = dir.Combine("large.iso");
                using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    MakeSparse(fs);
                    fs.SetLength(size);
                    fs.Seek(size - 1, SeekOrigin.Begin);
                    fs.WriteByte(0x5A); // last byte non-zero: proves the tail beyond 4 GiB is read
                }
                Assert.Equal(size, new FileInfo(path).Length);

                var outcome = new FileHasher().ComputeHash(path, HashAlgorithmKind.Sha256, CancellationToken.None);
                Assert.Equal(size, outcome.Length);

                string expected;
                using (var sha = SHA256.Create())
                {
                    var zeros = new byte[1024 * 1024];
                    long remaining = size - 1;
                    while (remaining > 0)
                    {
                        int n = (int)Math.Min(zeros.Length, remaining);
                        sha.TransformBlock(zeros, 0, n, null, 0);
                        remaining -= n;
                    }
                    sha.TransformFinalBlock(new byte[] { 0x5A }, 0, 1);
                    expected = Hex.ToHex(sha.Hash);
                }
                Assert.Equal(expected, outcome.Hex);
            }
        }

        private static void MakeSparse(FileStream fs)
        {
            const uint FSCTL_SET_SPARSE = 0x000900C4;
            uint returned;
            NativeTest.DeviceIoControl(fs.SafeFileHandle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, out returned, IntPtr.Zero);
        }
    }

    internal static class NativeTest
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, IntPtr inBuf, uint inSize,
            IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);
    }

    internal sealed class InlineProgress : IProgress<long>
    {
        private readonly Action<long> action;
        public InlineProgress(Action<long> action) { this.action = action; }
        public void Report(long value) { action(value); }
    }
}
