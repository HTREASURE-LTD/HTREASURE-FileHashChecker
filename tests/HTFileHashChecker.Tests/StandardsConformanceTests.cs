using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HTREASURE.Branding;
using HTREASURE.Standards;
using HTREASURE.Standards.Security;
using Xunit;

namespace HTFileHashChecker.Tests
{
    /// <summary>
    /// HTREASURE standards conformance for the .NET Framework port, using the shared data in
    /// ht-software-standards/security/test-fixtures (copied to StandardsFixtures\ by the test project).
    /// </summary>
    public class StandardsConformanceTests
    {
        private static string Fixtures { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StandardsFixtures"); } }

        private static Dictionary<string, SecurityAdvisory> LoadFixtureAdvisories()
        {
            var result = new Dictionary<string, SecurityAdvisory>();
            foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "advisories"), "*.json"))
            {
                var a = SecurityAdvisory.FromJson(JsonObj.Parse(File.ReadAllBytes(file), 1024 * 1024));
                result.Add(a.Id, a);
            }
            return result;
        }

        public static IEnumerable<object[]> ExpectedStatusCases()
        {
            var root = JsonObj.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "expected-statuses.json")), 1024 * 1024);
            root.Str("description", false);
            foreach (var c in root.Objs("cases"))
            {
                var inst = c.Obj("installed");
                yield return new object[]
                {
                    c.Str("name"), c.Str("advisory"),
                    inst.Str("appId"), inst.Str("version"), inst.Str("channel"), inst.Str("platform"), inst.Str("buildId", false),
                    c.Str("expected"),
                };
            }
        }

        [Theory]
        [MemberData(nameof(ExpectedStatusCases))]
        public void ExpectedStatuses_MatchTheSharedConformanceTable(string name, string advisoryId, string appId, string version,
            string channel, string platform, string buildId, string expected)
        {
            var advisory = LoadFixtureAdvisories()[advisoryId];
            var app = new InstalledApplication(appId, "HT Fixture App", version, channel, platform, buildId);
            var result = AdvisoryStatusEngine.Assess(advisory, app);
            Assert.True(expected == result.Status.ToString(), name + ": expected " + expected + " but got " + result.Status + " (" + result.Reason + ")");
        }

        [Fact]
        public void ConformanceTable_HasTheDocumentedNumberOfCases()
        {
            Assert.Equal(21, ExpectedStatusCases().Count());
        }

        [Fact]
        public void ValidFixtures_PassValidation_InvalidFixturesAreRejected()
        {
            foreach (var a in LoadFixtureAdvisories().Values)
                Assert.Empty(AdvisoryValidator.Validate(a));
            foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "invalid"), "*.json"))
            {
                bool rejected;
                try { rejected = AdvisoryValidator.Validate(SecurityAdvisory.FromJson(JsonObj.Parse(File.ReadAllBytes(file), 1024 * 1024))).Count > 0; }
                catch (InvalidDataException) { rejected = true; } // unknown fields / missing required members
                Assert.True(rejected, Path.GetFileName(file) + " must be rejected");
            }
        }

        // ---------------------------------------------------------------- signatures (throwaway key, as in the standard's own tests)

        private static readonly byte[] SpkiPrefix =
        {
            0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A,
            0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04,
        };

        private sealed class TestSigner : IDisposable
        {
            private readonly CngKey key = CngKey.Create(CngAlgorithm.ECDsaP256, null, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport });

            public string Spki
            {
                get
                {
                    var blob = key.Export(CngKeyBlobFormat.EccPublicBlob);
                    return Convert.ToBase64String(SpkiPrefix.Concat(blob.Skip(8).Take(64)).ToArray());
                }
            }

            public byte[] Sign(byte[] content, string keyId)
            {
                using (var ecdsa = new ECDsaCng(key) { HashAlgorithm = CngAlgorithm.Sha256 })
                {
                    var sig = Convert.ToBase64String(ecdsa.SignData(content));
                    return Encoding.UTF8.GetBytes("{\"algorithm\":\"ECDSA-P256-SHA256\",\"keyId\":\"" + keyId + "\",\"signature\":\"" + sig + "\"}");
                }
            }

            public void Dispose() { key.Dispose(); }
        }

        private static byte[] Snapshot(string appId, long sequence, string kind = "bundled-snapshot", string advisories = "[]")
        {
            return Encoding.UTF8.GetBytes("{\"feedType\":\"htreasure-security-advisories\",\"schemaVersion\":\"1.0.0\",\"kind\":\"" + kind
                + "\",\"sequence\":" + sequence + ",\"generatedAt\":\"2026-11-20T12:00:00Z\",\"scope\":\"" + appId + "\",\"advisories\":" + advisories + "}");
        }

        [Fact]
        public void Signatures_ValidTamperedUnknownKeyAndMissing()
        {
            using (var signer = new TestSigner())
            using (var other = new TestSigner())
            {
                var keys = new TrustedKeySet(new[] { new KeyValuePair<string, string>("test-key", signer.Spki) });
                var content = Snapshot("ht-file-hash-checker", 3);
                var sig = signer.Sign(content, "test-key");
                Assert.Equal(SignatureCheck.Valid, FeedSignature.Verify(content, sig, keys));

                var tampered = (byte[])content.Clone();
                tampered[tampered.Length - 3] ^= 0x01;
                Assert.Equal(SignatureCheck.Invalid, FeedSignature.Verify(tampered, sig, keys));
                Assert.Equal(SignatureCheck.UnknownKey, FeedSignature.Verify(content, other.Sign(content, "other-key"), keys));
                Assert.Equal(SignatureCheck.Invalid, FeedSignature.Verify(content, other.Sign(content, "test-key"), keys));
                Assert.Equal(SignatureCheck.Missing, FeedSignature.Verify(content, null, keys));
                Assert.Equal(SignatureCheck.Malformed, FeedSignature.Verify(content, Encoding.UTF8.GetBytes("{\"algorithm\":\"RSA\"}"), keys));
            }
        }

        [Fact]
        public void ProductionTrustedKeys_AreValidP256Keys()
        {
            // The build embeds ht-software-standards/security/keys/trusted-keys.json into the executable.
            using (var stream = typeof(HTFileHashChecker.UI.MainForm).Assembly.GetManifestResourceStream("HTREASURE.Standards.trusted-keys.json"))
            {
                Assert.NotNull(stream);
                Assert.True(TrustedKeySet.Load(stream).Count >= 1);
            }
        }

        [Fact]
        public void AdvisoryCenter_ShowsSignedSnapshotAsHistorical_AndRejectsUnsignedInReleaseMode()
        {
            using (var signer = new TestSigner())
            using (var dir = new TempDir())
            {
                var keys = new TrustedKeySet(new[] { new KeyValuePair<string, string>("test-key", signer.Spki) });
                var app = new InstalledApplication("ht-file-hash-checker", "HT File Hash Checker", "1.0.0", "stable", "windows-x64");
                var content = Snapshot(app.AppId, 1);
                dir.File("bundle/advisory-snapshot.json", content);

                // Release behaviour: an unsigned snapshot is not trusted.
                var strict = new SecurityAdvisoryCenter(app, new SecurityAdvisoryOptions { BundledDirectory = dir.Combine("bundle"), CacheDirectory = dir.Combine("cache"), TrustedKeys = keys });
                var none = strict.LoadOffline();
                Assert.Equal(AdvisorySource.None, none.Source);
                Assert.Equal("Unknown", none.OverallAssessment);

                dir.File("bundle/advisory-snapshot.json.sig.json", signer.Sign(content, "test-key"));
                var view = strict.LoadOffline();
                Assert.Equal(AdvisorySource.BundledSnapshot, view.Source);
                Assert.True(view.IsHistorical);
                Assert.Empty(view.Assessments);
                // Never claims the software is secure.
                Assert.Equal("No known applicable advisories", view.OverallAssessment);
                Assert.Contains("not proof", SecurityAdvisoryView.PolicyStatement);

                // Without a provisioned service the check reports "not configured" and keeps the verified data.
                var sync = strict.SyncAsync(System.Threading.CancellationToken.None).Result;
                Assert.False(sync.Success);
                Assert.Contains("not configured", sync.Message);
                Assert.Equal(AdvisorySource.BundledSnapshot, sync.View.Source);

                // Snapshot for another application is refused.
                var otherApp = Snapshot("ht-iso-ripper", 1);
                dir.File("bundle/advisory-snapshot.json", otherApp);
                dir.File("bundle/advisory-snapshot.json.sig.json", signer.Sign(otherApp, "test-key"));
                Assert.Equal(AdvisorySource.None, strict.LoadOffline().Source);
            }
        }

        [Fact]
        public void AdvisoryCenter_AppliesStatusEngineToSnapshotContents()
        {
            using (var dir = new TempDir())
            {
                string advisory = File.ReadAllText(Path.Combine(Fixtures, "advisories", "HTSA-0000-0001.json"));
                var content = Snapshot("ht-fixture-app", 2, "test-fixture", "[" + advisory + "]");
                dir.File("b/advisory-snapshot.json", content);
                var app = new InstalledApplication("ht-fixture-app", "HT Fixture App", "1.1.5", "stable", "windows-x64");
                var center = new SecurityAdvisoryCenter(app, new SecurityAdvisoryOptions
                {
                    BundledDirectory = dir.Combine("b"), CacheDirectory = dir.Combine("c"),
                    TrustedKeys = new TrustedKeySet(new KeyValuePair<string, string>[0]),
                    AllowUnsignedBundledSnapshot = true, AllowTestFixtures = true,
                });
                var view = center.LoadOffline();
                Assert.Equal(AdvisorySource.TestFixture, view.Source);
                Assert.Equal(AdvisoryStatus.Affected, view.Assessments.Single().Status);
                Assert.Equal("Action required", view.OverallAssessment);
                Assert.Contains(view.Warnings, w => w.Contains("not signed"));

                string md = SecurityReport.ToMarkdown(view, DateTimeOffset.Now);
                Assert.Contains("HTSA-0000-0001", md);
                Assert.Contains("Historical data", md);
                string json = SecurityReport.ToJson(view, DateTimeOffset.Now);
                Assert.Contains("\"reportType\": \"htreasure-security-advisory-report\"", json);
            }
        }

        [Fact]
        public void UnknownFeedMembers_AreRejected()
        {
            var content = Encoding.UTF8.GetBytes("{\"kind\":\"bundled-snapshot\",\"sequence\":1,\"generatedAt\":\"2026-11-20T12:00:00Z\",\"scope\":\"x\",\"advisories\":[],\"extra\":1}");
            var r = FeedReader.Read(content, null, new TrustedKeySet(new KeyValuePair<string, string>[0]), new[] { FeedKinds.BundledSnapshot }, DateTimeOffset.UtcNow, false, false);
            Assert.Equal(FeedProblem.Malformed, r.Problem);
        }

        [Fact]
        public void SemVersion_FollowsSemVer2Precedence()
        {
            Func<string, SemVersion> v = s => { SemVersion x; Assert.True(SemVersion.TryParse(s, out x)); return x; };
            Assert.True(v("1.0.0-alpha").CompareTo(v("1.0.0-alpha.1")) < 0);
            Assert.True(v("1.0.0-alpha.1").CompareTo(v("1.0.0-alpha.beta")) < 0);
            Assert.True(v("1.0.0-rc.1").CompareTo(v("1.0.0")) < 0);
            Assert.Equal(0, v("1.0.0+build.5").CompareTo(v("1.0.0")));
            SemVersion bad;
            Assert.False(SemVersion.TryParse("01.0.0", out bad));
        }
    }

    public class BuildIntegrationTests
    {
        private static System.Reflection.Assembly App { get { return typeof(HTFileHashChecker.UI.MainForm).Assembly; } }

        [Fact]
        public void StandardsTargets_StampedTheApplication()
        {
            var runtime = HTStandardsRuntime.FromAssembly(App, "HT File Hash Checker", AppDomain.CurrentDomain.BaseDirectory);
            Assert.Equal("ht-file-hash-checker", runtime.Application.AppId);
            Assert.Equal("stable", runtime.Application.Channel);
            Assert.Equal("1.0.0", runtime.Application.Version);
            Assert.True(runtime.TrustedKeys.Count >= 1, "trusted keys are embedded in the executable");
            Assert.Null(runtime.AdvisoryServiceUrl);
            Assert.StartsWith("windows-", runtime.Application.Platform);
        }

        [Fact]
        public void BuildOutput_ContainsSnapshotAndCompatibilityManifest()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            Assert.True(File.Exists(Path.Combine(dir, "security", "advisory-snapshot.json")), "advisory snapshot is copied next to the executable");
            string manifest = File.ReadAllText(Path.Combine(dir, "ht-compatibility.json"));
            Assert.Contains("\"appId\": \"ht-file-hash-checker\"", manifest);
            Assert.Contains("\"advisoryServiceEnabled\": false", manifest);
        }

        [Fact]
        public void Branding_ComesFromTheOfficialKit()
        {
            Assert.Equal("HTREASURE LTD", BrandInfo.CompanyName);
            Assert.Equal("17178941", BrandInfo.RegistrationNumber);
            Assert.Equal("An HTREASURE LTD product", BrandInfo.Attribution);
            Assert.False(BrandInfo.IsCodeSigningConfigured);
            Assert.True(BrandAssets.Exists(BrandAssets.CorporateIcon));
            Assert.True(BrandAssets.Exists(BrandAssets.InterRegular));
            var copyright = (System.Reflection.AssemblyCopyrightAttribute)Attribute.GetCustomAttribute(App, typeof(System.Reflection.AssemblyCopyrightAttribute));
            Assert.Matches(@"^Copyright © 20\d\d HTREASURE LTD\. All rights reserved\.$", copyright.Copyright);
            var company = (System.Reflection.AssemblyCompanyAttribute)Attribute.GetCustomAttribute(App, typeof(System.Reflection.AssemblyCompanyAttribute));
            Assert.Equal("HTREASURE LTD", company.Company);
        }
    }
}
