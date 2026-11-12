using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace HTFileHashChecker.Core.Hashing
{
    /// <summary>Supported hash algorithms. SHA-256 is the default and recommended choice.</summary>
    public enum HashAlgorithmKind
    {
        Sha256 = 0,
        Sha512 = 1,
        Sha384 = 2,
        Sha1 = 3,
        Md5 = 4,
    }

    /// <summary>Static description of one algorithm.</summary>
    public sealed class HashAlgorithmInfo
    {
        internal HashAlgorithmInfo(HashAlgorithmKind kind, string displayName, string gnuName, string bsdTag,
            int digestBytes, bool isLegacy, string manifestFileName)
        {
            Kind = kind;
            DisplayName = displayName;
            GnuName = gnuName;
            BsdTag = bsdTag;
            DigestBytes = digestBytes;
            IsLegacy = isLegacy;
            ManifestFileName = manifestFileName;
        }

        public HashAlgorithmKind Kind { get; private set; }

        /// <summary>Name shown to users, e.g. "SHA-256".</summary>
        public string DisplayName { get; private set; }

        /// <summary>Lower-case tool name used by GNU coreutils, e.g. "sha256".</summary>
        public string GnuName { get; private set; }

        /// <summary>Tag used by the BSD / "--tag" manifest format, e.g. "SHA256".</summary>
        public string BsdTag { get; private set; }

        public int DigestBytes { get; private set; }

        public int HexLength { get { return DigestBytes * 2; } }

        /// <summary>True for MD5 and SHA-1, which must not be relied on for security-sensitive authenticity checks.</summary>
        public bool IsLegacy { get; private set; }

        /// <summary>Conventional manifest file name, e.g. "SHA256SUMS".</summary>
        public string ManifestFileName { get; private set; }

        /// <summary>Conventional single-file checksum extension, e.g. ".sha256".</summary>
        public string FileExtension { get { return "." + GnuName; } }

        public string SecurityNote
        {
            get
            {
                return IsLegacy
                    ? DisplayName + " is a legacy algorithm with known collision attacks. Use it only to compare against a published "
                      + DisplayName + " value or to detect accidental corruption, never as proof that a file is authentic."
                    : DisplayName + " is suitable for integrity verification against a trusted expected value.";
            }
        }

        public override string ToString() { return DisplayName; }
    }

    /// <summary>Raised when Windows refuses to provide an algorithm (for example MD5 under a FIPS-only policy).</summary>
    [Serializable]
    public sealed class HashAlgorithmUnavailableException : Exception
    {
        public HashAlgorithmUnavailableException(string message, Exception inner) : base(message, inner) { }

        private HashAlgorithmUnavailableException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    public static class HashAlgorithms
    {
        private static readonly HashAlgorithmInfo[] AllInfo =
        {
            new HashAlgorithmInfo(HashAlgorithmKind.Sha256, "SHA-256", "sha256", "SHA256", 32, false, "SHA256SUMS"),
            new HashAlgorithmInfo(HashAlgorithmKind.Sha512, "SHA-512", "sha512", "SHA512", 64, false, "SHA512SUMS"),
            new HashAlgorithmInfo(HashAlgorithmKind.Sha384, "SHA-384", "sha384", "SHA384", 48, false, "SHA384SUMS"),
            new HashAlgorithmInfo(HashAlgorithmKind.Sha1, "SHA-1", "sha1", "SHA1", 20, true, "SHA1SUMS"),
            new HashAlgorithmInfo(HashAlgorithmKind.Md5, "MD5", "md5", "MD5", 16, true, "MD5SUMS"),
        };

        public const HashAlgorithmKind Default = HashAlgorithmKind.Sha256;

        /// <summary>All algorithms in display order (recommended first).</summary>
        public static IReadOnlyList<HashAlgorithmInfo> All { get { return AllInfo; } }

        public static HashAlgorithmInfo Get(HashAlgorithmKind kind)
        {
            foreach (var info in AllInfo)
                if (info.Kind == kind) return info;
            throw new ArgumentOutOfRangeException("kind", kind, "Unknown hash algorithm.");
        }

        public static bool IsDefined(HashAlgorithmKind kind)
        {
            foreach (var info in AllInfo)
                if (info.Kind == kind) return true;
            return false;
        }

        /// <summary>Parses "SHA-256", "sha256", "SHA256" and similar spellings.</summary>
        public static bool TryParse(string name, out HashAlgorithmKind kind)
        {
            kind = Default;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string key = name.Trim().Replace("-", string.Empty).Replace("_", string.Empty);
            foreach (var info in AllInfo)
            {
                if (string.Equals(key, info.GnuName, StringComparison.OrdinalIgnoreCase))
                {
                    kind = info.Kind;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Returns the algorithm whose hexadecimal digest length is <paramref name="hexLength"/>, if any.
        /// All supported algorithms have distinct lengths, so detection is unambiguous.</summary>
        public static bool TryDetectFromHexLength(int hexLength, out HashAlgorithmKind kind)
        {
            kind = Default;
            foreach (var info in AllInfo)
            {
                if (info.HexLength == hexLength)
                {
                    kind = info.Kind;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Infers the algorithm from a manifest file name such as "SHA256SUMS", "sha512sums.txt",
        /// "MD5SUMS" or "setup.exe.sha256".</summary>
        public static bool TryDetectFromFileName(string fileName, out HashAlgorithmKind kind)
        {
            kind = Default;
            if (string.IsNullOrEmpty(fileName)) return false;
            string name = Path.GetFileName(fileName).ToLowerInvariant();
            string ext = Path.GetExtension(name);
            // Longest names first so "sha512" is never mistaken for "sha5…" style prefixes of others.
            foreach (var info in new[] { Get(HashAlgorithmKind.Sha512), Get(HashAlgorithmKind.Sha384), Get(HashAlgorithmKind.Sha256), Get(HashAlgorithmKind.Sha1), Get(HashAlgorithmKind.Md5) })
            {
                if (ext == info.FileExtension || name.StartsWith(info.GnuName + "sum", StringComparison.Ordinal)
                    || name.Contains("." + info.GnuName + "sum") || ext == "." + info.GnuName + "sum")
                {
                    kind = info.Kind;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Creates a new instance of Microsoft's built-in implementation. The caller disposes it.</summary>
        /// <remarks>The CryptoServiceProvider classes use the Windows cryptographic providers (CAPI/CNG), which are
        /// FIPS-validated and, on modern CPUs, hardware accelerated. No custom cryptography is implemented.</remarks>
        public static HashAlgorithm Create(HashAlgorithmKind kind)
        {
            try
            {
                switch (kind)
                {
                    case HashAlgorithmKind.Sha256: return new SHA256CryptoServiceProvider();
                    case HashAlgorithmKind.Sha512: return new SHA512CryptoServiceProvider();
                    case HashAlgorithmKind.Sha384: return new SHA384CryptoServiceProvider();
                    case HashAlgorithmKind.Sha1: return new SHA1CryptoServiceProvider();
                    case HashAlgorithmKind.Md5: return new MD5CryptoServiceProvider();
                    default: throw new ArgumentOutOfRangeException("kind", kind, "Unknown hash algorithm.");
                }
            }
            catch (InvalidOperationException ex)
            {
                throw Unavailable(kind, ex);
            }
            catch (TargetInvocationException ex)
            {
                throw Unavailable(kind, ex);
            }
            catch (CryptographicException ex)
            {
                throw Unavailable(kind, ex);
            }
        }

        private static Exception Unavailable(HashAlgorithmKind kind, Exception ex)
        {
            return new HashAlgorithmUnavailableException(
                Get(kind).DisplayName + " is not available on this computer. Windows may be configured to allow only "
                + "FIPS-approved algorithms (MD5 is not FIPS-approved). Choose SHA-256 instead.", ex);
        }
    }
}
