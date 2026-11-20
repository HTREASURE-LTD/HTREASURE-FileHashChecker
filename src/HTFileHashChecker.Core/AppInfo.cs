using System;
using System.Reflection;

namespace HTFileHashChecker.Core
{
    /// <summary>Product identity used by the UI, reports, logs and settings paths.</summary>
    public static class AppInfo
    {
        public const string ProductName = "HT File Hash Checker";
        public const string Publisher = "HTREASURE LTD";
        public const string Website = "https://htreasure.com";
        public const string SupportEmail = "support@htreasure.com";
        public const string ExecutableName = "HTFileHashChecker.exe";

        /// <summary>Marker file next to the executable that switches the application into portable mode.</summary>
        public const string PortableMarkerFileName = "HTFileHashChecker.portable";

        /// <summary>"Copyright © {year} HTREASURE LTD. All rights reserved." as stamped at build time by
        /// HTREASURE.Branding.props (HTCopyrightYear).</summary>
        public static string Copyright
        {
            get
            {
                var attr = (AssemblyCopyrightAttribute)Attribute.GetCustomAttribute(typeof(AppInfo).Assembly, typeof(AssemblyCopyrightAttribute));
                return attr != null && !string.IsNullOrEmpty(attr.Copyright) ? attr.Copyright : "Copyright © HTREASURE LTD. All rights reserved.";
            }
        }

        public static string Support { get { return "htreasure.com · " + SupportEmail; } }

        public static string Version
        {
            get
            {
                var attr = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(AppInfo).Assembly, typeof(AssemblyInformationalVersionAttribute));
                return attr != null && !string.IsNullOrEmpty(attr.InformationalVersion)
                    ? attr.InformationalVersion.Split('+')[0]
                    : typeof(AppInfo).Assembly.GetName().Version.ToString(3);
            }
        }

        /// <summary>The .NET Framework build this copy was compiled for ("4.8" or "4.6.1").</summary>
        public static string TargetFrameworkDisplay
        {
            get
            {
#if NET48
                return "4.8";
#elif NET461
                return "4.6.1";
#else
                return "4.x";
#endif
            }
        }

        public static string ProductNameAndVersion
        {
            get { return ProductName + " " + Version; }
        }
    }
}
