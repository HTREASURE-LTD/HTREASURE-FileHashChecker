using System;
using System.Reflection;

namespace HTFileHashChecker.Core
{
    /// <summary>Product identity used by the UI, reports, logs and settings paths.</summary>
    public static class AppInfo
    {
        public const string ProductName = "HT File Hash Checker";
        public const string Publisher = "HTREASURE LTD";
        public const string Copyright = "© 2026 HTREASURE LTD";
        public const string ExecutableName = "HTFileHashChecker.exe";

        /// <summary>Marker file next to the executable that switches the application into portable mode.</summary>
        public const string PortableMarkerFileName = "HTFileHashChecker.portable";

        public static string Version
        {
            get
            {
                var attr = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(AppInfo).Assembly, typeof(AssemblyInformationalVersionAttribute));
                return attr != null && !string.IsNullOrEmpty(attr.InformationalVersion)
                    ? attr.InformationalVersion
                    : typeof(AppInfo).Assembly.GetName().Version.ToString(3);
            }
        }

        public static string ProductNameAndVersion
        {
            get { return ProductName + " " + Version; }
        }
    }
}
