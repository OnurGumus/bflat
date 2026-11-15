// Bit-level conversions between floating-point values and their raw bits.
// Subset of System.BitConverter.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace System
{
    public static class BitConverter
    {
        public static int SingleToInt32Bits(float value) => Unsafe.As<float, int>(ref value);
        public static float Int32BitsToSingle(int value) => Unsafe.As<int, float>(ref value);
        public static uint SingleToUInt32Bits(float value) => Unsafe.As<float, uint>(ref value);
        public static float UInt32BitsToSingle(uint value) => Unsafe.As<uint, float>(ref value);

        public static ushort HalfToUInt16Bits(Half value) => value._value;
        public static Half UInt16BitsToHalf(ushort value) => new Half(value);
        public static short HalfToInt16Bits(Half value) => (short)value._value;
        public static Half Int16BitsToHalf(short value) => new Half((ushort)value);

        public static ushort BFloat16ToUInt16Bits(BFloat16 value) => value._value;
        public static BFloat16 UInt16BitsToBFloat16(ushort value) => new BFloat16(value);
        public static short BFloat16ToInt16Bits(BFloat16 value) => (short)value._value;
        public static BFloat16 Int16BitsToBFloat16(short value) => new BFloat16((ushort)value);
    }
}
