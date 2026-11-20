using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HTREASURE.Standards.Security
{
    /// <summary>Trusted public keys (security/keys/trusted-keys.json), embedded in the application at build time.</summary>
    public sealed class TrustedKeySet
    {
        private readonly Dictionary<string, byte[]> keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>DER prefix of a P-256 SubjectPublicKeyInfo (id-ecPublicKey, prime256v1), followed by the
        /// uncompressed point 04 || X || Y.</summary>
        private static readonly byte[] P256SpkiPrefix =
        {
            0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A,
            0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04,
        };

        public TrustedKeySet(IEnumerable<KeyValuePair<string, string>> keyIdAndSpkiBase64)
        {
            foreach (var k in keyIdAndSpkiBase64)
            {
                byte[] spki = Convert.FromBase64String(k.Value);
                keys[k.Key] = ToEccPublicBlob(spki, k.Key);
            }
        }

        public int Count { get { return keys.Count; } }

        public static TrustedKeySet Load(Stream json)
        {
            byte[] bytes;
            using (var ms = new MemoryStream()) { json.CopyTo(ms); bytes = ms.ToArray(); }
            var o = JsonObj.Parse(bytes, 1024 * 1024);
            o.Str("description", false);
            var list = o.Objs("keys").Select(k =>
            {
                var pair = new KeyValuePair<string, string>(k.Str("keyId"), k.Str("publicKeySpki"));
                k.RejectUnknown();
                return pair;
            }).ToList();
            o.RejectUnknown();
            return new TrustedKeySet(list);
        }

        internal bool TryGet(string keyId, out byte[] eccPublicBlob) { return keys.TryGetValue(keyId, out eccPublicBlob); }

        /// <summary>Converts a P-256 SubjectPublicKeyInfo into a CNG BCRYPT_ECCPUBLIC_BLOB (works on Windows 7 and later).</summary>
        internal static byte[] ToEccPublicBlob(byte[] spki, string keyId)
        {
            if (spki.Length != P256SpkiPrefix.Length + 64 || !spki.Take(P256SpkiPrefix.Length).SequenceEqual(P256SpkiPrefix))
                throw new CryptographicException("Key " + keyId + " is not a P-256 public key.");
            var blob = new byte[8 + 64];
            BitConverter.GetBytes(0x31534345).CopyTo(blob, 0); // BCRYPT_ECDSA_PUBLIC_P256_MAGIC "ECS1"
            BitConverter.GetBytes(32).CopyTo(blob, 4);
            Buffer.BlockCopy(spki, P256SpkiPrefix.Length, blob, 8, 64);
            return blob;
        }
    }

    public enum SignatureCheck { Valid, Missing, UnknownKey, Invalid, Malformed }

    /// <summary>ECDSA P-256 / SHA-256 detached signatures over exact file bytes (IEEE P1363 r||s, base64).</summary>
    public static class FeedSignature
    {
        public const string Algorithm = "ECDSA-P256-SHA256";

        public static SignatureCheck Verify(byte[] content, byte[] signatureJson, TrustedKeySet trusted)
        {
            if (signatureJson == null || signatureJson.Length == 0) return SignatureCheck.Missing;
            string algorithm, keyId, signature;
            try
            {
                var o = JsonObj.Parse(signatureJson, 64 * 1024);
                algorithm = o.Str("algorithm");
                keyId = o.Str("keyId");
                signature = o.Str("signature");
                o.RejectUnknown();
            }
            catch (InvalidDataException) { return SignatureCheck.Malformed; }
            if (algorithm != Algorithm || string.IsNullOrEmpty(keyId)) return SignatureCheck.Malformed;
            byte[] blob;
            if (!trusted.TryGet(keyId, out blob)) return SignatureCheck.UnknownKey;
            byte[] raw;
            try { raw = Convert.FromBase64String(signature); }
            catch (FormatException) { return SignatureCheck.Malformed; }
            if (raw.Length != 64) return SignatureCheck.Malformed;
            try
            {
                using (var key = CngKey.Import(blob, CngKeyBlobFormat.EccPublicBlob))
                using (var ecdsa = new ECDsaCng(key) { HashAlgorithm = CngAlgorithm.Sha256 })
                {
                    // CNG uses the same r||s (IEEE P1363) encoding as the standard.
                    return ecdsa.VerifyData(content, raw) ? SignatureCheck.Valid : SignatureCheck.Invalid;
                }
            }
            catch (CryptographicException) { return SignatureCheck.Invalid; }
        }
    }

    public enum FeedProblem { None, SignatureMissing, SignatureInvalid, UnknownSigningKey, Malformed, SchemaInvalid, Expired, WrongKind, TooLarge }

    public sealed class FeedReadResult
    {
        public FeedReadResult(AdvisoryFeed feed, FeedProblem problem, string message, string contentHash)
        {
            Feed = feed; Problem = problem; Message = message; ContentHash = contentHash;
        }

        public AdvisoryFeed Feed { get; private set; }
        public FeedProblem Problem { get; private set; }
        public string Message { get; private set; }
        public string ContentHash { get; private set; }
        public bool Ok { get { return Problem == FeedProblem.None && Feed != null; } }
    }

    /// <summary>Authenticate first, then parse, then validate. Advisory data is never executed, opened or installed.</summary>
    public static class FeedReader
    {
        public const int MaxFeedBytes = 5 * 1024 * 1024;

        public static FeedReadResult Read(byte[] content, byte[] signatureJson, TrustedKeySet trusted, ICollection<string> allowedKinds,
            DateTimeOffset now, bool requireSignature = true, bool enforceExpiry = true)
        {
            string hash;
            using (var sha = SHA256.Create())
                hash = string.Concat(sha.ComputeHash(content).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            Func<FeedProblem, string, FeedReadResult> fail = (p, m) => new FeedReadResult(null, p, m, hash);
            if (content.Length > MaxFeedBytes) return fail(FeedProblem.TooLarge, "The advisory data is larger than allowed.");

            if (requireSignature || signatureJson != null)
            {
                switch (FeedSignature.Verify(content, signatureJson, trusted))
                {
                    case SignatureCheck.Valid: break;
                    case SignatureCheck.Missing: return fail(FeedProblem.SignatureMissing, "The advisory data is not signed.");
                    case SignatureCheck.UnknownKey: return fail(FeedProblem.UnknownSigningKey, "The advisory data is signed by a key this application does not trust.");
                    case SignatureCheck.Malformed: return fail(FeedProblem.SignatureInvalid, "The advisory signature is malformed.");
                    default: return fail(FeedProblem.SignatureInvalid, "The advisory signature does not match; the data may have been altered.");
                }
            }

            AdvisoryFeed feed;
            try { feed = AdvisoryFeed.Parse(content, MaxFeedBytes); }
            catch (InvalidDataException ex) { return fail(FeedProblem.Malformed, "The advisory data is not valid: " + ex.Message); }

            var errors = AdvisoryValidator.Validate(feed);
            if (errors.Count > 0) return fail(FeedProblem.SchemaInvalid, "The advisory data failed validation: " + string.Join(" ", errors.Take(5)));
            if (!allowedKinds.Contains(feed.Kind)) return fail(FeedProblem.WrongKind, "Unexpected advisory data kind '" + feed.Kind + "'.");
            if (enforceExpiry && feed.ExpiresAt.HasValue && feed.ExpiresAt.Value <= now)
                return fail(FeedProblem.Expired, "The advisory data expired on " + feed.ExpiresAt.Value.ToString("u", CultureInfo.InvariantCulture) + "; it may be a replay of old data.");
            return new FeedReadResult(feed, FeedProblem.None, "Verified.", hash);
        }
    }

    /// <summary>Persistent client state for rollback/replay protection and notifications. Contains no personal data.</summary>
    public sealed class AdvisorySyncState
    {
        public AdvisorySyncState() { NotifiedAdvisoryIds = new List<string>(); }
        public long HighestSequence { get; set; }
        public string HighestSequenceHash { get; set; }
        public DateTimeOffset? LastCheckAttempt { get; set; }
        public DateTimeOffset? LastSuccessfulCheck { get; set; }
        public string LastError { get; set; }
        public List<string> NotifiedAdvisoryIds { get; set; }
    }

    /// <summary>Bundled snapshot (read-only, next to the executable) and per-user cache of the last verified live feed.</summary>
    public sealed class AdvisoryStore
    {
        public const string SnapshotFile = "advisory-snapshot.json";
        public const string LiveCacheFile = "advisory-live-cache.json";
        public const string StateFile = "advisory-sync-state.json";

        public AdvisoryStore(string bundledDirectory, string cacheDirectory)
        {
            BundledDirectory = bundledDirectory;
            CacheDirectory = cacheDirectory;
        }

        public string BundledDirectory { get; private set; }
        public string CacheDirectory { get; private set; }

        public static string SignatureFileFor(string file) { return file + ".sig.json"; }

        public Tuple<byte[], byte[]> ReadBundled() { return ReadPair(BundledDirectory, SnapshotFile); }
        public Tuple<byte[], byte[]> ReadLiveCache() { return ReadPair(CacheDirectory, LiveCacheFile); }

        public void WriteLiveCache(byte[] content, byte[] signature)
        {
            Directory.CreateDirectory(CacheDirectory);
            WriteAtomic(Path.Combine(CacheDirectory, LiveCacheFile), content);
            WriteAtomic(Path.Combine(CacheDirectory, SignatureFileFor(LiveCacheFile)), signature);
        }

        public AdvisorySyncState ReadState()
        {
            string path = Path.Combine(CacheDirectory, StateFile);
            try
            {
                if (!File.Exists(path)) return new AdvisorySyncState();
                var o = JsonObj.Parse(File.ReadAllBytes(path), 1024 * 1024);
                var s = new AdvisorySyncState
                {
                    HighestSequence = o.Has("highestSequence") ? o.Long("highestSequence") : 0,
                    HighestSequenceHash = o.Str("highestSequenceHash", false),
                    LastCheckAttempt = o.Date("lastCheckAttempt", false),
                    LastSuccessfulCheck = o.Date("lastSuccessfulCheck", false),
                    LastError = o.Str("lastError", false),
                    NotifiedAdvisoryIds = o.Strs("notifiedAdvisoryIds", false) ?? new List<string>(),
                };
                o.RejectUnknown();
                return s;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return new AdvisorySyncState { LastError = "Local advisory state was damaged and has been reset." };
            }
        }

        public void WriteState(AdvisorySyncState state)
        {
            Directory.CreateDirectory(CacheDirectory);
            var map = new JsonMap
            {
                { "highestSequence", state.HighestSequence },
                { "highestSequenceHash", state.HighestSequenceHash },
                { "lastCheckAttempt", state.LastCheckAttempt },
                { "lastSuccessfulCheck", state.LastSuccessfulCheck },
                { "lastError", state.LastError },
                { "notifiedAdvisoryIds", state.NotifiedAdvisoryIds },
            };
            map.RemoveAll(kv => kv.Value == null);
            WriteAtomic(Path.Combine(CacheDirectory, StateFile), new UTF8Encoding(false).GetBytes(JsonOut.Serialize(map)));
        }

        private static Tuple<byte[], byte[]> ReadPair(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) return null;
            string sigPath = Path.Combine(dir, SignatureFileFor(file));
            return Tuple.Create(File.ReadAllBytes(path), File.Exists(sigPath) ? File.ReadAllBytes(sigPath) : null);
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Replace(tmp, path, null, true);
            else File.Move(tmp, path);
        }

        /// <summary>%LocalAppData%\HTREASURE\&lt;appId&gt;\security (the standard per-user cache location).</summary>
        public static string DefaultCacheDirectory(string appId)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HTREASURE", appId, "security");
        }
    }

    /// <summary>Fetches the signed live feed (content and detached signature). Not implemented in this port until a
    /// production advisory service URL exists.</summary>
    public interface IAdvisoryTransport
    {
        Task<Tuple<byte[], byte[]>> FetchAsync(CancellationToken cancellationToken);
    }

    public enum AdvisorySource { None, BundledSnapshot, LiveVerified, LiveCachedHistorical, TestFixture }

    public sealed class SecurityAdvisoryOptions
    {
        public string BundledDirectory { get; set; }
        public string CacheDirectory { get; set; }
        public TrustedKeySet TrustedKeys { get; set; }
        public IAdvisoryTransport Transport { get; set; }
        public bool AllowUnsignedBundledSnapshot { get; set; }
        public bool AllowTestFixtures { get; set; }
        public Func<DateTimeOffset> Clock { get; set; }
    }

    /// <summary>Everything the Security Advisory History screen needs.</summary>
    public sealed class SecurityAdvisoryView
    {
        public const string PolicyStatement = "A lack of known advisories is not proof that the software is free from vulnerabilities.";

        public InstalledApplication Application { get; internal set; }
        public AdvisorySource Source { get; internal set; }
        public string SourceDescription { get; internal set; }
        public DateTimeOffset? DataGeneratedAt { get; internal set; }
        public DateTimeOffset? BundledSnapshotDate { get; internal set; }
        public DateTimeOffset? LastCheckAttempt { get; internal set; }
        public DateTimeOffset? LastSuccessfulCheck { get; internal set; }
        public string LastCheckError { get; internal set; }
        public bool AdvisoryServiceConfigured { get; internal set; }
        public IList<AdvisoryAssessment> Assessments { get; internal set; }
        public IList<string> Warnings { get; internal set; }

        public bool IsHistorical { get { return Source != AdvisorySource.LiveVerified; } }

        public IEnumerable<AdvisoryAssessment> Outstanding
        {
            get { return Assessments.Where(a => a.Status == AdvisoryStatus.Affected || a.Status == AdvisoryStatus.UnderReview || a.Status == AdvisoryStatus.Unknown); }
        }

        public IEnumerable<AdvisoryAssessment> FixesIncluded { get { return Assessments.Where(a => a.Status == AdvisoryStatus.Fixed); } }

        /// <summary>Headline assessment. Never claims the software is secure or free of vulnerabilities.</summary>
        public string OverallAssessment
        {
            get
            {
                if (Source == AdvisorySource.None) return "Unknown";
                if (Assessments.Any(a => a.Status == AdvisoryStatus.Affected)) return "Action required";
                if (Assessments.Any(a => a.Status == AdvisoryStatus.UnderReview || a.Status == AdvisoryStatus.Unknown)) return "Review pending";
                return "No known applicable advisories";
            }
        }
    }

    public sealed class AdvisorySyncOutcome
    {
        public AdvisorySyncOutcome(bool success, string message, FeedProblem problem, IList<AdvisoryAssessment> newlyRelevant, SecurityAdvisoryView view)
        {
            Success = success; Message = message; Problem = problem; NewlyRelevant = newlyRelevant; View = view;
        }

        public bool Success { get; private set; }
        public string Message { get; private set; }
        public FeedProblem Problem { get; private set; }
        public IList<AdvisoryAssessment> NewlyRelevant { get; private set; }
        public SecurityAdvisoryView View { get; private set; }
    }

    /// <summary>Entry point (port of SecurityAdvisoryCenter). Works offline from the bundled snapshot; online sync
    /// authenticates, validates and rejects rollback. Synchronisation never installs software.</summary>
    public sealed class SecurityAdvisoryCenter
    {
        private static readonly string[] BundledKinds = { FeedKinds.BundledSnapshot };
        private static readonly string[] BundledKindsWithFixtures = { FeedKinds.BundledSnapshot, FeedKinds.TestFixture };
        private static readonly string[] LiveKinds = { FeedKinds.Live };

        private readonly InstalledApplication app;
        private readonly SecurityAdvisoryOptions options;
        private readonly AdvisoryStore store;

        public SecurityAdvisoryCenter(InstalledApplication app, SecurityAdvisoryOptions options)
        {
            this.app = app;
            this.options = options;
            if (options.Clock == null) options.Clock = () => DateTimeOffset.UtcNow;
            store = new AdvisoryStore(options.BundledDirectory, options.CacheDirectory ?? AdvisoryStore.DefaultCacheDirectory(app.AppId));
        }

        public InstalledApplication Application { get { return app; } }
        public bool ServiceConfigured { get { return options.Transport != null; } }

        public SecurityAdvisoryView LoadOffline()
        {
            var now = options.Clock();
            var state = store.ReadState();
            var warnings = new List<string>();
            if (state.LastError != null && state.LastSuccessfulCheck == null) warnings.Add(state.LastError);
            var bundled = ReadBundled(now, warnings);
            var live = ReadLiveCache(now, warnings);

            if (live != null && (bundled == null || live.Feed.Sequence >= bundled.Feed.Sequence))
            {
                bool fresh = live.Feed.ExpiresAt.HasValue && live.Feed.ExpiresAt.Value > now;
                return BuildView(live.Feed, fresh ? AdvisorySource.LiveVerified : AdvisorySource.LiveCachedHistorical,
                    fresh ? "HTREASURE advisory service, verified " + live.Feed.GeneratedAt.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture)
                          : "Last verified advisory data from " + live.Feed.GeneratedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) + ". It may be out of date.",
                    bundled == null ? null : bundled.Feed, state, warnings);
            }
            if (bundled != null)
            {
                bool fixture = bundled.Feed.Kind == FeedKinds.TestFixture;
                return BuildView(bundled.Feed, fixture ? AdvisorySource.TestFixture : AdvisorySource.BundledSnapshot,
                    fixture ? "TEST FIXTURE DATA - not real security information"
                            : "Advisory snapshot bundled with this release (" + bundled.Feed.GeneratedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) + ")",
                    bundled.Feed, state, warnings);
            }
            return new SecurityAdvisoryView
            {
                Application = app,
                Source = AdvisorySource.None,
                SourceDescription = "No verified advisory data is available.",
                Assessments = new List<AdvisoryAssessment>(),
                LastCheckAttempt = state.LastCheckAttempt,
                LastSuccessfulCheck = state.LastSuccessfulCheck,
                LastCheckError = state.LastError,
                AdvisoryServiceConfigured = options.Transport != null,
                Warnings = warnings,
            };
        }

        /// <summary>Checks the central advisory service. On any failure the last verified data is kept.</summary>
        public async Task<AdvisorySyncOutcome> SyncAsync(CancellationToken cancellationToken)
        {
            var now = options.Clock();
            var state = store.ReadState();
            state.LastCheckAttempt = now;
            Func<FeedProblem, string, AdvisorySyncOutcome> fail = (p, m) => new AdvisorySyncOutcome(false, m, p, new List<AdvisoryAssessment>(), LoadOffline());
            if (options.Transport == null)
            {
                state.LastError = "The advisory service is not configured for this product.";
                TryWriteState(state);
                return fail(FeedProblem.None, state.LastError);
            }

            Tuple<byte[], byte[]> fetched;
            try { fetched = await options.Transport.FetchAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is System.Net.WebException || ex is TaskCanceledException)
            {
                state.LastError = "Could not reach the advisory service. Showing the last verified data.";
                TryWriteState(state);
                return fail(FeedProblem.None, state.LastError);
            }

            var result = FeedReader.Read(fetched.Item1, fetched.Item2, options.TrustedKeys, LiveKinds, now);
            if (!result.Ok)
            {
                state.LastError = result.Message;
                TryWriteState(state);
                return fail(result.Problem, result.Message);
            }
            var feed = result.Feed;
            var floor = SequenceFloor(state, now);
            if (feed.Sequence < floor.Key || (feed.Sequence == floor.Key && floor.Value != null && floor.Value != result.ContentHash))
            {
                state.LastError = "The advisory service returned older data than this device has already verified. It was rejected as a possible rollback or replay.";
                TryWriteState(state);
                return fail(FeedProblem.Expired, state.LastError);
            }
            store.WriteLiveCache(fetched.Item1, fetched.Item2);
            var relevant = AdvisoryStatusEngine.AssessAll(feed.Advisories, app)
                .Where(a => a.Status == AdvisoryStatus.Affected || a.Status == AdvisoryStatus.UnderReview)
                .Where(a => !state.NotifiedAdvisoryIds.Contains(a.Advisory.Id)).ToList();
            state.HighestSequence = feed.Sequence;
            state.HighestSequenceHash = result.ContentHash;
            state.LastSuccessfulCheck = now;
            state.LastError = null;
            state.NotifiedAdvisoryIds = state.NotifiedAdvisoryIds.Concat(relevant.Select(r => r.Advisory.Id)).Distinct().ToList();
            TryWriteState(state);
            return new AdvisorySyncOutcome(true,
                relevant.Count == 0 ? "Advisory information is up to date." : relevant.Count + " new advisory(ies) may affect this installation.",
                FeedProblem.None, relevant, LoadOffline());
        }

        private void TryWriteState(AdvisorySyncState state)
        {
            try { store.WriteState(state); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private KeyValuePair<long, string> SequenceFloor(AdvisorySyncState state, DateTimeOffset now)
        {
            var floor = new KeyValuePair<long, string>(state.HighestSequence, state.HighestSequenceHash);
            var cached = ReadLiveCache(now, new List<string>());
            if (cached != null && cached.Feed.Sequence > floor.Key) floor = new KeyValuePair<long, string>(cached.Feed.Sequence, cached.ContentHash);
            var bundled = ReadBundled(now, new List<string>());
            if (bundled != null && bundled.Feed.Sequence > floor.Key) floor = new KeyValuePair<long, string>(bundled.Feed.Sequence, null);
            return floor;
        }

        private FeedReadResult ReadBundled(DateTimeOffset now, List<string> warnings)
        {
            Tuple<byte[], byte[]> raw;
            try { raw = store.ReadBundled(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { raw = null; }
            if (raw == null) { warnings.Add("No advisory snapshot is bundled with this installation."); return null; }
            var kinds = options.AllowTestFixtures ? BundledKindsWithFixtures : BundledKinds;
            var r = FeedReader.Read(raw.Item1, raw.Item2, options.TrustedKeys, kinds, now, !options.AllowUnsignedBundledSnapshot, false);
            if (!r.Ok) { warnings.Add("The bundled advisory snapshot could not be verified: " + r.Message); return null; }
            if (raw.Item2 == null) warnings.Add("Development build: the bundled advisory snapshot is not signed.");
            if (r.Feed.Scope != app.AppId && r.Feed.Scope != "all") { warnings.Add("The bundled advisory snapshot belongs to a different application."); return null; }
            return r;
        }

        private FeedReadResult ReadLiveCache(DateTimeOffset now, List<string> warnings)
        {
            Tuple<byte[], byte[]> raw;
            try { raw = store.ReadLiveCache(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { raw = null; }
            if (raw == null) return null;
            var r = FeedReader.Read(raw.Item1, raw.Item2, options.TrustedKeys, LiveKinds, now, true, false);
            if (!r.Ok) { warnings.Add("Cached advisory data failed verification and was ignored: " + r.Message); return null; }
            return r;
        }

        private SecurityAdvisoryView BuildView(AdvisoryFeed feed, AdvisorySource source, string description, AdvisoryFeed bundled,
            AdvisorySyncState state, List<string> warnings)
        {
            return new SecurityAdvisoryView
            {
                Application = app,
                Source = source,
                SourceDescription = description,
                DataGeneratedAt = feed.GeneratedAt,
                BundledSnapshotDate = bundled == null ? (DateTimeOffset?)null : bundled.GeneratedAt,
                LastCheckAttempt = state.LastCheckAttempt,
                LastSuccessfulCheck = state.LastSuccessfulCheck,
                LastCheckError = state.LastError,
                AdvisoryServiceConfigured = options.Transport != null,
                Assessments = AdvisoryStatusEngine.AssessAll(feed.Advisories, app),
                Warnings = warnings,
            };
        }
    }

    /// <summary>Export Security Report (Markdown or JSON). Contains no device identifiers or personal data.</summary>
    public static class SecurityReport
    {
        public static string ToMarkdown(SecurityAdvisoryView v, DateTimeOffset generatedAt)
        {
            var sb = new StringBuilder();
            Action<string, string> row = (k, val) => sb.AppendLine("| " + k + " | " + Escape(val) + " |");
            sb.AppendLine("# Security Advisory Report: " + v.Application.Name);
            sb.AppendLine();
            sb.AppendLine("| Item | Value |");
            sb.AppendLine("|---|---|");
            row("Application", v.Application.Name + " (" + v.Application.AppId + ")");
            row("Installed version", v.Application.Version + (v.Application.BuildId == null ? "" : " (build " + v.Application.BuildId + ")"));
            row("Release channel", v.Application.Channel);
            row("Platform", v.Application.Platform);
            row("HTREASURE standards version", StandardsInfo.StandardsVersion);
            row("Report generated", Fmt(generatedAt));
            row("Advisory data source", v.SourceDescription);
            row("Advisory data date", v.DataGeneratedAt.HasValue ? Fmt(v.DataGeneratedAt.Value) : "None");
            row("Last successful online check", v.LastSuccessfulCheck.HasValue ? Fmt(v.LastSuccessfulCheck.Value) : "Never");
            row("Assessment", v.OverallAssessment);
            sb.AppendLine();
            if (v.IsHistorical)
            {
                sb.AppendLine("> **Historical data.** This report is based on advisory data that is not a current, live assessment. Check for advisories online for the latest information.");
                sb.AppendLine();
            }
            Section(sb, "Applicable and outstanding advisories", v.Outstanding.ToList());
            Section(sb, "Security fixes included in this version", v.FixesIncluded.ToList());
            sb.AppendLine("## Statement");
            sb.AppendLine();
            sb.AppendLine(SecurityAdvisoryView.PolicyStatement);
            sb.AppendLine();
            sb.AppendLine("HTREASURE LTD \u00B7 https://htreasure.com \u00B7 support@htreasure.com");
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, List<AdvisoryAssessment> items)
        {
            sb.AppendLine("## " + title);
            sb.AppendLine();
            if (items.Count == 0) { sb.AppendLine("None recorded in the advisory data used for this report."); sb.AppendLine(); return; }
            sb.AppendLine("| Advisory | Severity | Title | Affected versions | Fixed version | Published | Status |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var a in items)
            {
                string ids = a.Advisory.Id + string.Concat((a.Advisory.Aliases ?? new List<AdvisoryAlias>()).Select(x => ", " + x.Id));
                sb.AppendLine("| " + Escape(ids) + " | " + Cap(a.Advisory.Severity) + " | " + Escape(a.Advisory.Title) + " | " + Escape(a.Advisory.AffectedVersionsText)
                    + " | " + Escape(a.Advisory.FixedVersionText) + " | " + a.Advisory.Published + " | " + StatusText(a.Status) + " |");
            }
            sb.AppendLine();
        }

        public static string ToJson(SecurityAdvisoryView v, DateTimeOffset generatedAt)
        {
            var app = new JsonMap { { "appId", v.Application.AppId }, { "name", v.Application.Name }, { "version", v.Application.Version },
                { "channel", v.Application.Channel }, { "platform", v.Application.Platform } };
            if (v.Application.BuildId != null) app.Add("buildId", v.Application.BuildId);
            var advisories = v.Assessments.Select(a =>
            {
                var m = new JsonMap { { "id", a.Advisory.Id } };
                if (a.Advisory.Aliases != null) m.Add("aliases", a.Advisory.Aliases.Select(x => x.Id).ToList());
                m.Add("severity", a.Advisory.Severity);
                m.Add("title", a.Advisory.Title);
                m.Add("affectedVersions", a.Advisory.AffectedVersionsText);
                if (a.Advisory.FixedVersions != null) m.Add("fixedVersions", a.Advisory.FixedVersions);
                m.Add("published", a.Advisory.Published);
                m.Add("status", a.Status.ToString());
                m.Add("reason", a.Reason);
                return (object)m;
            }).ToList();
            var root = new JsonMap
            {
                { "reportType", "htreasure-security-advisory-report" },
                { "generatedAt", generatedAt },
                { "standardsVersion", StandardsInfo.StandardsVersion },
                { "application", app },
                { "source", v.Source.ToString() },
                { "sourceDescription", v.SourceDescription },
            };
            if (v.DataGeneratedAt.HasValue) root.Add("dataGeneratedAt", v.DataGeneratedAt.Value);
            if (v.LastSuccessfulCheck.HasValue) root.Add("lastSuccessfulCheck", v.LastSuccessfulCheck.Value);
            root.Add("historical", v.IsHistorical);
            root.Add("assessment", v.OverallAssessment);
            root.Add("advisories", advisories);
            root.Add("statement", SecurityAdvisoryView.PolicyStatement);
            return JsonOut.Serialize(root);
        }

        public static string StatusText(AdvisoryStatus s)
        {
            switch (s)
            {
                case AdvisoryStatus.UnderReview: return "Under Review";
                case AdvisoryStatus.NotAffected: return "Not Affected";
                default: return s.ToString();
            }
        }

        private static string Fmt(DateTimeOffset d) { return d.ToString("yyyy-MM-dd HH:mm 'UTC'zzz", CultureInfo.InvariantCulture); }
        private static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
        private static string Escape(string s) { return (s ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " "); }
    }
}
