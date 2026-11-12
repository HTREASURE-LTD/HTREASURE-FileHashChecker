using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HTFileHashChecker.UI
{
    internal static class UiFormat
    {
        public static string Size(long? bytes)
        {
            if (!bytes.HasValue) return string.Empty;
            double b = bytes.Value;
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            int u = 0;
            while (b >= 1024 && u < units.Length - 1) { b /= 1024; u++; }
            if (u == 0) return bytes.Value.ToString("N0", CultureInfo.CurrentCulture) + " bytes";
            return b.ToString(b >= 100 ? "0" : (b >= 10 ? "0.0" : "0.00"), CultureInfo.CurrentCulture) + " " + units[u];
        }

        public static string Elapsed(TimeSpan? t)
        {
            if (!t.HasValue) return string.Empty;
            var v = t.Value;
            if (v.TotalSeconds < 60) return v.TotalSeconds.ToString(v.TotalSeconds < 10 ? "0.00" : "0.0", CultureInfo.CurrentCulture) + " s";
            if (v.TotalHours < 1) return string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}", (int)v.TotalMinutes, v.Seconds);
            return string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}:{2:00}", (int)v.TotalHours, v.Minutes, v.Seconds);
        }

        public static string Rate(long bytes, TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 0.5 || bytes <= 0) return string.Empty;
            return Size((long)(bytes / elapsed.TotalSeconds)) + "/s";
        }
    }

    internal static class ClipboardHelper
    {
        /// <summary>Sets clipboard text, retrying briefly when another program holds the clipboard.</summary>
        public static bool TrySetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true, 3, 100);
                    return true;
                }
                catch (ExternalException) { Thread.Sleep(100); }
            }
            return false;
        }
    }

    internal static class ListViewNative
    {
        private const int LVM_SETITEMSTATE = 0x1000 + 43;
        private const int LVIF_STATE = 0x8;
        private const int LVIS_SELECTED = 0x2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LVITEM
        {
            public int mask, iItem, iSubItem, state, stateMask;
            public IntPtr pszText;
            public int cchTextMax, iImage;
            public IntPtr lParam;
            public int iIndent, iGroupId, cColumns;
            public IntPtr puColumns, piColFmt;
            public int iGroup;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVITEM lParam);

        /// <summary>Selects or clears every item of a (virtual) ListView in one message.</summary>
        public static void SetAllSelected(ListView list, bool selected)
        {
            var item = new LVITEM { mask = LVIF_STATE, state = selected ? LVIS_SELECTED : 0, stateMask = LVIS_SELECTED };
            SendMessage(list.Handle, LVM_SETITEMSTATE, new IntPtr(-1), ref item);
        }
    }

    /// <summary>Button factory with consistent sizing and accessibility names.</summary>
    internal static class Ui
    {
        public static Button Button(string text, string role, string accessibleDescription)
        {
            var b = new Button
            {
                Text = text,
                Tag = role,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(92, 30),
                Padding = new Padding(10, 2, 10, 2),
                Margin = new Padding(0, 0, 8, 6),
                AccessibleName = text.Replace("&", string.Empty).Replace("...", string.Empty).Replace(" ▾", string.Empty),
                AccessibleDescription = accessibleDescription,
                UseMnemonic = true,
            };
            return b;
        }

        public static Label Label(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 7, 6, 0), UseMnemonic = true };
        }

        public static Font Bold(Font f, float delta)
        {
            return new Font(f.FontFamily, f.Size + delta, FontStyle.Bold, f.Unit);
        }
    }
}
