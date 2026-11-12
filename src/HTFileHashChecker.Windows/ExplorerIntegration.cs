using System;
using System.IO;
using Microsoft.Win32;

namespace HTFileHashChecker.Windows
{
    /// <summary>
    /// The optional "Calculate Hash with HT File Hash Checker" Explorer context-menu entry.
    /// It is a plain static verb (registry keys only): no shell-extension DLL, no COM server, no background service.
    /// The application manages the per-user entry under HKCU\Software\Classes (no administrator rights needed);
    /// Setup can create the same entry for all users under HKLM\Software\Classes and removes it on uninstall.
    /// </summary>
    public sealed class ExplorerIntegration
    {
        public const string VerbName = "HTFileHashChecker.Calculate";
        public const string MenuText = "Calculate Hash with HT File Hash Checker";
        public const string CalculateSwitch = "--calculate";

        /// <summary>File and folder targets (relative to ...\Software\Classes).</summary>
        internal static readonly string[] TargetKeys = { @"*\shell\" + VerbName, @"Directory\shell\" + VerbName };

        private readonly RegistryKey userRoot;
        private readonly RegistryKey machineRoot;
        private readonly string classesPath;

        /// <summary>Uses HKEY_CURRENT_USER and HKEY_LOCAL_MACHINE (read-only).</summary>
        public ExplorerIntegration() : this(Registry.CurrentUser, Registry.LocalMachine, @"Software\Classes") { }

        /// <summary>For tests: any user/machine roots and classes path (machine root may be null).</summary>
        public ExplorerIntegration(RegistryKey userRoot, RegistryKey machineRoot, string classesPath)
        {
            if (userRoot == null) throw new ArgumentNullException("userRoot");
            this.userRoot = userRoot;
            this.machineRoot = machineRoot;
            this.classesPath = classesPath.TrimEnd('\\');
        }

        /// <summary>Command line Explorer runs: the executable and the selected item are each quoted. Windows paths cannot
        /// contain a double quote, so quoting cannot be broken out of.</summary>
        public static string BuildCommand(string executablePath)
        {
            ValidateExecutablePath(executablePath);
            return "\"" + executablePath + "\" " + CalculateSwitch + " \"%1\"";
        }

        private static void ValidateExecutablePath(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath)) throw new ArgumentException("Executable path is required.", "executablePath");
            if (executablePath.IndexOf('"') >= 0 || executablePath.IndexOf('%') >= 0)
                throw new ArgumentException("The executable path contains characters that cannot be used in a shell command.", "executablePath");
            if (!Path.IsPathRooted(executablePath)) throw new ArgumentException("The executable path must be absolute.", "executablePath");
        }

        /// <summary>Adds the per-user entry for files and folders.</summary>
        public void Register(string executablePath)
        {
            ValidateExecutablePath(executablePath);
            string command = BuildCommand(executablePath);
            foreach (string target in TargetKeys)
            {
                using (var verb = userRoot.CreateSubKey(classesPath + "\\" + target))
                {
                    verb.SetValue(string.Empty, MenuText);
                    verb.SetValue("MUIVerb", MenuText);
                    verb.SetValue("Icon", "\"" + executablePath + "\",0");
                    // "Player" shows the entry for any number of selected items; Explorer starts one process per item and
                    // the running instance collects them (see SingleInstance).
                    verb.SetValue("MultiSelectModel", "Player");
                    verb.DeleteValue("LegacyDisable", false);
                    using (var cmd = verb.CreateSubKey("command"))
                    {
                        cmd.SetValue(string.Empty, command);
                    }
                }
            }
        }

        /// <summary>
        /// Removes the per-user entry. If Setup installed an all-users entry, it is hidden for this user with
        /// "LegacyDisable" (removing it requires administrator rights and is done by the uninstaller).
        /// </summary>
        public void Unregister()
        {
            foreach (string target in TargetKeys)
            {
                userRoot.DeleteSubKeyTree(classesPath + "\\" + target, false);
                if (MachineEntryExists(target))
                {
                    using (var verb = userRoot.CreateSubKey(classesPath + "\\" + target))
                    {
                        verb.SetValue("LegacyDisable", string.Empty);
                    }
                }
            }
        }

        /// <summary>Removes only what the application itself created for this user (used by the uninstaller).</summary>
        public void RemoveUserEntries()
        {
            foreach (string target in TargetKeys)
                userRoot.DeleteSubKeyTree(classesPath + "\\" + target, false);
        }

        public ExplorerIntegrationState GetState(string executablePath)
        {
            var state = new ExplorerIntegrationState();
            foreach (string target in new[] { TargetKeys[0] })
            {
                string userCommand = null;
                bool hidden = false;
                using (var verb = userRoot.OpenSubKey(classesPath + "\\" + target))
                {
                    if (verb != null)
                    {
                        hidden = verb.GetValue("LegacyDisable") != null;
                        using (var cmd = verb.OpenSubKey("command"))
                            if (cmd != null) userCommand = cmd.GetValue(string.Empty) as string;
                    }
                }
                string machineCommand = null;
                if (machineRoot != null)
                {
                    using (var verb = machineRoot.OpenSubKey(classesPath + "\\" + target))
                    using (var cmd = verb != null ? verb.OpenSubKey("command") : null)
                        if (cmd != null) machineCommand = cmd.GetValue(string.Empty) as string;
                }

                state.UserEntry = userCommand != null;
                state.MachineEntry = machineCommand != null;
                state.HiddenForUser = hidden;
                string effective = userCommand ?? machineCommand;
                state.Enabled = effective != null && !hidden;
                state.Command = effective;
                state.PointsToThisExecutable = effective != null && executablePath != null
                    && string.Equals(effective, BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);
            }
            return state;
        }

        private bool MachineEntryExists(string target)
        {
            if (machineRoot == null) return false;
            using (var key = machineRoot.OpenSubKey(classesPath + "\\" + target + "\\command"))
                return key != null;
        }
    }

    public sealed class ExplorerIntegrationState
    {
        /// <summary>The menu entry is currently shown for this user.</summary>
        public bool Enabled { get; internal set; }
        public bool UserEntry { get; internal set; }
        public bool MachineEntry { get; internal set; }
        public bool HiddenForUser { get; internal set; }
        public bool PointsToThisExecutable { get; internal set; }
        public string Command { get; internal set; }
    }
}
