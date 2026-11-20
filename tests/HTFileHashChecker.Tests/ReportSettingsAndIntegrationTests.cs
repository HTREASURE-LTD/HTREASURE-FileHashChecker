using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HTFileHashChecker.Core;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Logging;
using HTFileHashChecker.Core.Processing;
using HTFileHashChecker.Core.Reports;
using HTFileHashChecker.Core.Settings;
using HTFileHashChecker.Core.Verification;
using HTFileHashChecker.Windows;
using Microsoft.Win32;
using Xunit;

namespace HTFileHashChecker.Tests
{
    public class ReportTests
    {
        private static ReportData Sample()
        {
            var match = new HashItem(@"C:\Data\<script>alert(1)</script> & co.iso", ItemSource.Manual)
            {
                Size = 3,
                HashHex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Expected = ExpectedHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", null).Value,
                Status = ItemStatus.Match,
                Message = "File checksum matches.",
                Elapsed = TimeSpan.FromMilliseconds(12),
            };
            // A legal Windows file name that a spreadsheet would treat as a formula.
            var bad = new HashItem(@"C:\Data\=SUM(A1,A2)+cmd!x.csv", ItemSource.Manual)
            {
                Algorithm = HashAlgorithmKind.Md5,
                Size = 10,
                HashHex = "900150983cd24fb0d6963f7d28e17f72",
                Expected = ExpectedHash.Parse("d41d8cd98f00b204e9800998ecf8427e", null).Value,
                Status = ItemStatus.Mismatch,
                Message = "File checksum does not match, \"quoted\"",
            };
            var err = new HashItem(@"C:\Data\locked.bin", ItemSource.Manual) { Status = ItemStatus.Error, Message = "The file is in use by another program and cannot be read." };
            return new ReportData
            {
                SelectedAlgorithm = HashAlgorithmKind.Sha256,
                Items = new[] { match, bad, err }.ToList(),
                TotalProcessingTime = TimeSpan.FromSeconds(1.5),
                GeneratedAt = new DateTimeOffset(2026, 11, 12, 10, 30, 0, TimeSpan.Zero),
            };
        }

        [Fact]
        public void Text_ContainsRequiredFields()
        {
            string t = TextReportWriter.Render(Sample());
            Assert.Contains(AppInfo.ProductName + " " + AppInfo.Version, t);
            Assert.Contains("2026-11-12 10:30:00", t);
            Assert.Contains("Selected algorithm: SHA-256", t);
            Assert.Contains(@"C:\Data\locked.bin", t);
            Assert.Contains("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", t);
            Assert.Contains("Expected:   d41d8cd98f00b204e9800998ecf8427e", t);
            Assert.Contains("MATCH", t);
            Assert.Contains("MISMATCH", t);
            Assert.Contains("in use by another program", t);
            Assert.Contains("Total files:            3", t);
            Assert.Contains("Verification failures:  1", t);
            Assert.Contains("Errors:                 1", t);
            Assert.Contains("Total processing time:  1.500 s", t);
            Assert.Contains("does not show that the file is free of malware", System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " "));
            Assert.Contains("legacy algorithms", t);
        }

        [Fact]
        public void Csv_EscapesQuotesCommasAndFormulaInjection()
        {
            string csv = CsvReportWriter.Render(Sample());
            Assert.Contains("\"File checksum does not match, \"\"quoted\"\"\"", csv);
            // First column: prefixed with an apostrophe (formula injection) and quoted (contains a comma).
            Assert.Contains("\n\"'=SUM(A1,A2)+cmd!x.csv\",", csv);
            Assert.DoesNotContain("\n=SUM", csv);
            Assert.DoesNotContain(",=SUM", csv);
            Assert.Contains(string.Join(",", CsvReportWriter.Columns), csv);
            Assert.Contains("Verification failures,1", csv);
            Assert.Equal("'-1", CsvReportWriter.Escape("-1"));
            Assert.Equal("\"a\nb\"", CsvReportWriter.Escape("a\nb"));
        }

        [Fact]
        public void Html_EncodesMarkupAndHasNoScripts()
        {
            string html = HtmlReportWriter.Render(Sample());
            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; co.iso", html);
            Assert.Contains("Content-Security-Policy", html);
            Assert.Contains("MISMATCH", html);
            Assert.Contains("Verification failures", html);
        }

        [Fact]
        public void Save_WritesFiles_AndRefusesSilentOverwrite()
        {
            using (var dir = new TempDir())
            {
                foreach (ReportFormat f in Enum.GetValues(typeof(ReportFormat)))
                {
                    string path = dir.Combine("report" + ReportService.Extension(f));
                    ReportService.Save(path, Sample(), f, false);
                    Assert.True(new FileInfo(path).Length > 200);
                    Assert.Throws<IOException>(() => ReportService.Save(path, Sample(), f, false));
                    ReportService.Save(path, Sample(), f, true);
                }
                var csv = File.ReadAllBytes(dir.Combine("report.csv"));
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csv.Take(3).ToArray()); // BOM for Excel
            }
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void SaveAndLoad_RoundTripsAllPreferences()
        {
            using (var dir = new TempDir())
            {
                var store = new SettingsStore(dir.Combine("sub\\settings.xml"));
                var s = new AppSettings
                {
                    DefaultAlgorithm = HashAlgorithmKind.Sha512,
                    LastOutputFolder = @"C:\Reports",
                    Theme = ThemePreference.Dark,
                    WindowLeft = 10, WindowTop = 20, WindowWidth = 900, WindowHeight = 700, WindowState = "Maximized",
                    DefaultReportFormat = ReportFormat.Csv,
                    OpenReportAfterSaving = true,
                    ExplorerIntegrationEnabled = true,
                    IncludeSubfolders = false,
                    MaxParallelFiles = 2,
                };
                store.Save(s);
                var loaded = new SettingsStore(store.FilePath).Load();
                Assert.Equal(HashAlgorithmKind.Sha512, loaded.DefaultAlgorithm);
                Assert.Equal(@"C:\Reports", loaded.LastOutputFolder);
                Assert.Equal(ThemePreference.Dark, loaded.Theme);
                Assert.Equal(900, loaded.WindowWidth);
                Assert.Equal("Maximized", loaded.WindowState);
                Assert.Equal(ReportFormat.Csv, loaded.DefaultReportFormat);
                Assert.True(loaded.OpenReportAfterSaving);
                Assert.True(loaded.ExplorerIntegrationEnabled);
                Assert.False(loaded.IncludeSubfolders);
                Assert.Equal(2, loaded.MaxParallelFiles);
            }
        }

        [Fact]
        public void MissingFile_GivesDefaults()
        {
            using (var dir = new TempDir())
            {
                var s = new SettingsStore(dir.Combine("none.xml")).Load();
                Assert.Equal(HashAlgorithmKind.Sha256, s.DefaultAlgorithm);
                Assert.Equal(ThemePreference.Light, s.Theme);
                Assert.True(s.IncludeSubfolders);
                Assert.False(s.ExplorerIntegrationEnabled);
            }
        }

        [Fact]
        public void CorruptFile_IsBackedUpAndDefaultsUsed()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("settings.xml", "<Settings><DefaultAlgorithm>Sha5");
                var store = new SettingsStore(path);
                var s = store.Load();
                Assert.NotNull(store.LoadWarning);
                Assert.Equal(HashAlgorithmKind.Sha256, s.DefaultAlgorithm);
                Assert.True(File.Exists(path + ".corrupt"));
            }
        }

        [Fact]
        public void OutOfRangeValues_AreNormalised_AndUnknownElementsIgnored()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("settings.xml",
                    "<?xml version=\"1.0\"?><Settings><SchemaVersion>99</SchemaVersion><MaxParallelFiles>64</MaxParallelFiles>" +
                    "<LogRetentionDays>0</LogRetentionDays><WindowState>Weird</WindowState><FutureOption>1</FutureOption>" +
                    "<LastOutputFolder>relative\\path</LastOutputFolder></Settings>");
                var s = new SettingsStore(path).Load();
                Assert.Equal(4, s.MaxParallelFiles);
                Assert.Equal(14, s.LogRetentionDays);
                Assert.Equal("Normal", s.WindowState);
                Assert.Null(s.LastOutputFolder);
            }
        }

        [Fact]
        public void DtdIsRejected()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("settings.xml", "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><Settings><LastOutputFolder>&e;</LastOutputFolder></Settings>");
                var store = new SettingsStore(path);
                var s = store.Load();
                Assert.NotNull(store.LoadWarning);
                Assert.Null(s.LastOutputFolder);
            }
        }

        [Fact]
        public void PortableMarker_SelectsLocalDataFolder()
        {
            using (var dir = new TempDir())
            {
                Assert.False(AppPaths.Detect(dir.Path).IsPortable);
                dir.File(AppInfo.PortableMarkerFileName, "");
                var p = AppPaths.Detect(dir.Path);
                Assert.True(p.IsPortable);
                Assert.Equal(Path.Combine(dir.Path, "Data", "settings.xml"), p.SettingsFile);
            }
        }

        [Fact]
        public void FileLogger_WritesAndDeletesOldLogs()
        {
            using (var dir = new TempDir())
            {
                string old = dir.File("HTFileHashChecker-20000101.log", "old");
                File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
                var logger = new FileLogger(dir.Path, 14);
                logger.Info("hello\nworld");
                Assert.False(File.Exists(old));
                string text = File.ReadAllText(logger.CurrentLogFile);
                Assert.Contains("hello world", text);
            }
        }
    }

    /// <summary>Uses a private scratch key under HKEY_CURRENT_USER so the real Explorer menu is never touched.</summary>
    public class ExplorerIntegrationTests : IDisposable
    {
        private readonly string scratch = @"Software\HTREASURE LTD\HTFileHashChecker.Tests\" + Guid.NewGuid().ToString("N");
        private readonly RegistryKey user;
        private readonly RegistryKey machine;

        public ExplorerIntegrationTests()
        {
            user = Registry.CurrentUser.CreateSubKey(scratch + @"\user");
            machine = Registry.CurrentUser.CreateSubKey(scratch + @"\machine");
        }

        public void Dispose()
        {
            user.Dispose();
            machine.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(scratch, false);
        }

        private const string Exe = @"C:\Program Files\HTREASURE LTD\HT File Hash Checker\HTFileHashChecker.exe";

        [Fact]
        public void Register_CreatesFileAndFolderVerbs_WithQuotedCommand()
        {
            var ei = new ExplorerIntegration(user, machine, "Classes");
            ei.Register(Exe);
            foreach (var target in new[] { @"Classes\*\shell\" + ExplorerIntegration.VerbName, @"Classes\Directory\shell\" + ExplorerIntegration.VerbName })
            {
                using (var verb = user.OpenSubKey(target))
                using (var cmd = user.OpenSubKey(target + @"\command"))
                {
                    Assert.Equal(ExplorerIntegration.MenuText, verb.GetValue(""));
                    Assert.Equal("Player", verb.GetValue("MultiSelectModel"));
                    Assert.Equal("\"" + Exe + "\" --calculate \"%1\"", cmd.GetValue(""));
                }
            }
            var state = ei.GetState(Exe);
            Assert.True(state.Enabled);
            Assert.True(state.PointsToThisExecutable);
        }

        [Fact]
        public void Unregister_RemovesEverything()
        {
            var ei = new ExplorerIntegration(user, machine, "Classes");
            ei.Register(Exe);
            ei.Unregister();
            Assert.Null(user.OpenSubKey(@"Classes\*\shell\" + ExplorerIntegration.VerbName));
            Assert.Null(user.OpenSubKey(@"Classes\Directory\shell\" + ExplorerIntegration.VerbName));
            Assert.False(ei.GetState(Exe).Enabled);
        }

        [Fact]
        public void Unregister_HidesAllUsersEntryForThisUser()
        {
            using (var cmd = machine.CreateSubKey(@"Classes\*\shell\" + ExplorerIntegration.VerbName + @"\command")) cmd.SetValue("", ExplorerIntegration.BuildCommand(Exe));
            var ei = new ExplorerIntegration(user, machine, "Classes");
            Assert.True(ei.GetState(Exe).Enabled);
            ei.Unregister();
            var state = ei.GetState(Exe);
            Assert.False(state.Enabled);
            Assert.True(state.HiddenForUser);
            ei.Register(Exe); // turning it on again clears the hide flag
            Assert.True(ei.GetState(Exe).Enabled);
            ei.RemoveUserEntries();
            Assert.Null(user.OpenSubKey(@"Classes\*\shell\" + ExplorerIntegration.VerbName));
        }

        [Theory]
        [InlineData("relative.exe")]
        [InlineData("C:\\bad\"quote.exe")]
        [InlineData("C:\\%PATH%\\x.exe")]
        public void UnsafeExecutablePaths_AreRefused(string exe)
        {
            Assert.Throws<ArgumentException>(() => new ExplorerIntegration(user, machine, "Classes").Register(exe));
        }
    }

    public class CommandLineAndInstanceTests
    {
        [Fact]
        public void ParsesSwitchesAndPaths()
        {
            var c = CommandLine.Parse(new[] { "--calculate", @"C:\a b\file.iso", "/selftest", @"C:\out.txt", @"\\server\share\x", "--bogus" });
            Assert.True(c.Calculate);
            Assert.Equal(@"C:\out.txt", c.SelfTestOutput);
            Assert.Equal(new[] { @"C:\a b\file.iso", @"\\server\share\x" }, c.Paths);
            Assert.Equal(new[] { "--bogus" }, c.Unknown);
        }

        [Fact]
        public void PipeMessage_RoundTripsUnicodeArguments()
        {
            var args = new[] { "--calculate", @"C:\données\文件 ✓.txt", "" };
            using (var ms = new MemoryStream(SingleInstance.Encode(args)))
                Assert.Equal(args, SingleInstance.ReadMessage(ms));
        }

        [Fact]
        public void PipeMessage_RejectsOversizedOrTruncatedData()
        {
            using (var ms = new MemoryStream(BitConverter.GetBytes(int.MaxValue)))
                Assert.Throws<InvalidDataException>(() => SingleInstance.ReadMessage(ms));
            using (var ms = new MemoryStream(BitConverter.GetBytes(10).Concat(new byte[] { 1, 2 }).ToArray()))
                Assert.Throws<InvalidDataException>(() => SingleInstance.ReadMessage(ms));
        }

        [Fact]
        public void SecondInstance_ForwardsArgumentsToFirst()
        {
            string id = "HTFHC.Test." + Guid.NewGuid().ToString("N");
            using (var first = new SingleInstance(id))
            using (var received = new ManualResetEventSlim())
            {
                Assert.True(first.TryBecomePrimary());
                string[] got = null;
                first.StartListening(a => { got = a; received.Set(); });
                using (var second = new SingleInstance(id))
                {
                    Assert.False(second.TryBecomePrimary());
                    Assert.True(second.SendToPrimary(new[] { "--calculate", @"C:\x.iso" }, 5000));
                }
                Assert.True(received.Wait(5000));
                Assert.Equal(new[] { "--calculate", @"C:\x.iso" }, got);
            }
        }
    }

    public class WorkspaceAndScannerTests
    {
        private static HashWorkspace Workspace() { return new HashWorkspace(new BatchProcessor(new FileHasher(), NullLogger.Instance)); }

        [Fact]
        public void Scanner_IsRecursiveSortedAndSkipsJunctions()
        {
            using (var outside = new TempDir())
            using (var dir = new TempDir())
            {
                outside.File("o.txt", "x");
                dir.File("b.txt", "1");
                dir.File("A.txt", "2");
                dir.File("sub/c.txt", "3");
                Assert.True(Ref.TryCreateJunction(dir.Combine("loop"), dir.Path));
                Assert.True(Ref.TryCreateJunction(dir.Combine("out"), outside.Path));
                var r = FileScanner.Scan(dir.Path, new ScanOptions(), CancellationToken.None);
                Assert.Equal(new[] { "A.txt", "b.txt", "sub/c.txt" }, r.Files.Select(f => f.RelativePath));
                Assert.Equal(2, r.Issues.Count(i => i.Reason.Contains("linked folder")));
                var flat = FileScanner.Scan(dir.Path, new ScanOptions { Recursive = false }, CancellationToken.None);
                Assert.Equal(2, flat.Files.Count);
            }
        }

        [Fact]
        public void Scanner_RespectsDepthAndFileLimits()
        {
            using (var dir = new TempDir())
            {
                dir.File("a/b/c/deep.txt", "x");
                for (int i = 0; i < 5; i++) dir.File("f" + i, "x");
                var depth = FileScanner.Scan(dir.Path, new ScanOptions { MaxDepth = 2 }, CancellationToken.None);
                Assert.DoesNotContain(depth.Files, f => f.RelativePath.EndsWith("deep.txt"));
                var limited = FileScanner.Scan(dir.Path, new ScanOptions { MaxFiles = 3 }, CancellationToken.None);
                Assert.True(limited.LimitReached);
                Assert.Equal(3, limited.Files.Count);
            }
        }

        [Fact]
        public void AddPaths_DeduplicatesAndReportsProblems()
        {
            using (var dir = new TempDir())
            {
                string a = dir.File("a.txt", "abc");
                dir.File("sub/b.txt", "b");
                var ws = Workspace();
                var r1 = ws.AddPathsAsync(new[] { a, dir.Path, dir.Combine("missing.txt"), "relative.txt" }, new AddOptions { Recursive = true }, CancellationToken.None).Result;
                Assert.Equal(2, r1.Added.Count);
                Assert.Equal(1, r1.Duplicates);
                Assert.Equal(2, r1.Issues.Count(i => i.IsError));
                var r2 = ws.AddPathsAsync(new[] { a }, null, CancellationToken.None).Result;
                Assert.Empty(r2.Added);
                Assert.Equal(1, r2.Duplicates);
            }
        }

        [Fact]
        public void CalculateThenVerifyWithPastedHash_EndToEnd()
        {
            using (var dir = new TempDir())
            {
                string a = dir.File("download.iso", "abc");
                var ws = Workspace();
                ws.AddPathsAsync(new[] { a }, null, CancellationToken.None).Wait();
                var calc = ws.PrepareCalculation(HashAlgorithmKind.Sha256, null, false);
                ws.RunAsync(calc, null, null, CancellationToken.None).Wait();
                Assert.Equal(ItemStatus.Hashed, ws.Items[0].Status);
                Assert.Empty(ws.PrepareCalculation(HashAlgorithmKind.Sha256, null, false)); // not re-read
                Assert.Single(ws.PrepareCalculation(HashAlgorithmKind.Sha256, null, true));

                // Pasting an MD5 value switches that file to MD5 automatically.
                Assert.True(ws.SetExpected(ws.Items, " 90015098 3CD24FB0 D6963F7D 28E17F72 ").IsValid);
                ws.RunAsync(ws.PrepareVerification(null), null, null, CancellationToken.None).Wait();
                Assert.Equal(ItemStatus.Match, ws.Items[0].Status);
                Assert.Equal(HashAlgorithmKind.Md5, ws.Items[0].Algorithm);

                Assert.False(ws.SetExpected(ws.Items, "not a hash").IsValid);
                ws.SetExpected(ws.Items, new string('0', 64));
                ws.RunAsync(ws.PrepareVerification(null), null, null, CancellationToken.None).Wait();
                Assert.Equal(ItemStatus.Mismatch, ws.Items[0].Status);

                var report = ws.BuildReport(HashAlgorithmKind.Sha256, null);
                Assert.Equal(1, report.VerificationFailures);
            }
        }

        [Fact]
        public void ManifestExport_UsesPathsRelativeToManifestFolder()
        {
            using (var dir = new TempDir())
            {
                string a = dir.File("x/a.txt", "abc");
                string b = dir.File("b.txt", "");
                var ws = Workspace();
                ws.AddPathsAsync(new[] { a, b }, null, CancellationToken.None).Wait();
                ws.RunAsync(ws.PrepareCalculation(HashAlgorithmKind.Sha256, null, false), null, null, CancellationToken.None).Wait();
                var plan = ws.PrepareManifestExport(dir.Combine("SHA256SUMS"), null);
                Assert.Empty(plan.OutsideFolder);
                Assert.False(plan.MixedAlgorithms);
                string text = Core.Manifests.ManifestWriter.Format(plan.Entries, null);
                Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  b.txt\n"
                    + "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  x/a.txt\n", text);
                var inner = ws.PrepareManifestExport(dir.Combine("x\\SHA256SUMS"), null);
                Assert.Single(inner.OutsideFolder);
            }
        }
    }
}
