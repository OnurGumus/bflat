// Acquire reads and release writes. Implemented through volatile fields, which
// the JIT compiles with the required ordering (ldar/stlr on arm64; plain moves
// on x64, whose memory model already provides it).

using System.Runtime.CompilerServices;

namespace System.Threading
{
    public static class Volatile
    {
        private struct VolatileBoolean { public volatile bool Value; }
        private struct VolatileInt32 { public volatile int Value; }
        private struct VolatileUInt32 { public volatile uint Value; }
        private struct VolatileIntPtr { public volatile IntPtr Value; }

        // 64-bit values: volatile long isn't allowed in C#, so read/write through
        // nint on 64-bit targets (zero mode targets are all 64-bit except win-x86).

        public static bool Read(ref readonly bool location)
            => Unsafe.As<bool, VolatileBoolean>(ref Unsafe.AsRef(in location)).Value;

        public static void Write(ref bool location, bool value)
            => Unsafe.As<bool, VolatileBoolean>(ref location).Value = value;

        public static int Read(ref readonly int location)
            => Unsafe.As<int, VolatileInt32>(ref Unsafe.AsRef(in location)).Value;

        public static void Write(ref int location, int value)
            => Unsafe.As<int, VolatileInt32>(ref location).Value = value;

        public static uint Read(ref readonly uint location)
            => Unsafe.As<uint, VolatileUInt32>(ref Unsafe.AsRef(in location)).Value;

        public static void Write(ref uint location, uint value)
            => Unsafe.As<uint, VolatileUInt32>(ref location).Value = value;

        public static nint Read(ref readonly nint location)
            => Unsafe.As<nint, VolatileIntPtr>(ref Unsafe.AsRef(in location)).Value;

        public static void Write(ref nint location, nint value)
            => Unsafe.As<nint, VolatileIntPtr>(ref location).Value = value;

#if !X86
        public static long Read(ref readonly long location)
            => (long)Unsafe.As<long, VolatileIntPtr>(ref Unsafe.AsRef(in location)).Value;

        public static void Write(ref long location, long value)
            => Unsafe.As<long, VolatileIntPtr>(ref location).Value = (nint)value;
#endif
    }
}
