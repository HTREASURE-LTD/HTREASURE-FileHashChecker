using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HTFileHashChecker.Core.Hashing;
using Xunit;

namespace HTFileHashChecker.Tests
{
    /// <summary>
    /// Published test vectors: FIPS 180-2 / NIST CSRC examples for SHA-1, SHA-256, SHA-384, SHA-512 and the
    /// RFC 1321 appendix A.5 test suite for MD5. Each vector is hashed through the real file pipeline.
    /// </summary>
    public class KnownAnswerTests
    {
        private const string Abc448 = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";
        private const string Abc896 = "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu";

        public static readonly object[][] Vectors =
        {
            // SHA-256
            new object[] { HashAlgorithmKind.Sha256, "", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" },
            new object[] { HashAlgorithmKind.Sha256, "abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" },
            new object[] { HashAlgorithmKind.Sha256, Abc448, "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1" },
            // SHA-512
            new object[] { HashAlgorithmKind.Sha512, "", "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e" },
            new object[] { HashAlgorithmKind.Sha512, "abc", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f" },
            new object[] { HashAlgorithmKind.Sha512, Abc896, "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909" },
            // SHA-384
            new object[] { HashAlgorithmKind.Sha384, "", "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b" },
            new object[] { HashAlgorithmKind.Sha384, "abc", "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7" },
            new object[] { HashAlgorithmKind.Sha384, Abc896, "09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039" },
            // SHA-1
            new object[] { HashAlgorithmKind.Sha1, "", "da39a3ee5e6b4b0d3255bfef95601890afd80709" },
            new object[] { HashAlgorithmKind.Sha1, "abc", "a9993e364706816aba3e25717850c26c9cd0d89d" },
            new object[] { HashAlgorithmKind.Sha1, Abc448, "84983e441c3bd26ebaae4aa1f95129e5e54670f1" },
            // MD5 (RFC 1321)
            new object[] { HashAlgorithmKind.Md5, "", "d41d8cd98f00b204e9800998ecf8427e" },
            new object[] { HashAlgorithmKind.Md5, "a", "0cc175b9c0f1b6a831c399e269772661" },
            new object[] { HashAlgorithmKind.Md5, "abc", "900150983cd24fb0d6963f7d28e17f72" },
            new object[] { HashAlgorithmKind.Md5, "message digest", "f96b697d7cb7938d525a2f31aaf161d0" },
            new object[] { HashAlgorithmKind.Md5, "abcdefghijklmnopqrstuvwxyz", "c3fcd3d76192e4007dfb496cca67e13b" },
            new object[] { HashAlgorithmKind.Md5, "12345678901234567890123456789012345678901234567890123456789012345678901234567890", "57edf4a22be3c955ac49da2e2107b67a" },
        };

        [Theory]
        [MemberData(nameof(Vectors))]
        public void FileHash_MatchesPublishedVector(HashAlgorithmKind kind, string message, string expected)
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("vector.bin", Encoding.ASCII.GetBytes(message));
                var outcome = new FileHasher().ComputeHash(path, kind, CancellationToken.None);
                Assert.Equal(expected, outcome.Hex);
                Assert.Equal(message.Length, outcome.Length);
            }
        }

        public static readonly object[][] MillionA =
        {
            new object[] { HashAlgorithmKind.Sha256, "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0" },
            new object[] { HashAlgorithmKind.Sha512, "e718483d0ce769644e2e42c7bc15b4638e1f98b13b2044285632a803afa973ebde0ff244877ea60a4cb0432ce577c31beb009c5c2c49aa2e4eadb217ad8cc09b" },
            new object[] { HashAlgorithmKind.Sha384, "9d0e1809716474cb086e834e310a4a1ced149e9c00f248527972cec5704c2a5b07b8b3dc38ecc4ebae97ddd87f3d8985" },
            new object[] { HashAlgorithmKind.Sha1, "34aa973cd4c4daa4f61eeb2bdbad27316534016f" },
        };

        /// <summary>One million 'a' characters, hashed with a small buffer so many blocks are streamed.</summary>
        [Theory]
        [MemberData(nameof(MillionA))]
        public void MillionA_StreamedInSmallBlocks(HashAlgorithmKind kind, string expected)
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("million-a.txt", Enumerable.Repeat((byte)'a', 1000000).ToArray());
                Assert.Equal(expected, new FileHasher(4096).ComputeHash(path, kind, CancellationToken.None).Hex);
            }
        }

        [Fact]
        public void EmptyFile_HasEmptyMessageDigest()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("empty.dat", new byte[0]);
                var outcome = new FileHasher().ComputeHash(path, HashAlgorithmKind.Sha256, CancellationToken.None);
                Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", outcome.Hex);
                Assert.Equal(0, outcome.Length);
            }
        }

        [Theory]
        [InlineData(HashAlgorithmKind.Sha256, 64, false)]
        [InlineData(HashAlgorithmKind.Sha512, 128, false)]
        [InlineData(HashAlgorithmKind.Sha384, 96, false)]
        [InlineData(HashAlgorithmKind.Sha1, 40, true)]
        [InlineData(HashAlgorithmKind.Md5, 32, true)]
        public void AlgorithmMetadata(HashAlgorithmKind kind, int hexLength, bool legacy)
        {
            var info = HashAlgorithms.Get(kind);
            Assert.Equal(hexLength, info.HexLength);
            Assert.Equal(legacy, info.IsLegacy);
            HashAlgorithmKind detected;
            Assert.True(HashAlgorithms.TryDetectFromHexLength(hexLength, out detected));
            Assert.Equal(kind, detected);
        }

        [Fact]
        public void DefaultIsSha256()
        {
            Assert.Equal(HashAlgorithmKind.Sha256, HashAlgorithms.Default);
            Assert.Equal(HashAlgorithmKind.Sha256, HashAlgorithms.All[0].Kind);
        }

        [Theory]
        [InlineData("SHA256SUMS", HashAlgorithmKind.Sha256)]
        [InlineData("sha512sums.txt", HashAlgorithmKind.Sha512)]
        [InlineData("SHA384SUMS", HashAlgorithmKind.Sha384)]
        [InlineData("SHA1SUMS", HashAlgorithmKind.Sha1)]
        [InlineData("MD5SUMS", HashAlgorithmKind.Md5)]
        [InlineData("ubuntu.iso.sha256", HashAlgorithmKind.Sha256)]
        [InlineData("setup.exe.md5", HashAlgorithmKind.Md5)]
        public void DetectsAlgorithmFromManifestName(string name, HashAlgorithmKind expected)
        {
            HashAlgorithmKind kind;
            Assert.True(HashAlgorithms.TryDetectFromFileName(name, out kind));
            Assert.Equal(expected, kind);
        }

        [Theory]
        [InlineData("sha-256", HashAlgorithmKind.Sha256)]
        [InlineData("SHA512", HashAlgorithmKind.Sha512)]
        [InlineData("md5", HashAlgorithmKind.Md5)]
        [InlineData("Sha_1", HashAlgorithmKind.Sha1)]
        public void ParsesAlgorithmNames(string name, HashAlgorithmKind expected)
        {
            HashAlgorithmKind kind;
            Assert.True(HashAlgorithms.TryParse(name, out kind));
            Assert.Equal(expected, kind);
        }

        [Fact]
        public void RejectsUnknownAlgorithmName()
        {
            HashAlgorithmKind kind;
            Assert.False(HashAlgorithms.TryParse("sha3-256", out kind));
            Assert.False(HashAlgorithms.TryParse("", out kind));
        }

        [Fact]
        public void Hex_RoundTripAndFixedTimeEquals()
        {
            byte[] bytes;
            Assert.True(Hex.TryParse("00ff10AB", out bytes));
            Assert.Equal(new byte[] { 0x00, 0xff, 0x10, 0xab }, bytes);
            Assert.Equal("00ff10ab", Hex.ToHex(bytes));
            Assert.False(Hex.TryParse("abc", out bytes));
            Assert.False(Hex.TryParse("zz", out bytes));
            Assert.True(Hex.FixedTimeEquals(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
            Assert.False(Hex.FixedTimeEquals(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
            Assert.False(Hex.FixedTimeEquals(new byte[] { 1, 2 }, new byte[] { 1, 2, 3 }));
            Assert.False(Hex.FixedTimeEquals(null, new byte[0]));
        }
    }
}
