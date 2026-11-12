using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Reports;

namespace HTFileHashChecker.Core.Settings
{
    public enum ThemePreference
    {
        Light,
        Dark,
    }

    /// <summary>User preferences. Stored outside the installation folder so they survive upgrades.</summary>
    [XmlRoot("Settings")]
    public sealed class AppSettings
    {
        public const int CurrentSchemaVersion = 1;

        public AppSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            DefaultAlgorithm = HashAlgorithms.Default;
            Theme = ThemePreference.Light;
            IncludeSubfolders = true;
            SkipOnlineOnlyFiles = true;
            MaxParallelFiles = 1;
            DefaultReportFormat = ReportFormat.Html;
            OpenReportAfterSaving = false;
            ReportUnexpectedFilesByDefault = false;
            ConfirmBeforeClearing = true;
            LogRetentionDays = 14;
            WindowState = "Normal";
        }

        public int SchemaVersion { get; set; }
        public HashAlgorithmKind DefaultAlgorithm { get; set; }
        public string LastOutputFolder { get; set; }
        public ThemePreference Theme { get; set; }

        // Window placement (logical pixels at 96 DPI; 0 width = use default).
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public string WindowState { get; set; }

        // Reports
        public ReportFormat DefaultReportFormat { get; set; }
        public bool OpenReportAfterSaving { get; set; }

        /// <summary>The user's choice for the Explorer "Calculate Hash" menu (the registry holds the actual state).</summary>
        public bool ExplorerIntegrationEnabled { get; set; }

        public bool IncludeSubfolders { get; set; }
        public bool SkipOnlineOnlyFiles { get; set; }
        public int MaxParallelFiles { get; set; }
        public bool ReportUnexpectedFilesByDefault { get; set; }
        public bool ConfirmBeforeClearing { get; set; }
        public int LogRetentionDays { get; set; }

        /// <summary>Repairs out-of-range values (hand-edited or from a newer version) instead of failing.</summary>
        public void Normalize()
        {
            if (!HashAlgorithms.IsDefined(DefaultAlgorithm)) DefaultAlgorithm = HashAlgorithms.Default;
            if (!Enum.IsDefined(typeof(ThemePreference), Theme)) Theme = ThemePreference.Light;
            if (!Enum.IsDefined(typeof(ReportFormat), DefaultReportFormat)) DefaultReportFormat = ReportFormat.Html;
            if (MaxParallelFiles < 1) MaxParallelFiles = 1;
            if (MaxParallelFiles > 4) MaxParallelFiles = 4;
            if (LogRetentionDays < 1 || LogRetentionDays > 365) LogRetentionDays = 14;
            if (WindowWidth < 0 || WindowHeight < 0 || WindowWidth > 20000 || WindowHeight > 20000) { WindowWidth = 0; WindowHeight = 0; }
            if (WindowState != "Normal" && WindowState != "Maximized") WindowState = "Normal";
            if (LastOutputFolder != null)
            {
                string error;
                if (!PathValidator.TryValidateLocalPath(LastOutputFolder, out error)) LastOutputFolder = null;
            }
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }

    /// <summary>Loads and saves <see cref="AppSettings"/> as XML (atomic writes; corrupt files are backed up and replaced by defaults).</summary>
    public sealed class SettingsStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AppSettings));

        public SettingsStore(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            FilePath = path;
        }

        public string FilePath { get; private set; }

        /// <summary>Set when the last <see cref="Load"/> found an unreadable file (it is renamed to *.corrupt).</summary>
        public string LoadWarning { get; private set; }

        public AppSettings Load()
        {
            LoadWarning = null;
            if (!File.Exists(FilePath)) return new AppSettings();
            try
            {
                AppSettings settings;
                var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = XmlReader.Create(stream, readerSettings))
                {
                    settings = (AppSettings)Serializer.Deserialize(reader);
                }
                if (settings == null) settings = new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
            {
                LoadWarning = "Your settings file could not be read and default settings are being used.";
                try
                {
                    string backup = FilePath + ".corrupt";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(FilePath, backup);
                }
                catch (Exception moveEx) when (moveEx is IOException || moveEx is UnauthorizedAccessException) { }
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            var sb = new StringBuilder();
            var writerSettings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
            using (var sw = new Utf8StringWriter(sb))
            using (var writer = XmlWriter.Create(sw, writerSettings))
            {
                var ns = new XmlSerializerNamespaces();
                ns.Add(string.Empty, string.Empty);
                Serializer.Serialize(writer, settings, ns);
            }
            AtomicFile.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false), true);
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter(StringBuilder sb) : base(sb, System.Globalization.CultureInfo.InvariantCulture) { }
            public override Encoding Encoding { get { return Encoding.UTF8; } }
        }
    }
}
