using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// UTF-8 encoding and decoding. Strict by default: overlong forms, surrogates and
    /// out-of-range scalars are rejected rather than silently accepted.
    /// </summary>
    public static unsafe class Utf8
    {
        public const uint ReplacementChar = 0xFFFD;
        public const uint MaxScalar = 0x10FFFF;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidScalar(uint scalar)
            => scalar <= MaxScalar && (scalar < 0xD800 || scalar > 0xDFFF);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsContinuation(byte value)
            => (value & 0xC0) == 0x80;

        /// <summary>Bytes needed to encode a scalar, or 0 if it is not encodable.</summary>
        public static int GetScalarByteCount(uint scalar)
        {
            if (scalar < 0x80)
                return 1;
            if (scalar < 0x800)
                return 2;
            if (scalar < 0x10000)
                return scalar >= 0xD800 && scalar <= 0xDFFF ? 0 : 3;
            if (scalar <= MaxScalar)
                return 4;
            return 0;
        }

        /// <summary>Encode one scalar. Returns bytes written, or 0 if invalid or out of room.</summary>
        public static int EncodeScalar(uint scalar, byte* destination, int capacity)
        {
            int needed = GetScalarByteCount(scalar);
            if (needed == 0 || needed > capacity)
                return 0;

            switch (needed)
            {
                case 1:
                    destination[0] = (byte)scalar;
                    break;
                case 2:
                    destination[0] = (byte)(0xC0 | (scalar >> 6));
                    destination[1] = (byte)(0x80 | (scalar & 0x3F));
                    break;
                case 3:
                    destination[0] = (byte)(0xE0 | (scalar >> 12));
                    destination[1] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
                    destination[2] = (byte)(0x80 | (scalar & 0x3F));
                    break;
                default:
                    destination[0] = (byte)(0xF0 | (scalar >> 18));
                    destination[1] = (byte)(0x80 | ((scalar >> 12) & 0x3F));
                    destination[2] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
                    destination[3] = (byte)(0x80 | (scalar & 0x3F));
                    break;
            }

            return needed;
        }

        /// <summary>
        /// Decode one scalar. On invalid input returns false with <paramref name="scalar"/> set
        /// to U+FFFD and <paramref name="consumed"/> set to 1, so callers can resynchronize.
        /// </summary>
        public static bool TryDecodeScalar(byte* source, int available, out uint scalar, out int consumed)
        {
            scalar = ReplacementChar;
            consumed = 1;

            if (available <= 0)
            {
                consumed = 0;
                return false;
            }

            byte b0 = source[0];

            if (b0 < 0x80)
            {
                scalar = b0;
                return true;
            }

            if (b0 < 0xC2)          // 0x80-0xBF are stray continuations; 0xC0/0xC1 are overlong
                return false;

            if (b0 < 0xE0)
            {
                if (available < 2 || !IsContinuation(source[1]))
                    return false;

                scalar = ((uint)(b0 & 0x1F) << 6) | (uint)(source[1] & 0x3F);
                consumed = 2;
                return true;
            }

            if (b0 < 0xF0)
            {
                if (available < 3 || !IsContinuation(source[1]) || !IsContinuation(source[2]))
                    return false;

                uint value = ((uint)(b0 & 0x0F) << 12)
                           | ((uint)(source[1] & 0x3F) << 6)
                           | (uint)(source[2] & 0x3F);

                if (value < 0x800)                        // overlong
                    return false;
                if (value >= 0xD800 && value <= 0xDFFF)   // surrogate
                    return false;

                scalar = value;
                consumed = 3;
                return true;
            }

            if (b0 < 0xF5)
            {
                if (available < 4 || !IsContinuation(source[1]) || !IsContinuation(source[2]) || !IsContinuation(source[3]))
                    return false;

                uint value = ((uint)(b0 & 0x07) << 18)
                           | ((uint)(source[1] & 0x3F) << 12)
                           | ((uint)(source[2] & 0x3F) << 6)
                           | (uint)(source[3] & 0x3F);

                if (value < 0x10000 || value > MaxScalar)
                    return false;

                scalar = value;
                consumed = 4;
                return true;
            }

            return false;
        }

        /// <summary>True if the whole buffer is well-formed UTF-8.</summary>
        public static bool IsWellFormed(byte* source, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                if (!TryDecodeScalar(source + offset, length - offset, out _, out int consumed))
                    return false;
                offset += consumed;
            }

            return true;
        }

        // ---- UTF-16 interop (C# string literals are UTF-16) ----

        /// <summary>Bytes needed to transcode a UTF-16 string to UTF-8, with U+FFFD for bad surrogates.</summary>
        public static int GetUtf8ByteCount(string value)
        {
            if (value == null)
                return 0;

            int total = 0;
            int index = 0;
            int length = value.Length;

            while (index < length)
            {
                uint scalar = NextScalarFromUtf16(value, length, ref index);
                total += GetScalarByteCount(scalar);
            }

            return total;
        }

        /// <summary>Transcode UTF-16 to UTF-8. Returns bytes written, or -1 if the destination is too small.</summary>
        public static int FromUtf16(string value, byte* destination, int capacity)
        {
            if (value == null)
                return 0;

            int written = 0;
            int index = 0;
            int length = value.Length;

            while (index < length)
            {
                uint scalar = NextScalarFromUtf16(value, length, ref index);
                int encoded = EncodeScalar(scalar, destination + written, capacity - written);
                if (encoded == 0)
                    return -1;

                written += encoded;
            }

            return written;
        }

        private static uint NextScalarFromUtf16(string value, int length, ref int index)
        {
            char current = value[index++];

            // High surrogate followed by a low surrogate forms one scalar.
            if (current >= 0xD800 && current <= 0xDBFF && index < length)
            {
                char low = value[index];
                if (low >= 0xDC00 && low <= 0xDFFF)
                {
                    index++;
                    return 0x10000u + (((uint)current - 0xD800u) << 10) + ((uint)low - 0xDC00u);
                }
            }

            // Unpaired surrogates are not encodable; substitute.
            if (current >= 0xD800 && current <= 0xDFFF)
                return ReplacementChar;

            return current;
        }

        // ---- ASCII helpers (zerolib has no char.IsX) ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiWhiteSpace(byte value)
            => value == (byte)' ' || value == (byte)'\t' || value == (byte)'\n'
            || value == (byte)'\r' || value == (byte)'\f' || value == (byte)'\v';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiDigit(byte value)
            => value >= (byte)'0' && value <= (byte)'9';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToAsciiLower(byte value)
            => value >= (byte)'A' && value <= (byte)'Z' ? (byte)(value + 32) : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToAsciiUpper(byte value)
            => value >= (byte)'a' && value <= (byte)'z' ? (byte)(value - 32) : value;
    }
}
