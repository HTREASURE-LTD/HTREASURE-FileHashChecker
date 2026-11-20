using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Logging;
using HTFileHashChecker.Core.Manifests;
using HTFileHashChecker.Core.Processing;
using Xunit;

namespace HTFileHashChecker.Tests
{
    public class ManifestParserTests
    {
        private const string Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        private const string Empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        private static ManifestDocument Parse(string text, string name = "SHA256SUMS")
        {
            return ManifestParser.Parse(text, new ManifestParseOptions { FileNameHint = name });
        }

        [Fact]
        public void GnuTextAndBinaryLines_WithSpacesInNames()
        {
            var doc = Parse(Abc + "  my file.iso\n" + Empty + " *folder/empty file.bin\n");
            Assert.Empty(doc.Issues);
            Assert.Equal(2, doc.Entries.Count);
            Assert.Equal("my file.iso", doc.Entries[0].RelativePath);
            Assert.False(doc.Entries[0].BinaryMode);
            Assert.Equal("folder/empty file.bin", doc.Entries[1].RelativePath);
            Assert.True(doc.Entries[1].BinaryMode);
        }

        [Fact]
        public void NameWithLeadingSpace_IsPreserved()
        {
            var doc = Parse(Abc + "   leading.txt\n");
            Assert.Equal(" leading.txt", doc.Entries[0].RelativePath);
        }

        [Fact]
        public void BsdTagFormat_IsParsed()
        {
            var doc = Parse("SHA256 (setup (x64).exe) = " + Abc + "\nMD5 (old.zip) = 900150983cd24fb0d6963f7d28e17f72\n", "CHECKSUMS");
            Assert.Empty(doc.Issues);
            Assert.Equal("setup (x64).exe", doc.Entries[0].RelativePath);
            Assert.Equal(HashAlgorithmKind.Sha256, doc.Entries[0].Algorithm);
            Assert.Equal(HashAlgorithmKind.Md5, doc.Entries[1].Algorithm);
        }

        [Fact]
        public void EscapedNames_AreUnescaped()
        {
            var doc = Parse("\\" + Abc + "  dir/back\\\\slash.txt\n");
            Assert.Equal("dir/back\\slash.txt", doc.Entries[0].RelativePath);
        }

        [Fact]
        public void EscapedNewlineInName_IsRejectedAsUnsafe()
        {
            var doc = Parse("\\" + Abc + "  line\\nbreak.txt\n");
            Assert.Contains(doc.Issues, i => i.Kind == ManifestIssueKind.UnsafePath);
            Assert.False(doc.Entries.Single().IsUsable);
        }

        [Fact]
        public void UnicodeNames_Utf8WithAndWithoutBom_AndUtf16()
        {
            string text = Abc + "  données/文件 ✓.txt\n";
            foreach (var bytes in new[]
            {
                new UTF8Encoding(false).GetBytes(text),
                new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(),
                new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
            })
            {
                var doc = ManifestParser.Parse(bytes, new ManifestParseOptions { FileNameHint = "SHA256SUMS" });
                Assert.Empty(doc.Issues);
                Assert.Equal("données/文件 ✓.txt", doc.Entries.Single().RelativePath);
            }
        }

        [Fact]
        public void CrLfLineEndings_CommentsAndBlankLines()
        {
            var doc = Parse("# generated\r\n\r\n" + Abc + "  a.txt\r\n");
            Assert.Empty(doc.Issues);
            Assert.Equal("a.txt", doc.Entries.Single().RelativePath);
        }

        [Theory]
        [InlineData("../outside.txt")]
        [InlineData("dir/../../outside.txt")]
        [InlineData("..\\outside.txt")]
        [InlineData("/etc/passwd")]
        [InlineData("\\Windows\\win.ini")]
        [InlineData("C:\\Windows\\win.ini")]
        [InlineData("C:relative.txt")]
        [InlineData("\\\\server\\share\\file.txt")]
        [InlineData("file.txt:stream")]
        [InlineData("dir//file.txt")]
        [InlineData("dir/./file.txt")]
        [InlineData("CON")]
        [InlineData("dir/nul.txt")]
        [InlineData("COM1.log")]
        [InlineData("trailing.")]
        [InlineData("trailing ")]
        [InlineData("wild*.txt")]
        public void PathTraversalAndUnsafePaths_AreRejected(string name)
        {
            var doc = Parse(Abc + "  " + name + "\n");
            Assert.Contains(doc.Issues, i => i.Kind == ManifestIssueKind.UnsafePath && i.IsError);
            Assert.False(doc.Entries.Single().IsUsable);
        }

        [Fact]
        public void DotSlashPrefix_IsAccepted()
        {
            var doc = Parse(Abc + "  ./sub/file.txt\n");
            Assert.Empty(doc.Issues);
        }

        [Fact]
        public void IdenticalDuplicate_IsWarningAndIgnored()
        {
            var doc = Parse(Abc + "  a.txt\n" + Abc + "  a.txt\n");
            Assert.Single(doc.Entries);
            var issue = Assert.Single(doc.Issues);
            Assert.Equal(ManifestIssueKind.Duplicate, issue.Kind);
            Assert.False(issue.IsError);
        }

        [Fact]
        public void ConflictingDuplicate_IncludingCaseOnlyDifference_IsRejected()
        {
            var doc = Parse(Abc + "  Setup.exe\n" + Empty + "  setup.EXE\n");
            Assert.Contains(doc.Issues, i => i.Kind == ManifestIssueKind.ConflictingDuplicate && i.IsError);
            Assert.False(doc.Entries.Single().IsUsable);
        }

        [Theory]
        [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  a.txt", ManifestIssueKind.InvalidFormat)]
        [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015  a.txt", ManifestIssueKind.InvalidHash)]
        [InlineData("SHA3 (a.txt) = abcd", ManifestIssueKind.UnsupportedAlgorithm)]
        [InlineData("just some text", ManifestIssueKind.InvalidFormat)]
        public void InvalidLines_AreReportedWithLineNumbers(string line, ManifestIssueKind kind)
        {
            var doc = Parse(Abc + "  ok.txt\n" + line + "\n");
            var issue = Assert.Single(doc.Issues);
            Assert.Equal(kind, issue.Kind);
            Assert.Equal(2, issue.LineNumber);
            Assert.Single(doc.Entries);
        }

        [Fact]
        public void HashLengthNotMatchingManifestAlgorithm_IsInvalid()
        {
            var doc = Parse("900150983cd24fb0d6963f7d28e17f72  a.txt\n", "SHA256SUMS");
            Assert.Equal(ManifestIssueKind.InvalidHash, Assert.Single(doc.Issues).Kind);
        }

        [Fact]
        public void BsdTagContradictingFileName_IsRejected()
        {
            var doc = Parse("MD5 (a.txt) = 900150983cd24fb0d6963f7d28e17f72\n", "SHA256SUMS");
            Assert.Equal(ManifestIssueKind.AlgorithmMismatch, Assert.Single(doc.Issues).Kind);
        }

        [Fact]
        public void AlgorithmDetectedFromLength_WhenNameGivesNoHint()
        {
            var doc = Parse("900150983cd24fb0d6963f7d28e17f72  a.txt\n", "checksums.txt");
            Assert.Equal(HashAlgorithmKind.Md5, doc.Entries.Single().Algorithm);
        }

        [Fact]
        public void InvalidUtf8_FallsBackWithWarning()
        {
            var bytes = Encoding.ASCII.GetBytes(Abc + "  caf").Concat(new byte[] { 0xE9 }).Concat(Encoding.ASCII.GetBytes(".txt\n")).ToArray();
            var doc = ManifestParser.Parse(bytes, new ManifestParseOptions { FileNameHint = "SHA256SUMS" });
            Assert.Contains(doc.Issues, i => i.Kind == ManifestIssueKind.Encoding && !i.IsError);
            Assert.Single(doc.Entries);
        }
    }

    public class ManifestWriterTests
    {
        private const string Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        private const string Empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        [Fact]
        public void Output_IsSortedGnuFormat_WithForwardSlashes()
        {
            string text = ManifestWriter.Format(new[]
            {
                new ManifestWriteEntry("z.txt", HashAlgorithmKind.Sha256, Abc),
                new ManifestWriteEntry("sub\\b file.txt", HashAlgorithmKind.Sha256, Empty.ToUpperInvariant()),
                new ManifestWriteEntry("A.txt", HashAlgorithmKind.Sha256, Abc),
            }, null);
            Assert.Equal(Abc + "  A.txt\n" + Empty + "  sub/b file.txt\n" + Abc + "  z.txt\n", text);
        }

        [Fact]
        public void Output_IsDeterministic_RegardlessOfInputOrder()
        {
            var entries = Enumerable.Range(0, 50).Select(i => new ManifestWriteEntry("f" + (i * 7 % 50) + ".bin", HashAlgorithmKind.Sha256, Abc)).ToList();
            string a = ManifestWriter.Format(entries, null);
            entries.Reverse();
            Assert.Equal(a, ManifestWriter.Format(entries, null));
        }

        [Fact]
        public void RoundTrip_ThroughParser()
        {
            var entries = new[]
            {
                new ManifestWriteEntry("données été.bin", HashAlgorithmKind.Sha512, new string('a', 128)),
                new ManifestWriteEntry("dir/space name.iso", HashAlgorithmKind.Sha512, new string('b', 128)),
            };
            string text = ManifestWriter.Format(entries, new ManifestWriteOptions { BinaryMarker = true });
            var doc = ManifestParser.Parse(text, new ManifestParseOptions { FileNameHint = "SHA512SUMS" });
            Assert.Empty(doc.Issues);
            Assert.Equal(new[] { "dir/space name.iso", "données été.bin" }, doc.Entries.Select(e => e.RelativePath));
            Assert.All(doc.Entries, e => Assert.True(e.BinaryMode));
        }

        [Fact]
        public void MixedAlgorithms_RequireBsdFormat()
        {
            var entries = new[]
            {
                new ManifestWriteEntry("a.txt", HashAlgorithmKind.Sha256, Abc),
                new ManifestWriteEntry("b.txt", HashAlgorithmKind.Md5, "900150983cd24fb0d6963f7d28e17f72"),
            };
            Assert.Throws<ArgumentException>(() => ManifestWriter.Format(entries, null));
            string bsd = ManifestWriter.Format(entries, new ManifestWriteOptions { Format = ManifestLineFormat.Bsd });
            Assert.Equal("SHA256 (a.txt) = " + Abc + "\nMD5 (b.txt) = 900150983cd24fb0d6963f7d28e17f72\n", bsd);
            Assert.Equal(2, ManifestParser.Parse(bsd, new ManifestParseOptions()).Entries.Count);
        }

        [Fact]
        public void UnsafeOrDuplicatePaths_AreRefused()
        {
            Assert.Throws<ArgumentException>(() => ManifestWriter.Format(new[] { new ManifestWriteEntry("../x", HashAlgorithmKind.Sha256, Abc) }, null));
            Assert.Throws<ArgumentException>(() => ManifestWriter.Format(new[]
            {
                new ManifestWriteEntry("A.txt", HashAlgorithmKind.Sha256, Abc),
                new ManifestWriteEntry("a.txt", HashAlgorithmKind.Sha256, Abc),
            }, null));
            Assert.Throws<ArgumentException>(() => ManifestWriter.Format(new ManifestWriteEntry[0], null));
        }

        [Fact]
        public void ExistingFile_IsNotOverwrittenSilently()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("SHA256SUMS", "original");
                var entries = new[] { new ManifestWriteEntry("a.txt", HashAlgorithmKind.Sha256, Abc) };
                Assert.Throws<IOException>(() => ManifestWriter.WriteFile(path, entries, new ManifestWriteOptions()));
                Assert.Equal("original", File.ReadAllText(path));
                ManifestWriter.WriteFile(path, entries, new ManifestWriteOptions { Overwrite = true });
                Assert.Equal(Abc + "  a.txt\n", File.ReadAllText(path));
                Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
            }
        }

        [Fact]
        public void WrittenFile_IsUtf8WithoutBom_AndLfOnly()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Combine("SHA256SUMS");
                ManifestWriter.WriteFile(path, new[] { new ManifestWriteEntry("é.txt", HashAlgorithmKind.Sha256, Abc) }, null);
                var bytes = File.ReadAllBytes(path);
                Assert.NotEqual(0xEF, bytes[0]);
                Assert.DoesNotContain((byte)'\r', bytes);
            }
        }
    }

    public class ManifestVerificationTests
    {
        private static HashWorkspace Workspace() { return new HashWorkspace(new BatchProcessor(new FileHasher(), NullLogger.Instance)); }

        /// <summary>Full integration: real files, a real manifest, verification through the workspace service.</summary>
        [Fact]
        public void EndToEnd_MatchMismatchMissingInvalidAndUnexpected()
        {
            using (var dir = new TempDir())
            {
                dir.File("good file.txt", "abc");
                dir.File("sub/文件.bin", "");
                string modified = dir.File("modified.txt", "original");
                dir.File("extra.txt", "not in manifest");
                string sha = Ref.Hash(HashAlgorithmKind.Sha256, Encoding.UTF8.GetBytes("original"));
                string manifest = dir.File("SHA256SUMS",
                    "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  good file.txt\n" +
                    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 *sub/文件.bin\n" +
                    sha + "  modified.txt\n" +
                    sha + "  gone.txt\n" +
                    sha + "  ../escape.txt\n");
                File.WriteAllText(modified, "tampered");

                var ws = Workspace();
                var loaded = ws.LoadManifestAsync(manifest, null, null, true, CancellationToken.None).Result;
                var toRun = ws.PrepareVerification(null);
                ws.RunAsync(toRun, new BatchOptions(), null, CancellationToken.None).Wait();

                Func<string, ItemStatus> status = rel => ws.Items.Single(i => i.RelativePath == rel).Status;
                Assert.Equal(ItemStatus.Match, status("good file.txt"));
                Assert.Equal(ItemStatus.Match, status("sub/文件.bin"));
                Assert.Equal(ItemStatus.Mismatch, status("modified.txt"));
                Assert.Equal(ItemStatus.Missing, status("gone.txt"));
                Assert.Equal(ItemStatus.Invalid, status("../escape.txt"));
                Assert.Equal(ItemStatus.Unexpected, status("extra.txt"));
                Assert.DoesNotContain(ws.Items, i => i.RelativePath == "SHA256SUMS"); // the manifest itself is not "unexpected"
                Assert.Equal(dir.Path, loaded.BaseDirectory);
            }
        }

        [Fact]
        public void UnexpectedFiles_NotReported_UnlessRequested()
        {
            using (var dir = new TempDir())
            {
                dir.File("a.txt", "abc");
                dir.File("extra.txt", "x");
                string manifest = dir.File("SHA256SUMS", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  a.txt\n");
                var ws = Workspace();
                ws.LoadManifestAsync(manifest, null, null, false, CancellationToken.None).Wait();
                Assert.Single(ws.Items);
            }
        }

        [Fact]
        public void JunctionInsideFolder_CannotBeUsedToReadOutside()
        {
            using (var outside = new TempDir())
            using (var dir = new TempDir())
            {
                outside.File("secret.txt", "abc");
                Assert.True(Ref.TryCreateJunction(dir.Combine("link"), outside.Path));
                string manifest = dir.File("SHA256SUMS", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  link/secret.txt\n");
                var ws = Workspace();
                ws.LoadManifestAsync(manifest, null, null, false, CancellationToken.None).Wait();
                ws.RunAsync(ws.PrepareVerification(null), null, null, CancellationToken.None).Wait();
                var item = ws.Items.Single();
                Assert.Equal(ItemStatus.Error, item.Status);
                Assert.Contains("outside the verification folder", item.Message);
                Assert.Null(item.HashHex);
            }
        }

        [Fact]
        public void PathResolution_StaysInsideBase()
        {
            string full, error;
            Assert.True(PathValidator.TryResolveManifestPath(@"C:\base", "sub/file.txt", out full, out error));
            Assert.Equal(@"C:\base\sub\file.txt", full);
            Assert.False(PathValidator.TryResolveManifestPath(@"C:\base", "../base2/file.txt", out full, out error));
            Assert.False(PathValidator.TryResolveManifestPath(@"C:\base", "sub/../../x", out full, out error));
        }
    }
}
