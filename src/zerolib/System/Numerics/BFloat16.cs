// bfloat16: the top 16 bits of a float. Subset of System.Numerics.BFloat16:
// storage plus conversions to and from float (round to nearest, ties to even).

namespace System.Numerics
{
    public readonly struct BFloat16
    {
        internal readonly ushort _value;

        internal BFloat16(ushort value) => _value = value;

        public static explicit operator float(BFloat16 value)
            => BitConverter.UInt32BitsToSingle((uint)value._value << 16);

        public static explicit operator BFloat16(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            if ((bits & 0x7F800000u) == 0x7F800000u && (bits & 0x7FFFFFu) != 0)
                return new BFloat16((ushort)((bits >> 16) | 0x40u)); // NaN: keep it a (quiet) NaN

            uint lsb = (bits >> 16) & 1;
            bits += 0x7FFFu + lsb; // round to nearest even; may carry up to infinity
            return new BFloat16((ushort)(bits >> 16));
        }
    }
}
