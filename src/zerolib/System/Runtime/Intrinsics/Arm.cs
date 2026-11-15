// ARM64 hardware intrinsics (subset). Each body calls itself: the JIT replaces
// the call with the instruction, so the body only runs if the JIT didn't.
// Signatures match .NET's System.Runtime.Intrinsics.Arm exactly; the JIT only
// recognizes exact matches.

using System.Runtime.CompilerServices;

namespace System.Runtime.Intrinsics.Arm
{
    [Intrinsic]
    public abstract class ArmBase
    {
        internal ArmBase() { }

        public static bool IsSupported { [Intrinsic] get => IsSupported; }

        [Intrinsic]
        public abstract class Arm64
        {
            internal Arm64() { }

            public static bool IsSupported { [Intrinsic] get => IsSupported; }
        }
    }

    [Intrinsic]
    public abstract unsafe class AdvSimd : ArmBase
    {
        internal AdvSimd() { }

        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        // ---- float arithmetic ----
        [Intrinsic] public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) => Add(left, right);
        [Intrinsic] public static Vector128<float> Subtract(Vector128<float> left, Vector128<float> right) => Subtract(left, right);
        [Intrinsic] public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) => Multiply(left, right);
        [Intrinsic] public static Vector128<float> Max(Vector128<float> left, Vector128<float> right) => Max(left, right);
        [Intrinsic] public static Vector128<float> Min(Vector128<float> left, Vector128<float> right) => Min(left, right);

        /// <summary>addend + (left * right), fused (FMLA).</summary>
        [Intrinsic]
        public static Vector128<float> FusedMultiplyAdd(Vector128<float> addend, Vector128<float> left, Vector128<float> right)
            => FusedMultiplyAdd(addend, left, right);

        /// <summary>Rounds to the nearest integer, ties to even (FRINTN).</summary>
        [Intrinsic] public static Vector128<float> RoundToNearest(Vector128<float> value) => RoundToNearest(value);

        // ---- int arithmetic ----
        [Intrinsic] public static Vector128<int> Add(Vector128<int> left, Vector128<int> right) => Add(left, right);
        [Intrinsic] public static Vector128<int> Subtract(Vector128<int> left, Vector128<int> right) => Subtract(left, right);
        [Intrinsic] public static Vector128<uint> ShiftLeftLogical(Vector128<uint> value, byte count) => ShiftLeftLogical(value, count);

        // ---- conversions ----
        [Intrinsic] public static Vector128<int> ConvertToInt32RoundToEven(Vector128<float> value) => ConvertToInt32RoundToEven(value);
        [Intrinsic] public static Vector128<float> ConvertToSingle(Vector128<int> value) => ConvertToSingle(value);

        // ---- broadcast, load, store ----
        [Intrinsic] public static Vector128<float> DuplicateToVector128(float value) => DuplicateToVector128(value);
        [Intrinsic] public static Vector128<int> DuplicateToVector128(int value) => DuplicateToVector128(value);
        [Intrinsic] public static Vector128<float> LoadVector128(float* address) => LoadVector128(address);
        [Intrinsic] public static Vector128<int> LoadVector128(int* address) => LoadVector128(address);
        [Intrinsic] public static Vector128<sbyte> LoadVector128(sbyte* address) => LoadVector128(address);
        [Intrinsic] public static void Store(float* address, Vector128<float> source) => Store(address, source);
        [Intrinsic] public static void Store(int* address, Vector128<int> source) => Store(address, source);

        [Intrinsic]
        public new abstract class Arm64 : ArmBase.Arm64
        {
            internal Arm64() { }

            public static new bool IsSupported { [Intrinsic] get => IsSupported; }

            /// <summary>Pairwise add (FADDP); applied twice it sums all four lanes.</summary>
            [Intrinsic]
            public static Vector128<float> AddPairwise(Vector128<float> left, Vector128<float> right) => AddPairwise(left, right);

            [Intrinsic] public static Vector128<float> Divide(Vector128<float> left, Vector128<float> right) => Divide(left, right);

            /// <summary>Largest lane, in lane 0 of the result (FMAXV).</summary>
            [Intrinsic] public static Vector64<float> MaxAcross(Vector128<float> value) => MaxAcross(value);

            /// <summary>Sum of all lanes, in lane 0 of the result (ADDV).</summary>
            [Intrinsic] public static Vector64<int> AddAcross(Vector128<int> value) => AddAcross(value);
        }
    }

    /// <summary>ARMv8.2 dot product (SDOT/UDOT). Not baseline: build with -m dotprod.</summary>
    [Intrinsic]
    public abstract class Dp : AdvSimd
    {
        internal Dp() { }

        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        /// <summary>
        /// For each 32-bit lane i: addend[i] + sum of left[4i..4i+3] * right[4i..4i+3] (SDOT).
        /// </summary>
        [Intrinsic]
        public static Vector128<int> DotProduct(Vector128<int> addend, Vector128<sbyte> left, Vector128<sbyte> right)
            => DotProduct(addend, left, right);

        [Intrinsic]
        public new abstract class Arm64 : AdvSimd.Arm64
        {
            internal Arm64() { }

            public static new bool IsSupported { [Intrinsic] get => IsSupported; }
        }
    }
}
