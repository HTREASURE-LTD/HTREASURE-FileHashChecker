using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HTREASURE.Branding
{
    /// <summary>Corporate logo variants available as embedded raster images.</summary>
    public enum LogoVariant
    {
        HorizontalColor,
        HorizontalColorDark,
        Monogram,
    }

    /// <summary>Corporate palette as hex strings (#RRGGBB), from brand.json.</summary>
    public static class BrandColors
    {
        public const string DeepNavy = "#0B1F3A";
        public const string TechnologyBlue = "#0B3D91";
        public const string ElectricBlue = "#168BFF";
        public const string White = "#FFFFFF";
        public const string SilverGrey = "#AAB4C3";

        public const string Edge = "#3A5275";
        public const string PageGrey = "#F4F6F9";
        public const string LineGrey = "#DDE3EB";
        public const string MutedText = "#5A6577";

        /// <summary>Parses #RRGGBB, #AARRGGBB and the "rgba(r,g,b,a)" form used in brand.json.</summary>
        public static void Parse(string colour, out byte a, out byte r, out byte g, out byte b)
        {
            colour = colour.Trim();
            if (colour.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            {
                var parts = colour.Substring(5, colour.Length - 6).Split(',').Select(p => p.Trim()).ToArray();
                a = (byte)Math.Round(float.Parse(parts[3], CultureInfo.InvariantCulture) * 255);
                r = byte.Parse(parts[0], CultureInfo.InvariantCulture);
                g = byte.Parse(parts[1], CultureInfo.InvariantCulture);
                b = byte.Parse(parts[2], CultureInfo.InvariantCulture);
                return;
            }
            string hex = colour.TrimStart('#');
            uint value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            a = hex.Length == 8 ? (byte)(value >> 24) : (byte)255;
            r = (byte)(value >> 16);
            g = (byte)(value >> 8);
            b = (byte)value;
        }
    }

    /// <summary>Brand assets compiled into this assembly from the HTREASURE-Branding kit. Nothing is read from disk.</summary>
    public static class BrandAssets
    {
        private const string AssetPrefix = "HTREASURE.Assets/";
        private static readonly Assembly Library = typeof(BrandAssets).Assembly;

        public const string CorporateIcon = "Icons/Windows/HTREASURE.ico";
        public const string CorporateIconPng = "Icons/Desktop/HTREASURE-icon-256.png";
        public const string InterRegular = "Fonts/Inter-Regular.ttf";
        public const string InterSemiBold = "Fonts/Inter-SemiBold.ttf";
        public const string InterLicence = "Fonts/OFL-LICENSE.txt";

        public static Stream Open(string path)
        {
            var stream = Library.GetManifestResourceStream(AssetPrefix + path.Replace('\\', '/'));
            if (stream == null) throw new FileNotFoundException("HTREASURE brand asset '" + path + "' is not embedded.", path);
            return stream;
        }

        public static bool Exists(string path)
        {
            return Library.GetManifestResourceInfo(AssetPrefix + path.Replace('\\', '/')) != null;
        }

        public static IEnumerable<string> All()
        {
            return Library.GetManifestResourceNames().Where(n => n.StartsWith(AssetPrefix, StringComparison.Ordinal))
                .Select(n => n.Substring(AssetPrefix.Length));
        }

        /// <summary>Smallest embedded image of the variant that is at least <paramref name="minPixelWidth"/> wide.</summary>
        public static string LogoPath(LogoVariant variant, int minPixelWidth)
        {
            string[] paths;
            int[] widths;
            switch (variant)
            {
                case LogoVariant.HorizontalColorDark:
                    paths = new[] { "Logos/Horizontal/HTREASURE-logo-horizontal-color-dark-1024w.png", "Logos/Horizontal/HTREASURE-logo-horizontal-color-dark-2048w.png" };
                    widths = new[] { 1024, 2048 };
                    break;
                case LogoVariant.Monogram:
                    paths = new[] { "Logos/Monogram/HTREASURE-monogram-color-256.png", "Logos/Monogram/HTREASURE-monogram-color-512.png" };
                    widths = new[] { 256, 512 };
                    break;
                default:
                    paths = new[] { "Logos/Horizontal/HTREASURE-logo-horizontal-color-1024w.png", "Logos/Horizontal/HTREASURE-logo-horizontal-color-2048w.png" };
                    widths = new[] { 1024, 2048 };
                    break;
            }
            for (int i = 0; i < paths.Length; i++)
                if (widths[i] >= minPixelWidth) return paths[i];
            return paths[paths.Length - 1];
        }

        public static byte[] ReadAllBytes(string path)
        {
            using (var stream = Open(path))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        public static string ReadText(string path)
        {
            return new UTF8Encoding(false).GetString(ReadAllBytes(path)).TrimStart('﻿');
        }
    }

    /// <summary>Typed view of brand.json, the kit's single source of truth.</summary>
    public sealed class BrandConfig
    {
        public string CompanyName { get; internal set; }
        public string RegistrationNumber { get; internal set; }
        public string Jurisdiction { get; internal set; }
        public string RegisteredAddress { get; internal set; }
        public string Publisher { get; internal set; }
        public string Attribution { get; internal set; }
        public string CopyrightTemplate { get; internal set; }
        public bool CodeSigningConfigured { get; internal set; }
        public int[] SplashDesignSize { get; internal set; }
        public SplashText ProductName { get; internal set; }
        public SplashText Version { get; internal set; }
        public SplashBar ProgressBar { get; internal set; }
        public SplashText Footer { get; internal set; }

        public sealed class SplashText
        {
            public float X, Y, Size;
            public string Weight, Color, Format;
        }

        public sealed class SplashBar
        {
            public float X, Y, Width, Height;
            public string Track, Fill;
        }

        internal static BrandConfig Load()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
            var root = (IDictionary<string, object>)serializer.DeserializeObject(BrandAssets.ReadText("brand.json"));
            var company = Obj(root, "company");
            var splash = Obj(root, "splash");
            var layout = Obj(splash, "layout");
            return new BrandConfig
            {
                CompanyName = Str(company, "name"),
                RegistrationNumber = Str(company, "registrationNumber"),
                Jurisdiction = Str(company, "jurisdiction"),
                RegisteredAddress = Str(company, "registeredAddress"),
                Publisher = Str(root, "publisher"),
                Attribution = Str(root, "attribution"),
                CopyrightTemplate = Str(Obj(root, "copyright"), "template"),
                CodeSigningConfigured = Obj(root, "codeSigning")["configured"] is bool && (bool)Obj(root, "codeSigning")["configured"],
                SplashDesignSize = ((IEnumerable)splash["designSize"]).Cast<object>().Select(o => Convert.ToInt32(o, CultureInfo.InvariantCulture)).ToArray(),
                ProductName = Text(Obj(layout, "productName")),
                Version = Text(Obj(layout, "version")),
                Footer = Text(Obj(layout, "footer")),
                ProgressBar = Bar(Obj(layout, "progressBar")),
            };
        }

        private static IDictionary<string, object> Obj(IDictionary<string, object> d, string key)
        {
            return (IDictionary<string, object>)d[key];
        }

        private static string Str(IDictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as string : null;
        }

        private static float Num(IDictionary<string, object> d, string key)
        {
            return Convert.ToSingle(d[key], CultureInfo.InvariantCulture);
        }

        private static SplashText Text(IDictionary<string, object> d)
        {
            return new SplashText { X = Num(d, "x"), Y = Num(d, "y"), Size = Num(d, "size"), Weight = Str(d, "weight"), Color = Str(d, "color"), Format = Str(d, "format") };
        }

        private static SplashBar Bar(IDictionary<string, object> d)
        {
            return new SplashBar { X = Num(d, "x"), Y = Num(d, "y"), Width = Num(d, "width"), Height = Num(d, "height"), Track = Str(d, "track"), Fill = Str(d, "fill") };
        }
    }

    /// <summary>
    /// Corporate identity (from the embedded brand.json) and the running product's identity (from its assembly
    /// attributes), for About windows, splash screens, footers, reports and logs.
    /// </summary>
    public static class BrandInfo
    {
        private static readonly Lazy<BrandConfig> Loaded = new Lazy<BrandConfig>(BrandConfig.Load);
        private static readonly Regex YearPattern = new Regex(@"\b(19|20)\d{2}\b", RegexOptions.CultureInvariant);

        /// <summary>The assembly that identifies the product. Defaults to the entry assembly.</summary>
        public static Assembly ProductAssembly { get; set; }

        private static Assembly Entry { get { return ProductAssembly ?? Assembly.GetEntryAssembly(); } }

        public static BrandConfig Config { get { return Loaded.Value; } }

        public static string CompanyName { get { return Config.CompanyName; } }
        public static string RegistrationNumber { get { return Config.RegistrationNumber; } }
        public static string RegisteredAddress { get { return Config.RegisteredAddress; } }
        public static string Jurisdiction { get { return Config.Jurisdiction; } }
        public static string Publisher { get { return Config.Publisher; } }

        /// <summary>"An HTREASURE LTD product".</summary>
        public static string Attribution { get { return Config.Attribution; } }

        /// <summary>"Company No. 17178941 · Registered in England and Wales".</summary>
        public static string RegistrationLine { get { return "Company No. " + RegistrationNumber + " · Registered in " + Jurisdiction; } }

        /// <summary>"Copyright © {year} HTREASURE LTD. All rights reserved." with the year stamped at build time.</summary>
        public static string Copyright { get { return CopyrightFor(CopyrightYear); } }

        /// <summary>"© 2026 HTREASURE LTD".</summary>
        public static string ShortCopyright { get { return "© " + CopyrightYear.ToString(CultureInfo.InvariantCulture) + " " + CompanyName; } }

        public static int CopyrightYear
        {
            get
            {
                var entry = Entry;
                var attr = entry == null ? null : (AssemblyCopyrightAttribute)Attribute.GetCustomAttribute(entry, typeof(AssemblyCopyrightAttribute));
                var match = attr == null ? null : YearPattern.Match(attr.Copyright);
                return match != null && match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : DateTime.Now.Year;
            }
        }

        public static string CopyrightFor(int year)
        {
            return Config.CopyrightTemplate.Replace("{year}", year.ToString(CultureInfo.InvariantCulture));
        }

        public static string ProductName
        {
            get
            {
                var entry = Entry;
                var attr = entry == null ? null : (AssemblyProductAttribute)Attribute.GetCustomAttribute(entry, typeof(AssemblyProductAttribute));
                return attr != null ? attr.Product : (entry != null ? entry.GetName().Name : CompanyName);
            }
        }

        public static string ProductVersion
        {
            get
            {
                var entry = Entry;
                var attr = entry == null ? null : (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(entry, typeof(AssemblyInformationalVersionAttribute));
                if (attr != null && !string.IsNullOrEmpty(attr.InformationalVersion)) return attr.InformationalVersion.Split('+')[0];
                return entry != null ? entry.GetName().Version.ToString(3) : "1.0.0";
            }
        }

        public static string ProductDescription
        {
            get
            {
                var entry = Entry;
                var attr = entry == null ? null : (AssemblyDescriptionAttribute)Attribute.GetCustomAttribute(entry, typeof(AssemblyDescriptionAttribute));
                return attr != null ? attr.Description : null;
            }
        }

        /// <summary>True only when a code-signing certificate has been configured in brand.json.</summary>
        public static bool IsCodeSigningConfigured { get { return Config.CodeSigningConfigured; } }
    }
}
