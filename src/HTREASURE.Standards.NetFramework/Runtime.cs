using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HTREASURE.Standards.Security;

namespace HTREASURE.Standards
{
    /// <summary>
    /// Wires an application to the advisory service using the values HTREASURE.Standards.targets compiled into it
    /// (HTAppId, HTChannel, HTBuildId, service URLs, embedded trusted keys). Port of HTStandardsRuntime.
    /// </summary>
    public sealed class HTStandardsRuntime
    {
        public InstalledApplication Application { get; private set; }
        public SecurityAdvisoryCenter Advisories { get; private set; }
        public TrustedKeySet TrustedKeys { get; private set; }
        public Uri AdvisoryServiceUrl { get; private set; }
        public Uri ReleaseFeedUrl { get; private set; }

        public static HTStandardsRuntime FromAssembly(Assembly app, string productName = null, string baseDirectory = null)
        {
            Func<string, string> meta = key =>
            {
                var a = app.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false).OfType<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == key);
                return a != null && !string.IsNullOrEmpty(a.Value) ? a.Value : null;
            };
            string appId = meta("HTAppId");
            if (appId == null) throw new InvalidOperationException("HTAppId is not set. Import HTREASURE.Standards.targets and set <HTAppId>.");
            var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(app, typeof(AssemblyInformationalVersionAttribute));
            string version = info != null ? info.InformationalVersion : app.GetName().Version.ToString(3);
            var product = (AssemblyProductAttribute)Attribute.GetCustomAttribute(app, typeof(AssemblyProductAttribute));
            string name = productName ?? (product != null ? product.Product : appId);
            var installed = new InstalledApplication(appId, name, version, meta("HTChannel") ?? "stable", CurrentPlatform(), meta("HTBuildId"));

            TrustedKeySet keys;
            using (var stream = app.GetManifestResourceStream("HTREASURE.Standards.trusted-keys.json"))
            {
                if (stream == null) throw new InvalidOperationException("Trusted keys are not embedded. Import HTREASURE.Standards.targets.");
                keys = TrustedKeySet.Load(stream);
            }
            string dir = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            return new HTStandardsRuntime
            {
                Application = installed,
                TrustedKeys = keys,
                AdvisoryServiceUrl = HttpsOrNull(meta("HTAdvisoryServiceUrl")),
                ReleaseFeedUrl = HttpsOrNull(meta("HTReleaseFeedUrl")),
                Advisories = new SecurityAdvisoryCenter(installed, new SecurityAdvisoryOptions
                {
                    BundledDirectory = Path.Combine(dir, "security"),
                    TrustedKeys = keys,
                    // No HTTPS transport in this port: no advisory service URL has been provisioned yet.
                    Transport = null,
                    AllowUnsignedBundledSnapshot = string.Equals(meta("HTAllowUnsignedSnapshot"), "True", StringComparison.OrdinalIgnoreCase),
                }),
            };
        }

        /// <summary>windows-x64, windows-x86 or windows-arm64 (the operating system's architecture, not the process's).</summary>
        public static string CurrentPlatform()
        {
            string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
            switch (arch.ToUpperInvariant())
            {
                case "AMD64": return "windows-x64";
                case "ARM64": return "windows-arm64";
                case "X86": return "windows-x86";
                default: return Environment.Is64BitOperatingSystem ? "windows-x64" : "windows-x86";
            }
        }

        private static Uri HttpsOrNull(string s)
        {
            Uri u;
            return s != null && Uri.TryCreate(s, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps ? u : null;
        }
    }
}
