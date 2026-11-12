using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using HTFileHashChecker.Core;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Logging;
using HTFileHashChecker.Core.Processing;
using HTFileHashChecker.Core.Settings;
using HTFileHashChecker.Windows;

namespace HTFileHashChecker.UI
{
    /// <summary>Composition root: creates the services once and hands them to the UI.</summary>
    internal sealed class AppServices
    {
        public string ExecutablePath;
        public AppPaths Paths;
        public SettingsStore SettingsStore;
        public AppSettings Settings;
        public ILogger Logger;
        public HashWorkspace Workspace;
        public ExplorerIntegration Explorer;

        public static AppServices Create(string executablePath, AppPaths paths)
        {
            var store = new SettingsStore(paths.SettingsFile);
            var settings = store.Load();
            ILogger logger = new FileLogger(paths.LogDirectory, settings.LogRetentionDays);
            if (store.LoadWarning != null) logger.Warn(store.LoadWarning);
            return new AppServices
            {
                ExecutablePath = executablePath,
                Paths = paths,
                SettingsStore = store,
                Settings = settings,
                Logger = logger,
                Workspace = new HashWorkspace(new BatchProcessor(new FileHasher(), logger)),
                Explorer = new ExplorerIntegration(),
            };
        }

        public bool TrySaveSettings(out string error)
        {
            error = null;
            try
            {
                SettingsStore.Save(Settings);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                Logger.Warn("Settings could not be saved: " + ex.GetType().Name);
                return false;
            }
        }
    }

    internal static class Program
    {
        public const string InstanceId = "HTREASURE.HTFileHashChecker";

        /// <summary>Checked by Setup (AppMutex) so the program is closed before files are replaced.</summary>
        public const string RunningMutexName = "HTREASURE_HTFileHashChecker_Running";

        [STAThread]
        private static int Main(string[] args)
        {
            CommandLine cmd = CommandLine.Parse(args);
            string exePath = Application.ExecutablePath;
            AppPaths paths = AppPaths.Detect(Path.GetDirectoryName(exePath));

            if (cmd.RegisterExplorer || cmd.UnregisterExplorer)
                return RunShellRegistration(cmd, exePath);
            if (cmd.SelfTestOutput != null)
                return SelfTest.Run(cmd.SelfTestOutput, exePath, paths);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var single = new SingleInstance(InstanceId))
            {
                if (!single.TryBecomePrimary())
                {
                    string[] forward = args.Length > 0 ? args : new[] { "--activate" };
                    if (single.SendToPrimary(forward, 8000)) return 0;
                    // The first instance did not answer (busy or closing): continue as an independent window.
                }

                AppServices services;
                try
                {
                    services = AppServices.Create(exePath, paths);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("HT File Hash Checker could not start: " + ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }

                services.Logger.Info("Started " + AppInfo.ProductNameAndVersion + (paths.IsPortable ? " (portable)" : string.Empty)
                    + ", " + (Environment.Is64BitProcess ? "64-bit" : "32-bit") + " process, " + Environment.OSVersion.VersionString);
                Application.ThreadException += (s, e) => ReportCrash(services.Logger, e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(services.Logger, e.ExceptionObject as Exception);

                using (new Mutex(false, RunningMutexName))
                using (var form = new MainForm(services, cmd))
                {
                    single.StartListening(form.ReceiveArguments);
                    Application.Run(form);
                }
                services.Logger.Info("Exited.");
            }
            return 0;
        }

        private static int RunShellRegistration(CommandLine cmd, string exePath)
        {
            try
            {
                var explorer = new ExplorerIntegration();
                if (cmd.RegisterExplorer) explorer.Register(exePath);
                else explorer.RemoveUserEntries();
                return 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException || ex is ArgumentException)
            {
                return 2;
            }
        }

        private static void ReportCrash(ILogger logger, Exception ex)
        {
            if (ex == null) return;
            logger.Error("Unhandled exception", ex);
            try
            {
                MessageBox.Show("An unexpected error occurred:\n\n" + ex.Message + "\n\nDetails were written to the log file.",
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>
    /// "--selftest &lt;file&gt;": writes diagnostic facts and a known-answer hash check to a text file and exits without
    /// showing a window or touching the registry. Used by the release scripts to check installed and portable copies.
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string outputPath, string exePath, AppPaths paths)
        {
            string error;
            if (!PathValidator.TryValidateLocalPath(outputPath, out error)) return 3;
            var sb = new StringBuilder();
            sb.AppendLine("Product=" + AppInfo.ProductName);
            sb.AppendLine("Version=" + AppInfo.Version);
            sb.AppendLine("Executable=" + exePath);
            sb.AppendLine("Portable=" + paths.IsPortable);
            sb.AppendLine("SettingsFile=" + paths.SettingsFile);
            sb.AppendLine("LogDirectory=" + paths.LogDirectory);
            sb.AppendLine("Process64Bit=" + Environment.Is64BitProcess);
            sb.AppendLine("OS=" + Environment.OSVersion.VersionString);
            sb.AppendLine("CLR=" + Environment.Version);
            bool allPass = true;
            // FIPS 180-4 / RFC 1321 known answers for the message "abc".
            var vectors = new[]
            {
                Tuple.Create(HashAlgorithmKind.Sha256, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),
                Tuple.Create(HashAlgorithmKind.Sha1, "a9993e364706816aba3e25717850c26c9cd0d89d"),
                Tuple.Create(HashAlgorithmKind.Md5, "900150983cd24fb0d6963f7d28e17f72"),
            };
            foreach (var v in vectors)
            {
                string actual;
                try
                {
                    using (var h = HashAlgorithms.Create(v.Item1))
                        actual = Hex.ToHex(h.ComputeHash(Encoding.ASCII.GetBytes("abc")));
                }
                catch (HashAlgorithmUnavailableException) { actual = "unavailable"; }
                bool pass = actual == v.Item2;
                allPass &= pass;
                sb.AppendLine("KnownAnswer." + HashAlgorithms.Get(v.Item1).GnuName + "=" + (pass ? "PASS" : "FAIL " + actual));
            }
            sb.AppendLine("Result=" + (allPass ? "PASS" : "FAIL"));
            sb.AppendLine("Timestamp=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            try
            {
                File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return 4;
            }
            return allPass ? 0 : 1;
        }
    }
}
