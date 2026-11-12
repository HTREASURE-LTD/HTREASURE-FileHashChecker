using System;
using System.IO;

namespace HTFileHashChecker.Core.Settings
{
    /// <summary>
    /// Where settings and logs live.
    /// Installed: %APPDATA%\HTREASURE LTD\HT File Hash Checker\settings.xml and %LOCALAPPDATA%\...\Logs.
    /// Portable (marker file "HTFileHashChecker.portable" next to the executable): .\Data\settings.xml and .\Data\Logs,
    /// so a portable copy never shares settings with an installed copy.
    /// </summary>
    public sealed class AppPaths
    {
        private const string CompanyFolder = "HTREASURE LTD";
        private const string ProductFolder = "HT File Hash Checker";

        private AppPaths(bool portable, string settingsFile, string logDirectory)
        {
            IsPortable = portable;
            SettingsFile = settingsFile;
            LogDirectory = logDirectory;
        }

        public bool IsPortable { get; private set; }
        public string SettingsFile { get; private set; }
        public string LogDirectory { get; private set; }

        public static AppPaths Detect(string executableDirectory)
        {
            if (executableDirectory != null && File.Exists(Path.Combine(executableDirectory, AppInfo.PortableMarkerFileName)))
            {
                string data = Path.Combine(executableDirectory, "Data");
                return new AppPaths(true, Path.Combine(data, "settings.xml"), Path.Combine(data, "Logs"));
            }
            string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), CompanyFolder, ProductFolder);
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CompanyFolder, ProductFolder);
            return new AppPaths(false, Path.Combine(roaming, "settings.xml"), Path.Combine(local, "Logs"));
        }

        public static AppPaths ForTesting(string root)
        {
            return new AppPaths(true, Path.Combine(root, "settings.xml"), Path.Combine(root, "Logs"));
        }
    }
}
