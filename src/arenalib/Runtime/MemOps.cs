using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// Raw memory primitives. zerolib's SpanHelpers is internal, so arenalib needs its own.
    ///
    /// The per-OS split lives inside method bodies rather than in separate .Windows/.Unix/.Efi
    /// files on purpose: the public signatures are then identical across all seven target
    /// builds *by construction*, which is what keeps the single ref/arenalib.dll honest.
    /// On Linux we call straight into libc; elsewhere we use portable word-at-a-time loops
    /// (kernel32 exports RtlMoveMemory but not reliably on every arch, and UEFI has no libc).
    /// </summary>
    public static unsafe class MemOps
    {
#if LINUX
        [DllImport("*"), SuppressGCTransition]
        private static extern void* memcpy(void* dest, void* src, nuint n);

        [DllImport("*"), SuppressGCTransition]
        private static extern void* memmove(void* dest, void* src, nuint n);

        [DllImport("*"), SuppressGCTransition]
        private static extern void* memset(void* dest, int c, nuint n);

        [DllImport("*"), SuppressGCTransition]
        private static extern int memcmp(void* a, void* b, nuint n);
#endif

        /// <summary>Copy <paramref name="bytes"/> bytes. The regions must not overlap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy(void* destination, void* source, nuint bytes)
        {
#if LINUX
            memcpy(destination, source, bytes);
#else
            CopyPortable((byte*)destination, (byte*)source, bytes);
#endif
        }

        /// <summary>Copy <paramref name="bytes"/> bytes. The regions may overlap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Move(void* destination, void* source, nuint bytes)
        {
#if LINUX
            memmove(destination, source, bytes);
#else
            MovePortable((byte*)destination, (byte*)source, bytes);
#endif
        }

        /// <summary>Fill <paramref name="bytes"/> bytes with zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Zero(void* destination, nuint bytes)
        {
#if LINUX
            memset(destination, 0, bytes);
#else
            SetPortable((byte*)destination, 0, bytes);
#endif
        }

        /// <summary>Fill <paramref name="bytes"/> bytes with <paramref name="value"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set(void* destination, byte value, nuint bytes)
        {
#if LINUX
            memset(destination, value, bytes);
#else
            SetPortable((byte*)destination, value, bytes);
#endif
        }

        /// <summary>Ordinal comparison. Negative / zero / positive, like memcmp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Compare(void* a, void* b, nuint bytes)
        {
#if LINUX
            return memcmp(a, b, bytes);
#else
            return ComparePortable((byte*)a, (byte*)b, bytes);
#endif
        }

        /// <summary>True if the two regions hold identical bytes.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Equal(void* a, void* b, nuint bytes)
            => Compare(a, b, bytes) == 0;

        // ---- portable implementations (also used to keep Windows/UEFI libc-free) ----

        private static void CopyPortable(byte* d, byte* s, nuint n)
        {
            while (n >= 8)
            {
                *(ulong*)d = *(ulong*)s;
                d += 8;
                s += 8;
                n -= 8;
            }

            while (n != 0)
            {
                *d = *s;
                d++;
                s++;
                n--;
            }
        }

        private static void MovePortable(byte* d, byte* s, nuint n)
        {
            if (n == 0 || d == s)
                return;

            // Forward is safe when the destination starts below the source, or when the
            // regions are disjoint entirely.
            if ((nuint)d < (nuint)s || (nuint)d >= (nuint)s + n)
            {
                CopyPortable(d, s, n);
                return;
            }

            // Overlapping with destination above source: copy backwards.
            byte* dEnd = (byte*)((nuint)d + n);
            byte* sEnd = (byte*)((nuint)s + n);

            while (n >= 8)
            {
                dEnd -= 8;
                sEnd -= 8;
                *(ulong*)dEnd = *(ulong*)sEnd;
                n -= 8;
            }

            while (n != 0)
            {
                dEnd--;
                sEnd--;
                *dEnd = *sEnd;
                n--;
            }
        }

        private static void SetPortable(byte* d, byte value, nuint n)
        {
            ulong word = value;
            word |= word << 8;
            word |= word << 16;
            word |= word << 32;

            while (n >= 8)
            {
                *(ulong*)d = word;
                d += 8;
                n -= 8;
            }

            while (n != 0)
            {
                *d = value;
                d++;
                n--;
            }
        }

        private static int ComparePortable(byte* a, byte* b, nuint n)
        {
            while (n != 0)
            {
                int diff = *a - *b;
                if (diff != 0)
                    return diff;
                a++;
                b++;
                n--;
            }

            return 0;
        }
    }
}
