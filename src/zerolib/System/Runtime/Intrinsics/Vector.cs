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
}
