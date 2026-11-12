using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HTFileHashChecker.Core.IO;

namespace HTFileHashChecker.Core.Hashing
{
    /// <summary>Result of hashing one file.</summary>
    public sealed class FileHashOutcome
    {
        public FileHashOutcome(HashAlgorithmKind algorithm, byte[] digest, long length, TimeSpan elapsed)
        {
            Algorithm = algorithm;
            Digest = digest;
            Hex = Hashing.Hex.ToHex(digest);
            Length = length;
            Elapsed = elapsed;
        }

        public HashAlgorithmKind Algorithm { get; private set; }
        public byte[] Digest { get; private set; }
        public string Hex { get; private set; }
        public long Length { get; private set; }
        public TimeSpan Elapsed { get; private set; }
    }

    /// <summary>The file was modified, truncated or extended while it was being read.</summary>
    [Serializable]
    public sealed class FileChangedDuringHashingException : IOException
    {
        public FileChangedDuringHashingException(string message) : base(message) { }

        private FileChangedDuringHashingException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    /// <summary>The opened file resolves outside the folder it must stay inside (symbolic link or junction escape).</summary>
    [Serializable]
    public sealed class PathOutsideRootException : IOException
    {
        public PathOutsideRootException(string message) : base(message) { }

        private PathOutsideRootException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Streams a file through a hash algorithm. Files are opened read-only and are never modified, executed or
    /// loaded fully into memory, so files of any size (including ISO images larger than 4 GB) are supported.
    /// </summary>
    public sealed class FileHasher
    {
        public const int DefaultBufferSize = 1024 * 1024;
        private static readonly long ProgressIntervalTicks = Stopwatch.Frequency / 20; // at most 20 updates per second

        private readonly int bufferSize;

        public FileHasher() : this(DefaultBufferSize) { }

        public FileHasher(int bufferSize)
        {
            if (bufferSize < 4096) throw new ArgumentOutOfRangeException("bufferSize");
            this.bufferSize = bufferSize;
        }

        /// <summary>Hashes <paramref name="path"/>.</summary>
        /// <param name="requiredRoot">When set, the file's fully resolved path (after symbolic links and junctions)
        /// must lie inside this folder's resolved path; otherwise <see cref="PathOutsideRootException"/> is thrown.</param>
        /// <param name="bytesProgress">Receives the number of bytes read so far (throttled).</param>
        public FileHashOutcome ComputeHash(string path, HashAlgorithmKind algorithm, CancellationToken cancellationToken,
            IProgress<long> bytesProgress = null, string requiredRoot = null)
        {
            if (path == null) throw new ArgumentNullException("path");
            PathValidator.ThrowIfInvalidLocalFilePath(path);
            if (Directory.Exists(path))
                throw new UnauthorizedAccessException("'" + Path.GetFileName(path) + "' is a folder, not a file.");

            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();

            using (FileStream stream = OpenForReading(path))
            {
                if (requiredRoot != null) EnsureInsideRoot(stream, requiredRoot);

                long startLength, startWrite;
                bool haveStamp = NativeMethods.TryGetFileStamp(stream.SafeFileHandle, out startLength, out startWrite);
                if (!haveStamp) startLength = stream.Length;

                using (var hash = HashAlgorithms.Create(algorithm))
                {
                    var buffer = new byte[bufferSize];
                    long total = 0;
                    long lastReport = stopwatch.ElapsedTicks;
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        hash.TransformBlock(buffer, 0, read, null, 0);
                        total += read;
                        cancellationToken.ThrowIfCancellationRequested();
                        if (bytesProgress != null && stopwatch.ElapsedTicks - lastReport >= ProgressIntervalTicks)
                        {
                            lastReport = stopwatch.ElapsedTicks;
                            bytesProgress.Report(total);
                        }
                    }
                    hash.TransformFinalBlock(buffer, 0, 0);

                    long endLength, endWrite;
                    bool changed = total != startLength;
                    if (!changed && haveStamp && NativeMethods.TryGetFileStamp(stream.SafeFileHandle, out endLength, out endWrite))
                        changed = endLength != startLength || endWrite != startWrite;
                    if (changed)
                    {
                        throw new FileChangedDuringHashingException(
                            "The file changed while it was being read, so the hash would not be reliable. Close any program that is writing to it and try again.");
                    }

                    if (bytesProgress != null) bytesProgress.Report(total);
                    stopwatch.Stop();
                    return new FileHashOutcome(algorithm, hash.Hash, total, stopwatch.Elapsed);
                }
            }
        }

        /// <summary>
        /// Opens read-only and, where possible, prevents other programs from writing while the file is hashed
        /// (FileShare.Read). If another program already has the file open for writing, falls back to a shared open;
        /// any change during hashing is then detected by comparing size and last-write time before and after.
        /// </summary>
        private FileStream OpenForReading(string path)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            }
            catch (IOException ex) when (ErrorDescriber.IsSharingViolation(ex))
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            }
        }

        private static void EnsureInsideRoot(FileStream stream, string requiredRoot)
        {
            string finalFile = NativeMethods.GetFinalPath(stream.SafeFileHandle);
            string finalRoot = NativeMethods.GetFinalDirectoryPath(requiredRoot).TrimEnd('\\') + "\\";
            if (!finalFile.StartsWith(finalRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new PathOutsideRootException(
                    "This entry resolves to a file outside the verification folder (through a symbolic link or junction), so it was not read.");
            }
        }
    }
}
