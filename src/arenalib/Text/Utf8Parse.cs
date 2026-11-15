using System;

namespace Arenas
{
    /// <summary>
    /// Strict UTF-8 number parsing. No leading/trailing whitespace is tolerated, the whole
    /// view must be consumed, and overflow returns false rather than wrapping.
    /// </summary>
    public static unsafe class Utf8Parse
    {
        // zerolib's primitives only define Int32/Int64 Min/Max, so spell the unsigned
        // limits out here.
        private const ulong UInt64Max = 18446744073709551615UL;
        private const uint UInt32Max = 4294967295u;

        public static bool TryParseUInt64(Utf8View text, out ulong value)
            => TryParseUInt64(text, 10, out value);

        public static bool TryParseUInt64(Utf8View text, int radix, out ulong value)
        {
            value = 0;

            if (radix < 2 || radix > 36 || text.Length == 0)
                return false;

            ulong limit = UInt64Max / (ulong)radix;
            ulong result = 0;

            for (int i = 0; i < text.Length; i++)
            {
                int digit = DigitValue(text[i]);
                if (digit < 0 || digit >= radix)
                    return false;

                if (result > limit)
                    return false;

                result *= (ulong)radix;

                if (result > UInt64Max - (ulong)digit)
                    return false;

                result += (ulong)digit;
            }

            value = result;
            return true;
        }

        public static bool TryParseInt64(Utf8View text, out long value)
            => TryParseInt64(text, 10, out value);

        public static bool TryParseInt64(Utf8View text, int radix, out long value)
        {
            value = 0;

            if (text.Length == 0)
                return false;

            bool negative = false;
            Utf8View digits = text;

            byte first = text[0];
            if (first == (byte)'-' || first == (byte)'+')
            {
                negative = first == (byte)'-';
                digits = text.Slice(1);
                if (digits.Length == 0)
                    return false;
            }

            if (!TryParseUInt64(digits, radix, out ulong magnitude))
                return false;

            if (negative)
            {
                // long.MinValue has magnitude 2^63, which is not representable as a positive long.
                if (magnitude > 9223372036854775808UL)
                    return false;

                value = magnitude == 9223372036854775808UL ? long.MinValue : -(long)magnitude;
                return true;
            }

            if (magnitude > (ulong)long.MaxValue)
                return false;

            value = (long)magnitude;
            return true;
        }

        public static bool TryParseInt32(Utf8View text, out int value)
        {
            value = 0;

            if (!TryParseInt64(text, 10, out long wide))
                return false;

            if (wide < int.MinValue || wide > int.MaxValue)
                return false;

            value = (int)wide;
            return true;
        }

        public static bool TryParseUInt32(Utf8View text, out uint value)
        {
            value = 0;

            if (!TryParseUInt64(text, 10, out ulong wide))
                return false;

            if (wide > UInt32Max)
                return false;

            value = (uint)wide;
            return true;
        }

        /// <summary>Parse hexadecimal, with an optional "0x" or "0X" prefix.</summary>
        public static bool TryParseHex64(Utf8View text, out ulong value)
        {
            Utf8View digits = text;
            if (text.Length > 2 && text[0] == (byte)'0' && (text[1] == (byte)'x' || text[1] == (byte)'X'))
                digits = text.Slice(2);

            return TryParseUInt64(digits, 16, out value);
        }

        public static bool TryParseHex32(Utf8View text, out uint value)
        {
            value = 0;

            if (!TryParseHex64(text, out ulong wide) || wide > UInt32Max)
                return false;

            value = (uint)wide;
            return true;
        }

        public static bool TryParseBool(Utf8View text, out bool value)
        {
            value = false;

            if (text.EqualsAsciiIgnoreCase(Utf8View.FromLiteral("true"u8)))
            {
                value = true;
                return true;
            }

            if (text.EqualsAsciiIgnoreCase(Utf8View.FromLiteral("false"u8)))
            {
                value = false;
                return true;
            }

            return false;
        }

        private static int DigitValue(byte character)
        {
            if (character >= (byte)'0' && character <= (byte)'9')
                return character - (byte)'0';
            if (character >= (byte)'a' && character <= (byte)'z')
                return character - (byte)'a' + 10;
            if (character >= (byte)'A' && character <= (byte)'Z')
                return character - (byte)'A' + 10;

            return -1;
        }
    }
}
