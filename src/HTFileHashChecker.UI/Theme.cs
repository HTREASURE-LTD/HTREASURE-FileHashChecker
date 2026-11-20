using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HTFileHashChecker.Core.Settings;
using HTREASURE.Branding.WinForms;

namespace HTFileHashChecker.UI
{
    /// <summary>Colours for one theme, built from the HTREASURE brand palette (HTREASURE-Branding/brand.json) and the
    /// status colours of the HTREASURE standards (SecurityTheme).</summary>
    internal sealed class Palette
    {
        public static readonly Color DeepNavy = BrandWinForms.DeepNavy;            // #0B1F3A
        public static readonly Color TechnologyBlue = BrandWinForms.TechnologyBlue; // #0B3D91
        public static readonly Color ElectricBlue = BrandWinForms.ElectricBlue;     // #168BFF

        public bool IsDark;
        public Color Window;        // main background
        public Color Surface;       // panels, list, inputs
        public Color SurfaceAlt;    // headers, hover
        public Color Text;
        public Color MutedText;
        public Color Border;
        public Color Header;        // title band
        public Color HeaderText;
        public Color Primary;
        public Color PrimaryHover;
        public Color PrimaryText;
        public Color Success;
        public Color Error;
        public Color Warning;
        public Color SuccessTint;
        public Color ErrorTint;
        public Color WarningTint;
        public Color InfoTint;

        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            Window = BrandWinForms.PageGrey,                       // #F4F6F9
            Surface = Color.White,
            SurfaceAlt = ColorTranslator.FromHtml("#EAF3FF"),       // standards BlueBg
            Text = DeepNavy,
            MutedText = BrandWinForms.MutedText,                   // #5A6577
            Border = BrandWinForms.SilverGrey,                     // #AAB4C3 (controls)
            Header = DeepNavy,
            HeaderText = Color.White,
            Primary = TechnologyBlue,
            PrimaryHover = ColorTranslator.FromHtml("#0D4AB0"),
            PrimaryText = Color.White,
            // Standards status colours (text-safe on white).
            Success = ColorTranslator.FromHtml("#067647"),
            Error = ColorTranslator.FromHtml("#B42318"),
            Warning = ColorTranslator.FromHtml("#B54708"),
            SuccessTint = ColorTranslator.FromHtml("#ECFDF3"),
            ErrorTint = ColorTranslator.FromHtml("#FEF3F2"),
            WarningTint = ColorTranslator.FromHtml("#FFF6E5"),
            InfoTint = ColorTranslator.FromHtml("#EAF3FF"),
        };

        public static readonly Palette Dark = new Palette
        {
            // Dark variant derived from Deep Navy, with the brand Edge and Silver Grey for lines and secondary text.
            IsDark = true,
            Window = ColorTranslator.FromHtml("#0A1626"),
            Surface = ColorTranslator.FromHtml("#11233D"),
            SurfaceAlt = ColorTranslator.FromHtml("#193254"),
            Text = ColorTranslator.FromHtml("#E8EDF5"),
            MutedText = BrandWinForms.SilverGrey,                  // #AAB4C3
            Border = BrandWinForms.Edge,                           // #3A5275
            Header = DeepNavy,
            HeaderText = Color.White,
            Primary = ColorTranslator.FromHtml("#1565C0"),          // brand blue lightened for contrast on dark surfaces
            PrimaryHover = ColorTranslator.FromHtml("#1E77DA"),
            PrimaryText = Color.White,
            Success = ColorTranslator.FromHtml("#5BD27A"),
            Error = ColorTranslator.FromHtml("#FF7A85"),
            Warning = ColorTranslator.FromHtml("#FFB74D"),
            SuccessTint = ColorTranslator.FromHtml("#163A22"),
            ErrorTint = ColorTranslator.FromHtml("#4A1C22"),
            WarningTint = ColorTranslator.FromHtml("#45321A"),
            InfoTint = ColorTranslator.FromHtml("#1B2F4D"),
        };

        public static Palette For(ThemePreference preference)
        {
            return preference == ThemePreference.Dark ? Dark : Light;
        }
    }

    /// <summary>Roles used to style controls consistently (set in <see cref="Control.Tag"/>).</summary>
    internal static class Role
    {
        public const string Header = "role:header";
        public const string HeaderText = "role:headertext";
        public const string Primary = "role:primary";
        public const string Secondary = "role:secondary";
        public const string Muted = "role:muted";
        public const string Card = "role:card";
        public const string Banner = "role:banner";
    }

    internal static class Theme
    {
        /// <summary>Normal application UI font (brand guidance: Segoe UI for UI, Inter for branded surfaces).</summary>
        public const string FontName = BrandFonts.ApplicationFontName;

        public static Palette Current = Palette.Light;

        public static Font BaseFont() { return new Font(FontName, 9F, FontStyle.Regular, GraphicsUnit.Point); }

        /// <summary>Applies the palette to a control tree.</summary>
        public static void Apply(Control root, Palette p)
        {
            Current = p;
            ApplyOne(root, p);
            var form = root as Form;
            if (form != null)
            {
                form.BackColor = p.Window;
                form.ForeColor = p.Text;
                if (form.MainMenuStrip != null) StyleToolStrip(form.MainMenuStrip, p);
                SetDarkTitleBar(form, p.IsDark);
            }
        }

        private static void ApplyOne(Control c, Palette p)
        {
            string role = c.Tag as string;
            if (role == Role.Header) { c.BackColor = p.Header; c.ForeColor = p.HeaderText; }
            else if (role == Role.HeaderText) { c.BackColor = Color.Transparent; c.ForeColor = p.HeaderText; }
            else if (role == Role.Primary && c is Button) StylePrimary((Button)c, p);
            else if (role == Role.Secondary && c is Button) StyleSecondary((Button)c, p);
            else if (role == Role.Muted) { c.ForeColor = p.MutedText; c.BackColor = Color.Transparent; }
            else if (role == Role.Card) { c.BackColor = p.Surface; c.ForeColor = p.Text; }
            else if (role == Role.Banner) { /* coloured by state at run time */ }
            else if (c is Button) StyleSecondary((Button)c, p);
            else if (c is TextBox || c is ComboBox || c is ListBox || c is RichTextBox || c is NumericUpDown)
            {
                c.BackColor = p.Surface;
                c.ForeColor = p.Text;
                var tb = c as TextBox;
                if (tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
                var combo = c as ComboBox;
                if (combo != null) combo.FlatStyle = p.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
            }
            else if (c is ListView)
            {
                c.BackColor = p.Surface;
                c.ForeColor = p.Text;
                SetExplorerTheme(c, p.IsDark);
            }
            else if (c is ToolStrip) StyleToolStrip((ToolStrip)c, p);
            else if (c is CheckBox || c is RadioButton || c is Label || c is GroupBox)
            {
                c.ForeColor = p.Text;
                if (!(c.Parent != null && (c.Parent.Tag as string) == Role.Header)) c.BackColor = Color.Transparent;
            }
            else if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel || c is SplitContainer || c is UserControl || c is TabPage)
            {
                c.BackColor = p.Window;
                c.ForeColor = p.Text;
            }

            if (c.ContextMenuStrip != null) StyleToolStrip(c.ContextMenuStrip, p);
            foreach (Control child in c.Controls) ApplyOne(child, p);
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, object> Hooked =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Button, object>();

        /// <summary>WinForms paints disabled flat buttons in system grey, which is unreadable on dark backgrounds.
        /// Repaint the disabled state with palette colours.</summary>
        private static void HookDisabledPaint(Button b)
        {
            object marker;
            if (Hooked.TryGetValue(b, out marker)) return;
            Hooked.Add(b, new object());
            b.EnabledChanged += (s, e) => b.Invalidate();
            b.Paint += (s, e) =>
            {
                if (b.Enabled) return;
                var p = Current;
                var r = b.ClientRectangle;
                using (var back = new SolidBrush(p.Window))
                using (var border = new Pen(p.Border))
                {
                    e.Graphics.FillRectangle(back, r);
                    e.Graphics.DrawRectangle(border, 0, 0, r.Width - 1, r.Height - 1);
                }
                TextRenderer.DrawText(e.Graphics, b.Text, b.Font, r, p.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.HidePrefix);
            };
        }

        public static void StylePrimary(Button b, Palette p)
        {
            HookDisabledPaint(b);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = p.PrimaryHover;
            b.FlatAppearance.MouseDownBackColor = p.Primary;
            b.BackColor = p.Primary;
            b.ForeColor = p.PrimaryText;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
        }

        public static void StyleSecondary(Button b, Palette p)
        {
            HookDisabledPaint(b);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = p.Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = p.SurfaceAlt;
            b.FlatAppearance.MouseDownBackColor = p.SurfaceAlt;
            b.BackColor = p.Surface;
            b.ForeColor = p.Text;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
        }

        public static void StyleToolStrip(ToolStrip strip, Palette p)
        {
            strip.Renderer = new ToolStripProfessionalRenderer(new PaletteColorTable(p)) { RoundedEdges = false };
            strip.BackColor = p.IsDark ? p.Surface : Color.White;
            strip.ForeColor = p.Text;
            foreach (ToolStripItem item in strip.Items) StyleItem(item, p);
        }

        private static void StyleItem(ToolStripItem item, Palette p)
        {
            item.ForeColor = p.Text;
            var drop = item as ToolStripDropDownItem;
            if (drop != null)
            {
                drop.DropDown.BackColor = p.Surface;
                drop.DropDown.ForeColor = p.Text;
                foreach (ToolStripItem child in drop.DropDownItems) StyleItem(child, p);
            }
        }

        /// <summary>Product icon from the HTREASURE branding kit (shared instance; do not dispose).</summary>
        public static Icon LoadAppIcon()
        {
            return BrandWinForms.ProductIcon;
        }

        // ---- Native theming helpers (best effort; ignored on Windows versions without support)

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void SetExplorerTheme(Control c, bool dark)
        {
            if (!c.IsHandleCreated) { c.HandleCreated += (s, e) => SetExplorerTheme(c, Current.IsDark); return; }
            try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static void SetDarkTitleBar(Form form, bool dark)
        {
            if (Environment.OSVersion.Version.Major < 10) return;
            if (!form.IsHandleCreated) { form.HandleCreated += (s, e) => SetDarkTitleBar(form, Current.IsDark); return; }
            try
            {
                int value = dark ? 1 : 0;
                // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 20H1+ and 11, 19 on older Windows 10 builds.
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private sealed class PaletteColorTable : ProfessionalColorTable
        {
            private readonly Palette p;
            public PaletteColorTable(Palette p) { this.p = p; UseSystemColors = false; }
            private Color Bar { get { return p.IsDark ? p.Surface : Color.White; } }
            public override Color MenuStripGradientBegin { get { return Bar; } }
            public override Color MenuStripGradientEnd { get { return Bar; } }
            public override Color ToolStripDropDownBackground { get { return p.Surface; } }
            public override Color ImageMarginGradientBegin { get { return p.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return p.Surface; } }
            public override Color ImageMarginGradientEnd { get { return p.Surface; } }
            public override Color MenuItemSelected { get { return p.SurfaceAlt; } }
            public override Color MenuItemSelectedGradientBegin { get { return p.SurfaceAlt; } }
            public override Color MenuItemSelectedGradientEnd { get { return p.SurfaceAlt; } }
            public override Color MenuItemPressedGradientBegin { get { return p.SurfaceAlt; } }
            public override Color MenuItemPressedGradientEnd { get { return p.SurfaceAlt; } }
            public override Color MenuItemBorder { get { return p.Border; } }
            public override Color MenuBorder { get { return p.Border; } }
            public override Color SeparatorDark { get { return p.Border; } }
            public override Color SeparatorLight { get { return p.Border; } }
            public override Color CheckBackground { get { return p.SurfaceAlt; } }
            public override Color CheckSelectedBackground { get { return p.SurfaceAlt; } }
            public override Color CheckPressedBackground { get { return p.SurfaceAlt; } }
            public override Color StatusStripGradientBegin { get { return p.Window; } }
            public override Color StatusStripGradientEnd { get { return p.Window; } }
            public override Color ToolStripBorder { get { return p.Border; } }
            public override Color ToolStripGradientBegin { get { return Bar; } }
            public override Color ToolStripGradientMiddle { get { return Bar; } }
            public override Color ToolStripGradientEnd { get { return Bar; } }
        }
    }
}
