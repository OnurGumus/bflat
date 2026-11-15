using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// Number to UTF-8 formatting. zerolib's primitives have no ToString at all, so this is
    /// the only way to render a number.
    ///
    /// Every method returns the number of bytes written, or -1 if the destination is too
    /// small. Nothing is written on failure.
    ///
    /// Floating point is deliberately absent: correct double formatting is Ryu-class work,
    /// and zerolib's checked float-to-int helpers FailFast. Use <see cref="FormatFixed"/>.
    /// </summary>
    public static unsafe class Utf8Format
    {
        /// <summary>Longest possible output of the integer formatters ("-9223372036854775808").</summary>
        public const int MaxIntegerLength = 20;

        /// <summary>Longest possible output of <see cref="FormatUInt64"/> in binary.</summary>
        public const int MaxRadix2Length = 64;

        private const string LowerDigits = "0123456789abcdefghijklmnopqrstuvwxyz";
        private const string UpperDigits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        // ---- pointer-based core ----

        public static int FormatUInt64(ulong value, byte* destination, int capacity, int radix, int minDigits, bool upper)
        {
            if (radix < 2 || radix > 36)
                Fail.Msg("Utf8Format: radix must be in [2, 36]");

            byte* scratch = stackalloc byte[MaxRadix2Length];
            int count = 0;

            do
            {
                uint digit = (uint)(value % (ulong)radix);
                value /= (ulong)radix;
                scratch[count++] = (byte)(upper ? UpperDigits[(int)digit] : LowerDigits[(int)digit]);
            }
            while (value != 0);

            int padding = minDigits > count ? minDigits - count : 0;
            int total = count + padding;
            if (total > capacity)
                return -1;

            int written = 0;
            while (padding-- > 0)
                destination[written++] = (byte)'0';

            while (count-- > 0)
                destination[written++] = scratch[count];

            return written;
        }

        public static int FormatInt64(long value, byte* destination, int capacity, int radix, int minDigits, bool upper)
        {
            if (value >= 0)
                return FormatUInt64((ulong)value, destination, capacity, radix, minDigits, upper);

            if (capacity < 1)
                return -1;

            // Negate through ulong so long.MinValue works.
            ulong magnitude = 0UL - (ulong)value;

            destination[0] = (byte)'-';
            int rest = FormatUInt64(magnitude, destination + 1, capacity - 1, radix, minDigits, upper);
            return rest < 0 ? -1 : rest + 1;
        }

        // ---- Span-based convenience ----

        public static int FormatInt32(int value, Span<byte> destination)
            => FormatInt64(value, destination, 10, 0, false);

        public static int FormatUInt32(uint value, Span<byte> destination)
            => FormatUInt64(value, destination, 10, 0, false);

        public static int FormatInt64(long value, Span<byte> destination)
            => FormatInt64(value, destination, 10, 0, false);

        public static int FormatUInt64(ulong value, Span<byte> destination)
            => FormatUInt64(value, destination, 10, 0, false);

        public static int FormatInt64(long value, Span<byte> destination, int radix, int minDigits, bool upper)
        {
            if (destination.Length == 0)
                return -1;

            return FormatInt64(value, Pointer(destination), destination.Length, radix, minDigits, upper);
        }

        public static int FormatUInt64(ulong value, Span<byte> destination, int radix, int minDigits, bool upper)
        {
            if (destination.Length == 0)
                return -1;

            return FormatUInt64(value, Pointer(destination), destination.Length, radix, minDigits, upper);
        }

        public static int FormatHex64(ulong value, Span<byte> destination, int digits, bool upper)
            => FormatUInt64(value, destination, 16, digits, upper);

        public static int FormatBool(bool value, Span<byte> destination)
        {
            ReadOnlySpan<byte> text = value ? "true"u8 : "false"u8;
            if (destination.Length < text.Length)
                return -1;

            text.CopyTo(destination);
            return text.Length;
        }

        /// <summary>
        /// Render a scaled integer as a fixed-point decimal: (12345, 2) becomes "123.45".
        /// This is the M1 stand-in for floating point.
        /// </summary>
        public static int FormatFixed(long scaledValue, int decimals, Span<byte> destination)
        {
            if (decimals < 0 || decimals > 18)
                Fail.Msg("Utf8Format: decimals must be in [0, 18]");

            if (decimals == 0)
                return FormatInt64(scaledValue, destination);

            bool negative = scaledValue < 0;
            ulong magnitude = negative ? 0UL - (ulong)scaledValue : (ulong)scaledValue;

            ulong divisor = 1;
            for (int i = 0; i < decimals; i++)
                divisor *= 10;

            ulong whole = magnitude / divisor;
            ulong fraction = magnitude % divisor;

            int written = 0;
            if (negative)
            {
                if (destination.Length < 1)
                    return -1;
                destination[0] = (byte)'-';
                written = 1;
            }

            int wholeLength = FormatUInt64(whole, destination.Slice(written), 10, 0, false);
            if (wholeLength < 0)
                return -1;
            written += wholeLength;

            if (written >= destination.Length)
                return -1;
            destination[written++] = (byte)'.';

            int fractionLength = FormatUInt64(fraction, destination.Slice(written), 10, decimals, false);
            if (fractionLength < 0)
                return -1;

            return written + fractionLength;
        }

        /// <summary>Render a byte count as "1.5 MiB".</summary>
        public static int FormatByteSize(ulong bytes, Span<byte> destination)
        {
            ReadOnlySpan<byte> suffix;
            ulong unit;

            if (bytes >= 1UL << 30) { suffix = " GiB"u8; unit = 1UL << 30; }
            else if (bytes >= 1UL << 20) { suffix = " MiB"u8; unit = 1UL << 20; }
            else if (bytes >= 1UL << 10) { suffix = " KiB"u8; unit = 1UL << 10; }
            else { suffix = " B"u8; unit = 1; }

            // One decimal place, computed without floating point.
            ulong scaled = unit == 1 ? bytes * 10 : (bytes * 10) / unit;

            int written = FormatFixed((long)scaled, 1, destination);
            if (written < 0)
                return -1;

            if (destination.Length - written < suffix.Length)
                return -1;

            suffix.CopyTo(destination.Slice(written));
            return written + suffix.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* Pointer(Span<byte> span)
            => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));
    }
}
