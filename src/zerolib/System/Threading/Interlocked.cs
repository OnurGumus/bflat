// Atomic operations. The JIT expands these intrinsics inline (lock xadd/cmpxchg on
// x64; ldaxr/stlxr loops, or LSE atomics with -m lse, on arm64). The bodies call
// themselves, as in CoreLib: they only run if the JIT didn't expand the call.

using System.Runtime.CompilerServices;

namespace System.Threading
{
    public static class Interlocked
    {
        [Intrinsic]
        public static int CompareExchange(ref int location1, int value, int comparand)
            => CompareExchange(ref location1, value, comparand);

        [Intrinsic]
        public static long CompareExchange(ref long location1, long value, long comparand)
            => CompareExchange(ref location1, value, comparand);

        [Intrinsic]
        public static int Exchange(ref int location1, int value) => Exchange(ref location1, value);

        [Intrinsic]
        public static long Exchange(ref long location1, long value) => Exchange(ref location1, value);

        [Intrinsic]
        internal static int ExchangeAdd(ref int location1, int value) => ExchangeAdd(ref location1, value);

        [Intrinsic]
        internal static long ExchangeAdd(ref long location1, long value) => ExchangeAdd(ref location1, value);

        /// <summary>Atomically adds and returns the new value.</summary>
        public static int Add(ref int location1, int value) => ExchangeAdd(ref location1, value) + value;
        public static long Add(ref long location1, long value) => ExchangeAdd(ref location1, value) + value;

        public static int Increment(ref int location) => Add(ref location, 1);
        public static long Increment(ref long location) => Add(ref location, 1);
        public static int Decrement(ref int location) => Add(ref location, -1);
        public static long Decrement(ref long location) => Add(ref location, -1);

        [Intrinsic]
        public static void MemoryBarrier() => MemoryBarrier();
    }
}
