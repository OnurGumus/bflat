// SIMD vector types. The compiler recognizes these by namespace and name in the
// system module (zerolib in zero mode) and gives them SIMD layout and registers.
// Field layout mirrors .NET's CoreLib.

using System.Runtime.CompilerServices;

namespace System.Runtime.Intrinsics
{
    [Intrinsic]
    public readonly struct Vector64<T>
    {
        private readonly ulong _00;
    }

    [Intrinsic]
    public readonly struct Vector128<T>
    {
        internal readonly Vector64<T> _lower;
        internal readonly Vector64<T> _upper;
    }

    [Intrinsic]
    public readonly struct Vector256<T>
    {
        internal readonly Vector128<T> _lower;
        internal readonly Vector128<T> _upper;
    }

    // Cross-platform helpers (subset). Unlike the ISA classes, these have real
    // software bodies, so they're correct even where the JIT doesn't expand them.

    public static unsafe class Vector128
    {
        [Intrinsic]
        public static Vector128<TTo> As<TFrom, TTo>(this Vector128<TFrom> vector)
            => Unsafe.As<Vector128<TFrom>, Vector128<TTo>>(ref vector);

        [Intrinsic]
        public static Vector128<float> AsSingle<T>(this Vector128<T> vector) => vector.As<T, float>();

        [Intrinsic]
        public static Vector128<int> AsInt32<T>(this Vector128<T> vector) => vector.As<T, int>();

        [Intrinsic]
        public static Vector128<uint> AsUInt32<T>(this Vector128<T> vector) => vector.As<T, uint>();

        [Intrinsic]
        public static Vector128<sbyte> AsSByte<T>(this Vector128<T> vector) => vector.As<T, sbyte>();

        [Intrinsic]
        public static Vector128<float> Create(float value)
        {
            Vector128<float> result;
            float* lanes = (float*)&result;
            for (int i = 0; i < 4; i++)
                lanes[i] = value;
            return result;
        }
    }

    public static unsafe class Vector256
    {
        [Intrinsic]
        public static Vector256<TTo> As<TFrom, TTo>(this Vector256<TFrom> vector)
            => Unsafe.As<Vector256<TFrom>, Vector256<TTo>>(ref vector);

        [Intrinsic]
        public static Vector256<float> AsSingle<T>(this Vector256<T> vector) => vector.As<T, float>();

        [Intrinsic]
        public static Vector256<int> AsInt32<T>(this Vector256<T> vector) => vector.As<T, int>();

        [Intrinsic]
        public static Vector256<float> Create(float value)
        {
            Vector256<float> result;
            float* lanes = (float*)&result;
            for (int i = 0; i < 8; i++)
                lanes[i] = value;
            return result;
        }
    }
}
