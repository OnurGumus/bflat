// ARM64 hardware intrinsics (subset). Each body calls itself: the JIT replaces
// the call with the instruction, so the body only runs if the JIT didn't.

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
    public abstract class AdvSimd : ArmBase
    {
        internal AdvSimd() { }

        public static new bool IsSupported { [Intrinsic] get => IsSupported; }

        [Intrinsic]
        public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) => Add(left, right);

        [Intrinsic]
        public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) => Multiply(left, right);

        /// <summary>addend + (left * right), fused (FMLA).</summary>
        [Intrinsic]
        public static Vector128<float> FusedMultiplyAdd(Vector128<float> addend, Vector128<float> left, Vector128<float> right)
            => FusedMultiplyAdd(addend, left, right);

        [Intrinsic]
        public static Vector128<float> DuplicateToVector128(float value) => DuplicateToVector128(value);

        [Intrinsic]
        public static unsafe Vector128<float> LoadVector128(float* address) => LoadVector128(address);

        [Intrinsic]
        public static unsafe void Store(float* address, Vector128<float> source) => Store(address, source);

        [Intrinsic]
        public new abstract class Arm64 : ArmBase.Arm64
        {
            internal Arm64() { }

            public static new bool IsSupported { [Intrinsic] get => IsSupported; }

            /// <summary>Pairwise add (FADDP); applied twice it sums all four lanes.</summary>
            [Intrinsic]
            public static Vector128<float> AddPairwise(Vector128<float> left, Vector128<float> right) => AddPairwise(left, right);
        }
    }
}
