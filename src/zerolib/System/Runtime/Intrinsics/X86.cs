// x86/x64 hardware intrinsics (subset). Each body calls itself: the JIT replaces
// the call with the instruction, so the body only runs if the JIT didn't.
// The class chain mirrors .NET's (X86Base -> Sse -> ... -> Avx -> Avx2, Fma).

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
    public abstract class Sse : X86Base
    {
        internal Sse() { }
        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        [Intrinsic]
        public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) => Add(left, right);

        [Intrinsic]
        public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) => Multiply(left, right);

        [Intrinsic]
        public static unsafe Vector128<float> LoadVector128(float* address) => LoadVector128(address);

        [Intrinsic]
        public static unsafe void Store(float* address, Vector128<float> source) => Store(address, source);
    }

    [Intrinsic] public abstract class Sse2 : Sse { internal Sse2() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Sse3 : Sse2 { internal Sse3() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Ssse3 : Sse3 { internal Ssse3() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Sse41 : Ssse3 { internal Sse41() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }
    [Intrinsic] public abstract class Sse42 : Sse41 { internal Sse42() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }

    [Intrinsic]
    public abstract class Avx : Sse42
    {
        internal Avx() { }
        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        [Intrinsic]
        public static Vector256<float> Add(Vector256<float> left, Vector256<float> right) => Add(left, right);

        [Intrinsic]
        public static Vector256<float> Multiply(Vector256<float> left, Vector256<float> right) => Multiply(left, right);

        [Intrinsic]
        public static unsafe Vector256<float> LoadVector256(float* address) => LoadVector256(address);

        [Intrinsic]
        public static unsafe void Store(float* address, Vector256<float> source) => Store(address, source);
    }

    [Intrinsic] public abstract class Avx2 : Avx { internal Avx2() { } public static new bool IsSupported { [Intrinsic] get => IsSupported; } }

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
