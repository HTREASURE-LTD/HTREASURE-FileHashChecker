using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HTREASURE.Standards.Security
{
    public enum AdvisorySeverity { Low, Medium, High, Critical }

    public enum AdvisoryStatus { Fixed, Affected, UnderReview, Unknown, NotAffected }

    public sealed class AdvisoryAlias
    {
        public string Type { get; set; }
        public string Id { get; set; }
        public bool Verified { get; set; }
    }

    public sealed class CvssInfo
    {
        public string Version { get; set; }
        public string Vector { get; set; }
        public double BaseScore { get; set; }
    }

    public sealed class AdvisoryReference
    {
        public string Type { get; set; }
        public string Url { get; set; }
    }

    public sealed class VersionRange
    {
        public string Introduced { get; set; }
        public string Fixed { get; set; }
        public string LastAffected { get; set; }
    }

    public sealed class AffectedEntry
    {
        public IList<string> Platforms { get; set; }
        public IList<string> Channels { get; set; }
        public string Status { get; set; }
        public IList<VersionRange> Ranges { get; set; }
        public IList<string> FixedBuilds { get; set; }
        public string AssessedThrough { get; set; }
    }

    /// <summary>One advisory, as defined by security/schemas/advisory.schema.json.</summary>
    public sealed class SecurityAdvisory
    {
        public SecurityAdvisory() { SchemaVersion = "1.0.0"; }

        public string SchemaVersion { get; set; }
        public string Id { get; set; }
        public string AppId { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Severity { get; set; }
        public CvssInfo Cvss { get; set; }
        public IList<AdvisoryAlias> Aliases { get; set; }
        public string Published { get; set; }
        public string Updated { get; set; }
        public string VerificationStatus { get; set; }
        public IList<AffectedEntry> Affected { get; set; }
        public IList<string> FixedVersions { get; set; }
        public string Remediation { get; set; }
        public string Mitigation { get; set; }
        public IList<AdvisoryReference> References { get; set; }
        public string ReleaseNotes { get; set; }

        public AdvisorySeverity SeverityLevel
        {
            get
            {
                switch (Severity)
                {
                    case "critical": return AdvisorySeverity.Critical;
                    case "high": return AdvisorySeverity.High;
                    case "medium": return AdvisorySeverity.Medium;
                    default: return AdvisorySeverity.Low;
                }
            }
        }

        public string AffectedVersionsText
        {
            get
            {
                var parts = new List<string>();
                foreach (var e in (Affected ?? new List<AffectedEntry>()).Where(a => a.Status == "affected"))
                    foreach (var r in e.Ranges ?? new List<VersionRange>())
                        parts.Add(r.Fixed != null ? ">= " + r.Introduced + ", < " + r.Fixed
                            : r.LastAffected != null ? r.Introduced + " to " + r.LastAffected : ">= " + r.Introduced);
                return parts.Count == 0 ? "Not specified" : string.Join("; ", parts.Distinct());
            }
        }

        public string FixedVersionText
        {
            get { return FixedVersions != null && FixedVersions.Count > 0 ? string.Join(", ", FixedVersions) : "No verified fix"; }
        }

        internal static SecurityAdvisory FromJson(JsonObj o)
        {
            var a = new SecurityAdvisory
            {
                SchemaVersion = o.Str("schemaVersion"),
                Id = o.Str("id"),
                AppId = o.Str("appId"),
                Title = o.Str("title"),
                Summary = o.Str("summary"),
                Severity = o.Str("severity"),
                Published = o.Str("published"),
                Updated = o.Str("updated"),
                VerificationStatus = o.Str("verificationStatus"),
                Remediation = o.Str("remediation"),
                Mitigation = o.Str("mitigation", false),
                ReleaseNotes = o.Str("releaseNotes", false),
                FixedVersions = o.Strs("fixedVersions", false),
            };
            var cvss = o.Obj("cvss", false);
            if (cvss != null)
            {
                a.Cvss = new CvssInfo { Version = cvss.Str("version"), Vector = cvss.Str("vector"), BaseScore = cvss.Number("baseScore").Value };
                cvss.RejectUnknown();
            }
            var aliases = o.Objs("aliases", false);
            if (aliases != null)
                a.Aliases = aliases.Select(x => { var r = new AdvisoryAlias { Type = x.Str("type"), Id = x.Str("id"), Verified = x.Bool("verified") }; x.RejectUnknown(); return r; }).ToList();
            var refs = o.Objs("references", false);
            if (refs != null)
                a.References = refs.Select(x => { var r = new AdvisoryReference { Type = x.Str("type"), Url = x.Str("url") }; x.RejectUnknown(); return r; }).ToList();
            a.Affected = o.Objs("affected").Select(x =>
            {
                var e = new AffectedEntry
                {
                    Platforms = x.Strs("platforms"),
                    Channels = x.Strs("channels"),
                    Status = x.Str("status"),
                    FixedBuilds = x.Strs("fixedBuilds", false),
                    AssessedThrough = x.Str("assessedThrough", false),
                };
                var ranges = x.Objs("ranges", false);
                if (ranges != null)
                    e.Ranges = ranges.Select(r => { var v = new VersionRange { Introduced = r.Str("introduced"), Fixed = r.Str("fixed", false), LastAffected = r.Str("lastAffected", false) }; r.RejectUnknown(); return v; }).ToList();
                x.RejectUnknown();
                return e;
            }).ToList();
            o.RejectUnknown();
            return a;
        }
    }

    public static class FeedKinds
    {
        public const string Live = "live";
        public const string BundledSnapshot = "bundled-snapshot";
        public const string TestFixture = "test-fixture";
    }

    /// <summary>A signed collection of advisories (security/schemas/advisory-feed.schema.json).</summary>
    public sealed class AdvisoryFeed
    {
        public string FeedType { get; set; }
        public string SchemaVersion { get; set; }
        public string Kind { get; set; }
        public long Sequence { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string Scope { get; set; }
        public IList<SecurityAdvisory> Advisories { get; set; }

        internal static AdvisoryFeed Parse(byte[] content, int maxBytes)
        {
            var o = JsonObj.Parse(content, maxBytes);
            var feed = new AdvisoryFeed
            {
                FeedType = o.Has("feedType") ? o.Str("feedType") : "htreasure-security-advisories",
                SchemaVersion = o.Has("schemaVersion") ? o.Str("schemaVersion") : "1.0.0",
                Kind = o.Str("kind"),
                Sequence = o.Long("sequence"),
                GeneratedAt = o.Date("generatedAt").Value,
                ExpiresAt = o.Date("expiresAt", false),
                Scope = o.Str("scope"),
                Advisories = o.Objs("advisories").Select(SecurityAdvisory.FromJson).ToList(),
            };
            o.RejectUnknown();
            return feed;
        }
    }

    /// <summary>Identity of the running installation. Collected locally only; never transmitted.</summary>
    public sealed class InstalledApplication
    {
        public InstalledApplication(string appId, string name, string version, string channel, string platform, string buildId = null)
        {
            AppId = appId; Name = name; Version = version; Channel = channel; Platform = platform; BuildId = buildId;
        }

        public string AppId { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; }
        public string Channel { get; private set; }
        public string Platform { get; private set; }
        public string BuildId { get; private set; }
    }

    public static class StandardsInfo
    {
        /// <summary>The HTREASURE standards version this port implements.</summary>
        public static string StandardsVersion
        {
            get
            {
                var attr = typeof(StandardsInfo).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                    .OfType<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "HTStandardsVersion");
                return attr != null ? attr.Value : "unknown";
            }
        }
    }

    /// <summary>Semantic Version 2.0.0. Precedence ignores build metadata.</summary>
    public sealed class SemVersion : IComparable<SemVersion>
    {
        private static readonly Regex Pattern = new Regex(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
            RegexOptions.CultureInvariant);

        private SemVersion(int major, int minor, int patch, string pre, string build, string original)
        {
            Major = major; Minor = minor; Patch = patch; Prerelease = pre; Build = build; Original = original;
        }

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Prerelease { get; private set; }
        public string Build { get; private set; }
        public string Original { get; private set; }

        public static bool TryParse(string text, out SemVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 128) return false;
            var m = Pattern.Match(text.Trim());
            if (!m.Success) return false;
            int major, minor, patch;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major)
                || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor)
                || !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            version = new SemVersion(major, minor, patch, m.Groups[4].Value, m.Groups[5].Value, text.Trim());
            return true;
        }

        public int CompareTo(SemVersion other)
        {
            if (other == null) return 1;
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        private static int ComparePrerelease(string a, string b)
        {
            if (a.Length == 0 && b.Length == 0) return 0;
            if (a.Length == 0) return 1;
            if (b.Length == 0) return -1;
            var pa = a.Split('.');
            var pb = b.Split('.');
            for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
            {
                long ia, ib;
                bool na = long.TryParse(pa[i], NumberStyles.None, CultureInfo.InvariantCulture, out ia);
                bool nb = long.TryParse(pb[i], NumberStyles.None, CultureInfo.InvariantCulture, out ib);
                int c = na && nb ? ia.CompareTo(ib) : na ? -1 : nb ? 1 : string.CompareOrdinal(pa[i], pb[i]);
                if (c != 0) return c;
            }
            return pa.Length.CompareTo(pb.Length);
        }

        public override string ToString() { return Original; }
    }

    /// <summary>Result of assessing one advisory against one installation.</summary>
    public sealed class AdvisoryAssessment
    {
        public AdvisoryAssessment(SecurityAdvisory advisory, AdvisoryStatus status, string reason)
        {
            Advisory = advisory; Status = status; Reason = reason;
        }

        public SecurityAdvisory Advisory { get; private set; }
        public AdvisoryStatus Status { get; private set; }
        public string Reason { get; private set; }
    }

    /// <summary>
    /// Decides whether an advisory applies to an installation (port of AdvisoryStatusEngine; same rules and wording).
    /// Conservative: only rules for the installation's platform and channel count; Fixed needs an explicit fixed build,
    /// the fixed version itself, or assessedThrough coverage; Not Affected needs an explicit verified rule.
    /// </summary>
    public static class AdvisoryStatusEngine
    {
        private static readonly AdvisoryStatus[] WorstFirst =
            { AdvisoryStatus.Affected, AdvisoryStatus.UnderReview, AdvisoryStatus.Unknown, AdvisoryStatus.Fixed, AdvisoryStatus.NotAffected };

        public static AdvisoryAssessment Assess(SecurityAdvisory advisory, InstalledApplication app)
        {
            if (!string.Equals(advisory.AppId, app.AppId, StringComparison.Ordinal))
                return new AdvisoryAssessment(advisory, AdvisoryStatus.NotAffected, "This advisory concerns a different application.");

            var matching = (advisory.Affected ?? new List<AffectedEntry>())
                .Where(e => Matches(e.Platforms, app.Platform) && Matches(e.Channels, app.Channel)).ToList();

            if (matching.Any(e => IsListedFixedBuild(e, app)))
                return advisory.VerificationStatus == "verified"
                    ? new AdvisoryAssessment(advisory, AdvisoryStatus.Fixed, "Build " + Describe(app) + " is verified to contain the fix.")
                    : new AdvisoryAssessment(advisory, AdvisoryStatus.UnderReview, "A fix is recorded for this build, but the advisory assessment is not complete.");

            if (advisory.VerificationStatus == "under-review")
                return new AdvisoryAssessment(advisory, AdvisoryStatus.UnderReview, "HTREASURE is still assessing whether this installation is affected.");
            if (advisory.VerificationStatus != "verified")
                return new AdvisoryAssessment(advisory, AdvisoryStatus.Unknown, "There is not enough verified information to decide whether this installation is affected.");

            if (matching.Count == 0)
                return new AdvisoryAssessment(advisory, AdvisoryStatus.Unknown,
                    "No verified assessment covers platform '" + app.Platform + "' on the '" + app.Channel + "' channel.");

            SemVersion installed;
            if (!SemVersion.TryParse(app.Version, out installed))
                return new AdvisoryAssessment(advisory, AdvisoryStatus.Unknown, "Installed version '" + app.Version + "' could not be read.");

            var results = matching.Select(e => AssessEntry(e, installed)).ToList();
            foreach (var status in WorstFirst)
            {
                var hit = results.FirstOrDefault(r => r.Key == status);
                if (hit.Value != null) return new AdvisoryAssessment(advisory, status, hit.Value);
            }
            return new AdvisoryAssessment(advisory, AdvisoryStatus.Unknown, "The advisory rules could not be evaluated.");
        }

        public static IList<AdvisoryAssessment> AssessAll(IEnumerable<SecurityAdvisory> advisories, InstalledApplication app)
        {
            return advisories.Where(a => a.AppId == app.AppId)
                .Select(a => Assess(a, app))
                .OrderBy(r => Rank(r.Status))
                .ThenByDescending(r => r.Advisory.SeverityLevel)
                .ThenByDescending(r => r.Advisory.Published, StringComparer.Ordinal)
                .ToList();
        }

        public static int Rank(AdvisoryStatus s)
        {
            switch (s)
            {
                case AdvisoryStatus.Affected: return 0;
                case AdvisoryStatus.UnderReview: return 1;
                case AdvisoryStatus.Unknown: return 2;
                case AdvisoryStatus.Fixed: return 3;
                default: return 4;
            }
        }

        private static KeyValuePair<AdvisoryStatus, string> R(AdvisoryStatus s, string reason)
        {
            return new KeyValuePair<AdvisoryStatus, string>(s, reason);
        }

        private static KeyValuePair<AdvisoryStatus, string> AssessEntry(AffectedEntry entry, SemVersion v)
        {
            if (entry.Status == "not-affected")
                return R(AdvisoryStatus.NotAffected, "A verified assessment confirms this platform and channel are not affected.");

            SemVersion assessedThrough = null;
            if (entry.AssessedThrough != null && !SemVersion.TryParse(entry.AssessedThrough, out assessedThrough))
                return R(AdvisoryStatus.Unknown, "The advisory's assessment range is invalid.");

            var outcomes = new List<KeyValuePair<AdvisoryStatus, string>>();
            foreach (var r in entry.Ranges ?? new List<VersionRange>())
            {
                SemVersion intro;
                if (!SemVersion.TryParse(r.Introduced, out intro)) { outcomes.Add(R(AdvisoryStatus.Unknown, "The advisory's version range is invalid.")); continue; }
                if (v.CompareTo(intro) < 0)
                {
                    outcomes.Add(R(AdvisoryStatus.NotAffected, "The vulnerability was introduced in " + r.Introduced + ", after the installed version."));
                    continue;
                }
                if (r.Fixed != null)
                {
                    SemVersion fixedV;
                    if (!SemVersion.TryParse(r.Fixed, out fixedV)) { outcomes.Add(R(AdvisoryStatus.Unknown, "The advisory's fixed version is invalid.")); continue; }
                    int c = v.CompareTo(fixedV);
                    if (c < 0) { outcomes.Add(R(AdvisoryStatus.Affected, "Versions from " + r.Introduced + " up to, but not including, " + r.Fixed + " are affected.")); continue; }
                    if (c == 0) { outcomes.Add(R(AdvisoryStatus.Fixed, "The fix was released in " + r.Fixed + ".")); continue; }
                    if (assessedThrough != null && v.CompareTo(assessedThrough) <= 0)
                    {
                        outcomes.Add(R(AdvisoryStatus.Fixed, "Fixed in " + r.Fixed + " and verified as still fixed through " + entry.AssessedThrough + "."));
                        continue;
                    }
                    outcomes.Add(R(AdvisoryStatus.Unknown, "Fixed in " + r.Fixed + ", but no verified assessment yet confirms the fix is carried into this newer version."));
                    continue;
                }
                if (r.LastAffected != null)
                {
                    SemVersion last;
                    if (!SemVersion.TryParse(r.LastAffected, out last)) { outcomes.Add(R(AdvisoryStatus.Unknown, "The advisory's version range is invalid.")); continue; }
                    outcomes.Add(v.CompareTo(last) <= 0
                        ? R(AdvisoryStatus.Affected, "Versions " + r.Introduced + " to " + r.LastAffected + " are affected.")
                        : R(AdvisoryStatus.Unknown, "No verified fix is recorded after " + r.LastAffected + "."));
                    continue;
                }
                outcomes.Add(R(AdvisoryStatus.Affected, "All versions from " + r.Introduced + " are affected and no fix is available yet."));
            }

            foreach (var status in new[] { AdvisoryStatus.Affected, AdvisoryStatus.Unknown, AdvisoryStatus.Fixed, AdvisoryStatus.NotAffected })
            {
                var hit = outcomes.FirstOrDefault(o => o.Key == status);
                if (hit.Value != null) return hit;
            }
            return R(AdvisoryStatus.Unknown, "No version range applies.");
        }

        private static bool Matches(IList<string> selectors, string value)
        {
            return selectors != null && selectors.Any(s => s == "*" || string.Equals(s, value, StringComparison.Ordinal));
        }

        private static bool IsListedFixedBuild(AffectedEntry entry, InstalledApplication app)
        {
            return entry.FixedBuilds != null && entry.FixedBuilds.Any(b => string.Equals(b, app.Version, StringComparison.Ordinal)
                || (app.BuildId != null && string.Equals(b, app.BuildId, StringComparison.Ordinal)));
        }

        private static string Describe(InstalledApplication app)
        {
            return app.BuildId == null ? app.Version : app.Version + " (" + app.BuildId + ")";
        }
    }

    /// <summary>Validates advisories and feeds against advisory.schema.json plus semantic rules (port of AdvisoryValidator).</summary>
    public static class AdvisoryValidator
    {
        private static readonly Regex IdPattern = new Regex(@"^HTSA-\d{4}-\d{4}$", RegexOptions.CultureInvariant);
        private static readonly Regex KebabPattern = new Regex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex SelectorPattern = new Regex(@"^(\*|[a-z0-9]+(-[a-z0-9]+)*)$", RegexOptions.CultureInvariant);
        private static readonly Regex CvePattern = new Regex(@"^CVE-\d{4}-\d{4,}$", RegexOptions.CultureInvariant);
        private static readonly Regex GhsaPattern = new Regex(@"^GHSA(-[23456789cfghjmpqrvwx]{4}){3}$", RegexOptions.CultureInvariant);
        private static readonly Regex CvssPattern = new Regex(@"^CVSS:(3\.1|4\.0)/[A-Za-z:/]+$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Severities = new HashSet<string> { "critical", "high", "medium", "low" };
        private static readonly HashSet<string> Verifications = new HashSet<string> { "verified", "under-review", "unverified" };
        private static readonly HashSet<string> ReferenceTypes = new HashSet<string> { "advisory", "release-notes", "cve", "ghsa", "web" };

        public static IList<string> Validate(SecurityAdvisory a)
        {
            var e = new List<string>();
            Action<bool, string> req = (ok, msg) => { if (!ok) e.Add(a.Id + ": " + msg); };
            req(a.SchemaVersion == "1.0.0", "schemaVersion must be 1.0.0.");
            req(a.Id != null && IdPattern.IsMatch(a.Id), "id must match HTSA-YYYY-NNNN.");
            req(a.AppId != null && a.AppId.Length <= 64 && KebabPattern.IsMatch(a.AppId), "appId must be lower-case kebab case.");
            req(a.Title != null && a.Title.Length >= 4 && a.Title.Length <= 160, "title must be 4-160 characters.");
            req(a.Summary != null && a.Summary.Length >= 10 && a.Summary.Length <= 20000, "summary must be 10-20000 characters.");
            req(a.Severity != null && Severities.Contains(a.Severity), "severity must be critical, high, medium or low.");
            req(a.VerificationStatus != null && Verifications.Contains(a.VerificationStatus), "verificationStatus is invalid.");
            req(a.Remediation != null && a.Remediation.Length >= 4 && a.Remediation.Length <= 10000, "remediation is required.");
            req(a.Mitigation == null || a.Mitigation.Length <= 10000, "mitigation is too long.");
            req(a.ReleaseNotes == null || a.ReleaseNotes.Length <= 20000, "releaseNotes is too long.");

            DateTime pub, upd;
            bool pubOk = DateTime.TryParseExact(a.Published, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out pub);
            bool updOk = DateTime.TryParseExact(a.Updated, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out upd);
            req(pubOk, "published must be a yyyy-MM-dd date.");
            req(updOk, "updated must be a yyyy-MM-dd date.");
            if (pubOk && updOk) req(upd >= pub, "updated must not be earlier than published.");
            if (pubOk && a.Id != null && IdPattern.IsMatch(a.Id))
                req(int.Parse(a.Id.Substring(5, 4), CultureInfo.InvariantCulture) <= pub.Year + 1, "id year is later than the publication year.");

            if (a.Cvss != null)
            {
                var c = a.Cvss;
                req(c.Version == "3.1" || c.Version == "4.0", "cvss.version must be 3.1 or 4.0.");
                req(c.Vector != null && c.Vector.Length <= 200 && CvssPattern.IsMatch(c.Vector) && c.Vector.StartsWith("CVSS:" + c.Version + "/", StringComparison.Ordinal),
                    "cvss.vector is invalid or does not match cvss.version.");
                req(c.BaseScore >= 0 && c.BaseScore <= 10, "cvss.baseScore must be 0-10.");
            }
            foreach (var al in a.Aliases ?? new List<AdvisoryAlias>())
            {
                req(al.Verified, "alias " + al.Id + " is not verified; only verified external identifiers may be listed.");
                bool valid = al.Type == "CVE" ? CvePattern.IsMatch(al.Id ?? "") : al.Type == "GHSA" && GhsaPattern.IsMatch(al.Id ?? "");
                req(valid, "alias " + al.Id + " is not a valid " + al.Type + " identifier.");
            }
            req((a.Aliases == null ? 0 : a.Aliases.Count) <= 20, "too many aliases.");
            foreach (var r in a.References ?? new List<AdvisoryReference>())
            {
                req(ReferenceTypes.Contains(r.Type ?? ""), "reference type '" + r.Type + "' is invalid.");
                Uri u;
                req(Uri.TryCreate(r.Url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && r.Url.Length <= 500,
                    "reference '" + r.Url + "' must be an absolute HTTPS URL.");
            }
            req(a.Affected != null && a.Affected.Count >= 1 && a.Affected.Count <= 50, "affected must contain 1-50 entries.");
            foreach (var entry in a.Affected ?? new List<AffectedEntry>())
            {
                req(entry.Platforms != null && entry.Platforms.Count > 0 && entry.Platforms.All(p => SelectorPattern.IsMatch(p)), "affected.platforms is invalid.");
                req(entry.Channels != null && entry.Channels.Count > 0 && entry.Channels.All(p => SelectorPattern.IsMatch(p)), "affected.channels is invalid.");
                req(entry.Status == "affected" || entry.Status == "not-affected", "affected.status must be affected or not-affected.");
                if (entry.Status == "affected") req(entry.Ranges != null && entry.Ranges.Count > 0, "an affected entry needs at least one version range.");
                SemVersion tmp;
                if (entry.AssessedThrough != null) req(SemVersion.TryParse(entry.AssessedThrough, out tmp), "assessedThrough is not a semantic version.");
                foreach (var r in entry.Ranges ?? new List<VersionRange>())
                {
                    SemVersion intro;
                    req(SemVersion.TryParse(r.Introduced, out intro), "range introduced '" + r.Introduced + "' is not a semantic version.");
                    req(!(r.Fixed != null && r.LastAffected != null), "a range may set fixed or lastAffected, not both.");
                    if (r.Fixed != null)
                    {
                        SemVersion fx;
                        req(SemVersion.TryParse(r.Fixed, out fx) && intro != null && fx.CompareTo(intro) > 0, "range fixed must be a version later than introduced.");
                    }
                    if (r.LastAffected != null)
                    {
                        SemVersion la;
                        req(SemVersion.TryParse(r.LastAffected, out la) && intro != null && la.CompareTo(intro) >= 0, "range lastAffected must not be earlier than introduced.");
                    }
                }
            }
            return e;
        }

        public static IList<string> Validate(AdvisoryFeed feed)
        {
            var e = new List<string>();
            if (feed.FeedType != "htreasure-security-advisories") e.Add("feedType is invalid.");
            if (feed.SchemaVersion != "1.0.0") e.Add("feed schemaVersion must be 1.0.0.");
            if (feed.Kind != FeedKinds.Live && feed.Kind != FeedKinds.BundledSnapshot && feed.Kind != FeedKinds.TestFixture) e.Add("feed kind is invalid.");
            if (feed.Sequence < 1) e.Add("feed sequence must be positive.");
            if (feed.Kind == FeedKinds.Live && feed.ExpiresAt == null) e.Add("a live feed must have expiresAt.");
            if (feed.ExpiresAt.HasValue && feed.ExpiresAt.Value <= feed.GeneratedAt) e.Add("expiresAt must be later than generatedAt.");
            if (string.IsNullOrWhiteSpace(feed.Scope)) e.Add("scope is required.");
            if (feed.Advisories == null) { e.Add("advisories is required."); return e; }
            foreach (var dup in feed.Advisories.GroupBy(a => a.Id).Where(g => g.Count() > 1)) e.Add("duplicate advisory id " + dup.Key + ".");
            if (feed.Scope != "all")
                foreach (var a in feed.Advisories.Where(a => a.AppId != feed.Scope))
                    e.Add(a.Id + ": appId " + a.AppId + " is outside the feed scope " + feed.Scope + ".");
            foreach (var a in feed.Advisories) e.AddRange(Validate(a));
            return e;
        }
    }
}
