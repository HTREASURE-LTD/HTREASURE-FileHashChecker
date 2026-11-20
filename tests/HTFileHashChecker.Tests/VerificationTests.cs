using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.Logging;
using HTFileHashChecker.Core.Processing;
using HTFileHashChecker.Core.Verification;
using Xunit;

namespace HTFileHashChecker.Tests
{
    public class ExpectedHashTests
    {
        private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        [Theory]
        [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
        [InlineData("  ba7816bf 8f01cfea 414140de 5dae2223\r\nb00361a3 96177a9c b410ff61 f20015ad  ")]
        [InlineData("0xba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
        [InlineData("​ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad ")]
        public void NormalisesWhitespaceCaseAndPrefix(string input)
        {
            var r = ExpectedHash.Parse(input, HashAlgorithmKind.Sha256);
            Assert.True(r.IsValid, r.Error);
            Assert.Equal(AbcSha256, r.Value.Hex);
        }

        [Theory]
        [InlineData("", "Enter the expected hash")]
        [InlineData("   ", "Enter the expected hash")]
        [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a", "63 characters")]
        [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015adff", "66 characters")]
        [InlineData("ga7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "not hexadecimal")]
        [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  setup.exe", "not hexadecimal")]
        public void RejectsMalformedInput(string input, string expectedMessage)
        {
            var r = ExpectedHash.Parse(input, HashAlgorithmKind.Sha256);
            Assert.False(r.IsValid);
            Assert.Contains(expectedMessage, r.Error);
        }

        [Fact]
        public void WrongLengthForSelectedAlgorithm_SuggestsMatchingAlgorithm()
        {
            var r = ExpectedHash.Parse("900150983cd24fb0d6963f7d28e17f72", HashAlgorithmKind.Sha256);
            Assert.False(r.IsValid);
            Assert.Contains("MD5", r.Error);
        }

        [Theory]
        [InlineData("900150983cd24fb0d6963f7d28e17f72", HashAlgorithmKind.Md5)]
        [InlineData("a9993e364706816aba3e25717850c26c9cd0d89d", HashAlgorithmKind.Sha1)]
        [InlineData(AbcSha256, HashAlgorithmKind.Sha256)]
        public void DetectsAlgorithmFromLength(string input, HashAlgorithmKind expected)
        {
            var r = ExpectedHash.Parse(input, null);
            Assert.True(r.IsValid);
            Assert.Equal(expected, r.Value.Algorithm);
        }

        [Fact]
        public void UnknownLength_IsRejected()
        {
            var r = ExpectedHash.Parse("abcdef", null);
            Assert.False(r.IsValid);
            Assert.Contains("does not match any supported algorithm", r.Error);
        }

        [Fact]
        public void Matches_UsesExactBytes()
        {
            var e = ExpectedHash.Parse(AbcSha256, null).Value;
            byte[] digest;
            Hex.TryParse(AbcSha256, out digest);
            Assert.True(e.Matches(digest));
            digest[31] ^= 1;
            Assert.False(e.Matches(digest));
            Assert.False(e.Matches(new byte[16]));
        }
    }

    public class BatchProcessorTests
    {
        private static BatchProcessor Processor() { return new BatchProcessor(new FileHasher(), NullLogger.Instance); }

        [Fact]
        public void MatchMismatchMissingAndError_AreReported()
        {
            using (var dir = new TempDir())
            {
                string good = dir.File("good.txt", "abc");
                string modified = dir.File("modified.txt", "abd");
                string missing = dir.Combine("missing.txt");
                var expected = ExpectedHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", null).Value;
                var items = new List<HashItem>
                {
                    new HashItem(good, ItemSource.Manual) { Expected = expected },
                    new HashItem(modified, ItemSource.Manual) { Expected = expected },
                    new HashItem(missing, ItemSource.Manual) { Expected = expected },
                    new HashItem(dir.Combine("plain-missing.txt"), ItemSource.Manual),
                };
                var summary = Processor().RunAsync(items, new BatchOptions(), null, CancellationToken.None).Result;
                Assert.Equal(ItemStatus.Match, items[0].Status);
                Assert.Equal(ItemStatus.Mismatch, items[1].Status);
                Assert.Equal(ItemStatus.Missing, items[2].Status);
                Assert.Equal(ItemStatus.Error, items[3].Status);
                Assert.Equal(1, summary.Matched);
                Assert.Equal(1, summary.Mismatched);
                Assert.Equal(1, summary.Missing);
                Assert.Equal(1, summary.Errors);
                Assert.Equal(3, summary.Failed);
                Assert.Equal("File checksum matches.", items[0].Message);
                Assert.Equal("File checksum does not match.", items[1].Message);
            }
        }

        [Fact]
        public void ExpectedAlgorithmDifferentFromItemAlgorithm_IsError()
        {
            using (var dir = new TempDir())
            {
                var item = new HashItem(dir.File("a.txt", "abc"), ItemSource.Manual)
                {
                    Algorithm = HashAlgorithmKind.Sha256,
                    Expected = ExpectedHash.Parse("900150983cd24fb0d6963f7d28e17f72", null).Value,
                };
                Processor().RunAsync(new[] { item }, null, null, CancellationToken.None).Wait();
                Assert.Equal(ItemStatus.Error, item.Status);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        public void ManyFiles_AllHashedWithBoundedWorkers(int workers)
        {
            using (var dir = new TempDir())
            {
                var items = Enumerable.Range(0, 40).Select(i => new HashItem(dir.File("f" + i + ".txt", "content " + i), ItemSource.Folder)).ToList();
                foreach (var i in items) i.Size = new System.IO.FileInfo(i.FullPath).Length;
                var reports = new List<BatchProgress>();
                var progress = new SyncProgressCollector(reports);
                var summary = Processor().RunAsync(items, new BatchOptions { MaxParallelFiles = workers }, progress, CancellationToken.None).Result;
                Assert.Equal(40, summary.Calculated);
                Assert.All(items, i => Assert.Equal(Ref.Hash(HashAlgorithmKind.Sha256, System.IO.File.ReadAllBytes(i.FullPath)), i.HashHex));
                var last = reports.Last();
                Assert.Equal(40, last.Finished);
                Assert.Equal(last.TotalBytes, last.BytesDone);
            }
        }

        [Fact]
        public void Cancellation_MarksRemainingItemsCancelled()
        {
            using (var dir = new TempDir())
            using (var cts = new CancellationTokenSource())
            {
                var items = Enumerable.Range(0, 30).Select(i => new HashItem(dir.File("f" + i + ".bin", new byte[200000]), ItemSource.Folder)).ToList();
                int finished = 0;
                var progress = new SyncProgressCollector(null, p => { if (p.ChangedItem != null && p.ChangedItem.Status != ItemStatus.Hashing && ++finished == 3) cts.Cancel(); });
                var summary = Processor().RunAsync(items, new BatchOptions { MaxParallelFiles = 1 }, progress, cts.Token).Result;
                Assert.True(summary.WasCancelled);
                Assert.True(summary.CancelledItems > 0);
                Assert.True(summary.Calculated >= 3 && summary.Calculated < 30);
                Assert.DoesNotContain(items, i => i.Status == ItemStatus.Pending || i.Status == ItemStatus.Hashing);
            }
        }

        [Fact]
        public void PlanFinalItems_AreNotReprocessed()
        {
            using (var dir = new TempDir())
            {
                var item = new HashItem(dir.File("x.txt", "abc"), ItemSource.Manifest) { Status = ItemStatus.Unexpected };
                Processor().RunAsync(new[] { item }, null, null, CancellationToken.None).Wait();
                Assert.Equal(ItemStatus.Unexpected, item.Status);
                Assert.Null(item.HashHex);
            }
        }
    }

    internal sealed class SyncProgressCollector : System.IProgress<BatchProgress>
    {
        private readonly List<BatchProgress> sink;
        private readonly System.Action<BatchProgress> action;
        public SyncProgressCollector(List<BatchProgress> sink, System.Action<BatchProgress> action = null) { this.sink = sink; this.action = action; }
        public void Report(BatchProgress value)
        {
            if (sink != null) lock (sink) sink.Add(value);
            if (action != null) action(value);
        }
    }
}
