using System;
using System.Runtime.CompilerServices;

namespace HTFileHashChecker.Core.Hashing
{
    /// <summary>Hexadecimal encoding and constant-time digest comparison.</summary>
    public static class Hex
    {
        private const string Digits = "0123456789abcdef";

        /// <summary>Lower-case hexadecimal string (the GNU coreutils convention).</summary>
        public static string ToHex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            var chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = Digits[bytes[i] >> 4];
                chars[i * 2 + 1] = Digits[bytes[i] & 0xF];
            }
            return new string(chars);
        }

        public static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        /// <summary>Decodes an even-length hexadecimal string (either letter case). No whitespace is accepted.</summary>
        public static bool TryParse(string hex, out byte[] bytes)
        {
            bytes = null;
            if (hex == null || hex.Length % 2 != 0) return false;
            var result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = Value(hex[i * 2]);
                int lo = Value(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return false;
                result[i] = (byte)((hi << 4) | lo);
            }
            bytes = result;
            return true;
        }

        private static int Value(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// <summary>Compares two digests in time that depends only on their length, not on where they differ.</summary>
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null) return false;
            if (left.Length != right.Length) return false;
            int diff = 0;
            for (int i = 0; i < left.Length; i++)
                diff |= left[i] ^ right[i];
            return diff == 0;
        }
    }
}
