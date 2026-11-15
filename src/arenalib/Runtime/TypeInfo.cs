using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    // Two fields of the same type laid out sequentially: the offset of the second one
    // is exactly sizeof(T), including any trailing padding.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SizePair<T>
    {
        public T A;
        public T B;
    }

    // A byte followed by a T: the offset of the T is the alignment the runtime chose.
    [StructLayout(LayoutKind.Sequential)]
    internal struct AlignPair<T>
    {
        public byte Pad;
        public T Value;
    }

    /// <summary>
    /// sizeof/alignof for arbitrary T, including managed T.
    ///
    /// arenalib cannot declare its own [Intrinsic] members (zerolib's IntrinsicAttribute
    /// is internal), and Unsafe.SizeOf&lt;T&gt;() is not expanded by ILC in zerolib mode.
    /// These struct-offset tricks work instead, and RyuJIT constant-folds them because it
    /// knows the field offsets statically.
    /// </summary>
    public static unsafe class TypeInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf<T>()
        {
            SizePair<T> pair = default;
            return (int)Unsafe.ByteOffset(ref pair.A, ref pair.B);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AlignOf<T>()
        {
            AlignPair<T> pair = default;
            int align = (int)Unsafe.ByteOffset(ref pair.Pad, ref Unsafe.As<T, byte>(ref pair.Value));

            // The runtime is free to reorder auto-layout structs that contain references,
            // which puts the reference field *before* the padding byte and yields a
            // negative or nonsensical offset. Verified: AlignOf<string>() does exactly
            // this. Fall back to pointer alignment, which is always safe.
            if (align <= 0 || align > 16 || (align & (align - 1)) != 0)
                align = sizeof(void*);

            return align;
        }

        /// <summary>Round <paramref name="value"/> up to a multiple of a power-of-two alignment.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint AlignUp(nuint value, nuint align)
            => (value + (align - 1)) & ~(align - 1);

        /// <summary>Round a requested alignment up to a usable power of two in [1, 4096].</summary>
        public static nuint NormalizeAlign(nuint align)
        {
            if (align == 0)
                return 1;
            if (align > 4096)
                align = 4096;

            // Round up to the next power of two.
            nuint result = 1;
            while (result < align)
                result <<= 1;
            return result;
        }
    }
}
