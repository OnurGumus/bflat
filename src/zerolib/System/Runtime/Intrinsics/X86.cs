// x86/x64 hardware intrinsics (subset). Each body calls itself: the JIT replaces
// the call with the instruction, so the body only runs if the JIT didn't.
// The class chain mirrors .NET's (X86Base -> Sse -> ... -> Avx -> Avx2, Fma), and
// signatures match .NET's exactly; the JIT only recognizes exact matches.
//
// Zero mode has no run-time CPU detection: Avx/Avx2/Fma are only supported when
// they're part of the baseline, e.g. with -m x86-64-v3.

using System.Runtime.CompilerServices;

namespace System.Runtime.Intrinsics.X86
{
    [Intrinsic]
    public abstract class X86Base
    {
        internal X86Base() { }
        public static bool IsSupported { [Intrinsic] get => IsSupported; }
    }

    [Intrinsic]
    public abstract unsafe class Sse : X86Base
    {
        internal Sse() { }
        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        [Intrinsic] public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) => Add(left, right);
        [Intrinsic] public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) => Multiply(left, right);
        [Intrinsic] public static Vector128<float> LoadVector128(float* address) => LoadVector128(address);
        [Intrinsic] public static void Store(float* address, Vector128<float> source) => Store(address, source);
    }

    [Intrinsic] public abstract class Sse2 : Sse { internal Sse2() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Sse3 : Sse2 { internal Sse3() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Ssse3 : Sse3 { internal Ssse3() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Sse41 : Ssse3 { internal Sse41() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Sse42 : Sse41 { internal Sse42() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }

    [Intrinsic]
    public abstract unsafe class Avx : Sse42
    {
        internal Avx() { }
        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        // ---- float arithmetic ----
        [Intrinsic] public static Vector256<float> Add(Vector256<float> left, Vector256<float> right) => Add(left, right);
        [Intrinsic] public static Vector256<float> Subtract(Vector256<float> left, Vector256<float> right) => Subtract(left, right);
        [Intrinsic] public static Vector256<float> Multiply(Vector256<float> left, Vector256<float> right) => Multiply(left, right);
        [Intrinsic] public static Vector256<float> Divide(Vector256<float> left, Vector256<float> right) => Divide(left, right);
        [Intrinsic] public static Vector256<float> Max(Vector256<float> left, Vector256<float> right) => Max(left, right);
        [Intrinsic] public static Vector256<float> Min(Vector256<float> left, Vector256<float> right) => Min(left, right);
        [Intrinsic] public static Vector256<float> RoundToNearestInteger(Vector256<float> value) => RoundToNearestInteger(value);

        // ---- conversions ----
        /// <summary>float to int32 with the current rounding mode (nearest even by default).</summary>
        [Intrinsic] public static Vector256<int> ConvertToVector256Int32(Vector256<float> value) => ConvertToVector256Int32(value);
        [Intrinsic] public static Vector256<float> ConvertToVector256Single(Vector256<int> value) => ConvertToVector256Single(value);

        // ---- broadcast, load, store ----
        [Intrinsic] public static Vector256<float> BroadcastScalarToVector256(float* source) => BroadcastScalarToVector256(source);
        [Intrinsic] public static Vector256<float> LoadVector256(float* address) => LoadVector256(address);
        [Intrinsic] public static Vector256<int> LoadVector256(int* address) => LoadVector256(address);
        [Intrinsic] public static Vector256<sbyte> LoadVector256(sbyte* address) => LoadVector256(address);
        [Intrinsic] public static void Store(float* address, Vector256<float> source) => Store(address, source);
        [Intrinsic] public static void Store(int* address, Vector256<int> source) => Store(address, source);
    }

    [Intrinsic]
    public abstract class Avx2 : Avx
    {
        internal Avx2() { }
        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        [Intrinsic] public static Vector256<int> Add(Vector256<int> left, Vector256<int> right) => Add(left, right);
        [Intrinsic] public static Vector256<int> ShiftLeftLogical(Vector256<int> value, byte count) => ShiftLeftLogical(value, count);

        // ---- int8 dot-product building blocks ----
        /// <summary>|value| as unsigned bytes (VPABSB).</summary>
        [Intrinsic] public static Vector256<byte> Abs(Vector256<sbyte> value) => Abs(value);

        /// <summary>left, negated where right is negative and zeroed where right is zero (VPSIGNB).</summary>
        [Intrinsic] public static Vector256<sbyte> Sign(Vector256<sbyte> left, Vector256<sbyte> right) => Sign(left, right);

        /// <summary>Unsigned x signed bytes, adjacent pairs summed into saturated int16 (VPMADDUBSW).</summary>
        [Intrinsic] public static Vector256<short> MultiplyAddAdjacent(Vector256<byte> left, Vector256<sbyte> right) => MultiplyAddAdjacent(left, right);

        /// <summary>int16 x int16, adjacent pairs summed into int32 (VPMADDWD).</summary>
        [Intrinsic] public static Vector256<int> MultiplyAddAdjacent(Vector256<short> left, Vector256<short> right) => MultiplyAddAdjacent(left, right);
    }

    [Intrinsic]
    public abstract class Fma : Avx
    {
        internal Fma() { }
        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        /// <summary>(a * b) + c, fused (VFMADD).</summary>
        [Intrinsic]
        public static Vector256<float> MultiplyAdd(Vector256<float> a, Vector256<float> b, Vector256<float> c) => MultiplyAdd(a, b, c);
    }
}
