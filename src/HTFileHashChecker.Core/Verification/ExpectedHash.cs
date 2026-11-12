using System;
using System.Text;
using HTFileHashChecker.Core.Hashing;

namespace HTFileHashChecker.Core.Verification
{
    /// <summary>A validated expected hash supplied by the user or a manifest.</summary>
    public sealed class ExpectedHash
    {
        private readonly byte[] bytes;

        private ExpectedHash(HashAlgorithmKind algorithm, byte[] bytes)
        {
            Algorithm = algorithm;
            this.bytes = bytes;
            Hex = Hashing.Hex.ToHex(bytes);
        }

        public HashAlgorithmKind Algorithm { get; private set; }

        /// <summary>Normalised lower-case hexadecimal value.</summary>
        public string Hex { get; private set; }

        public byte[] GetBytes() { return (byte[])bytes.Clone(); }

        /// <summary>Constant-time comparison with a calculated digest.</summary>
        public bool Matches(byte[] calculated)
        {
            return Hashing.Hex.FixedTimeEquals(bytes, calculated);
        }

        public static ExpectedHash FromBytes(HashAlgorithmKind algorithm, byte[] digest)
        {
            if (digest == null) throw new ArgumentNullException("digest");
            if (digest.Length != HashAlgorithms.Get(algorithm).DigestBytes)
                throw new ArgumentException("Digest length does not match " + HashAlgorithms.Get(algorithm).DisplayName + ".", "digest");
            return new ExpectedHash(algorithm, (byte[])digest.Clone());
        }

        /// <summary>
        /// Normalises and validates user input: removes all whitespace (including line breaks and non-breaking or
        /// zero-width spaces picked up when copying from web pages), an optional "0x" prefix, and ignores letter case.
        /// When <paramref name="algorithm"/> is null the algorithm is detected from the length.
        /// </summary>
        public static ExpectedHashParseResult Parse(string input, HashAlgorithmKind? algorithm)
        {
            if (input == null || input.Trim().Length == 0)
                return ExpectedHashParseResult.Fail("Enter the expected hash.");

            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (char.IsWhiteSpace(c) || c == '​' || c == '‌' || c == '‍' || c == '﻿' || c == '⁠')
                    continue;
                sb.Append(c);
            }
            string s = sb.ToString();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length == 0) return ExpectedHashParseResult.Fail("Enter the expected hash.");

            for (int i = 0; i < s.Length; i++)
            {
                if (!Hashing.Hex.IsHexDigit(s[i]))
                {
                    return ExpectedHashParseResult.Fail(
                        "The expected hash contains a character that is not hexadecimal ('" + Printable(s[i]) + "' at position "
                        + (i + 1) + "). A hash contains only the digits 0-9 and letters a-f. Paste only the hash value, not the file name.");
                }
            }

            HashAlgorithmKind kind;
            if (algorithm.HasValue)
            {
                kind = algorithm.Value;
                var info = HashAlgorithms.Get(kind);
                if (s.Length != info.HexLength)
                {
                    HashAlgorithmKind other;
                    string hint = HashAlgorithms.TryDetectFromHexLength(s.Length, out other)
                        ? " Its length matches " + HashAlgorithms.Get(other).DisplayName + "."
                        : string.Empty;
                    return ExpectedHashParseResult.Fail(
                        "The expected hash has " + s.Length + " characters, but a " + info.DisplayName + " hash has exactly "
                        + info.HexLength + "." + hint);
                }
            }
            else if (!HashAlgorithms.TryDetectFromHexLength(s.Length, out kind))
            {
                return ExpectedHashParseResult.Fail(
                    "The expected hash has " + s.Length + " characters, which does not match any supported algorithm "
                    + "(SHA-256: 64, SHA-512: 128, SHA-384: 96, SHA-1: 40, MD5: 32).");
            }

            byte[] bytes;
            if (!Hashing.Hex.TryParse(s, out bytes))
                return ExpectedHashParseResult.Fail("The expected hash is not valid hexadecimal.");

            return ExpectedHashParseResult.Ok(new ExpectedHash(kind, bytes));
        }

        private static string Printable(char c)
        {
            return char.IsControl(c) ? "U+" + ((int)c).ToString("X4") : c.ToString();
        }
    }

    public sealed class ExpectedHashParseResult
    {
        private ExpectedHashParseResult() { }

        public bool IsValid { get { return Value != null; } }
        public ExpectedHash Value { get; private set; }
        public string Error { get; private set; }

        internal static ExpectedHashParseResult Ok(ExpectedHash value) { return new ExpectedHashParseResult { Value = value }; }
        internal static ExpectedHashParseResult Fail(string error) { return new ExpectedHashParseResult { Error = error }; }
    }
}
