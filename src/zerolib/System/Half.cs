// IEEE 754 binary16. Subset of System.Half: storage plus exact conversions to and
// from float (round to nearest, ties to even; subnormals, infinities and NaN kept).
// NaN payloads are preserved where they fit, with the quiet bit set.

namespace System
{
    public readonly struct Half
    {
        internal readonly ushort _value;

        internal Half(ushort value) => _value = value;

        public static explicit operator float(Half value)
        {
            uint h = value._value;
            uint sign = (h & 0x8000u) << 16;
            uint exponent = (h >> 10) & 0x1Fu;
            uint mantissa = h & 0x3FFu;

            uint bits;
            if (exponent == 0x1F)
            {
                // Infinity, or NaN (quiet bit set, payload kept).
                bits = sign | 0x7F800000u | (mantissa != 0 ? 0x00400000u | (mantissa << 13) : 0u);
            }
            else if (exponent != 0)
            {
                bits = sign | ((exponent + (127 - 15)) << 23) | (mantissa << 13);
            }
            else if (mantissa == 0)
            {
                bits = sign;
            }
            else
            {
                // Subnormal: shift until the implicit bit appears, adjusting the exponent.
                uint shifts = 0;
                do
                {
                    mantissa <<= 1;
                    shifts++;
                } while ((mantissa & 0x400u) == 0);
                bits = sign | ((127 - 14 - shifts) << 23) | ((mantissa & 0x3FFu) << 13);
            }

            return BitConverter.UInt32BitsToSingle(bits);
        }

        public static explicit operator Half(float value)
        {
            uint f = BitConverter.SingleToUInt32Bits(value);
            uint sign = (f >> 16) & 0x8000u;
            uint exponent = (f >> 23) & 0xFFu;
            uint mantissa = f & 0x7FFFFFu;

            if (exponent == 0xFF)
            {
                // Infinity, or NaN (quiet bit set so a payload can't turn it into infinity).
                return new Half((ushort)(sign | 0x7C00u | (mantissa != 0 ? 0x200u | (mantissa >> 13) : 0u)));
            }

            int e = (int)exponent - 127 + 15;
            if (e >= 0x1F)
                return new Half((ushort)(sign | 0x7C00u)); // overflow to infinity

            if (e <= 0)
            {
                if (e < -10)
                    return new Half((ushort)sign); // below half the smallest subnormal: zero

                // Subnormal: value / 2^-24, rounded to nearest even.
                mantissa |= 0x800000u;
                int shift = 14 - e;
                uint result = mantissa >> shift;
                uint remainder = mantissa & ((1u << shift) - 1);
                uint halfway = 1u << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (result & 1) != 0))
                    result++;
                return new Half((ushort)(sign | result));
            }

            uint bits = sign | ((uint)e << 10) | (mantissa >> 13);
            uint rest = mantissa & 0x1FFFu;
            // Rounding up can carry into the exponent, which is exactly right (up to infinity).
            if (rest > 0x1000u || (rest == 0x1000u && (bits & 1) != 0))
                bits++;
            return new Half((ushort)bits);
        }
    }
}
