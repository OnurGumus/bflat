using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// Ready-made hash and equality functions.
    ///
    /// zerolib's <c>System.Object</c> has no members at all - no GetHashCode, no Equals - and
    /// there are no interfaces to dispatch through either, so hashed collections take managed
    /// function pointers instead of an IEqualityComparer. These are the presets; a custom key
    /// just needs two static methods with the right shape.
    ///
    /// <c>in</c> rather than <c>ref readonly</c> on purpose: it needs no extra attribute, so
    /// user code can write its own hashers without depending on arenalib's Compat shims.
    /// </summary>
    public static unsafe class Hashers
    {
        /// <summary>SplitMix64 finalizer. Good avalanche, four instructions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        public static ulong HashInt32(in int key) => Mix((ulong)(uint)key);
        public static bool EqualsInt32(in int a, in int b) => a == b;
        public static int CompareInt32(in int a, in int b) => a < b ? -1 : (a > b ? 1 : 0);

        public static ulong HashUInt32(in uint key) => Mix(key);
        public static bool EqualsUInt32(in uint a, in uint b) => a == b;
        public static int CompareUInt32(in uint a, in uint b) => a < b ? -1 : (a > b ? 1 : 0);

        public static ulong HashInt64(in long key) => Mix((ulong)key);
        public static bool EqualsInt64(in long a, in long b) => a == b;
        public static int CompareInt64(in long a, in long b) => a < b ? -1 : (a > b ? 1 : 0);

        public static ulong HashUInt64(in ulong key) => Mix(key);
        public static bool EqualsUInt64(in ulong a, in ulong b) => a == b;
        public static int CompareUInt64(in ulong a, in ulong b) => a < b ? -1 : (a > b ? 1 : 0);

        public static ulong HashByte(in byte key) => Mix(key);
        public static bool EqualsByte(in byte a, in byte b) => a == b;
        public static int CompareByte(in byte a, in byte b) => a - b;

        public static ulong HashChar(in char key) => Mix(key);
        public static bool EqualsChar(in char a, in char b) => a == b;
        public static int CompareChar(in char a, in char b) => a - b;

        public static ulong HashArenaString(in ArenaString key) => key.Hash();
        public static bool EqualsArenaString(in ArenaString a, in ArenaString b) => a.Equals(b);
        public static int CompareArenaString(in ArenaString a, in ArenaString b) => a.CompareOrdinal(b.AsView());

        // Function-pointer accessors, so call sites read as Hashers.Int32 rather than &Method.

        public static delegate*<in int, ulong> Int32Hash => &HashInt32;
        public static delegate*<in int, in int, bool> Int32Equals => &EqualsInt32;
        public static delegate*<in int, in int, int> Int32Compare => &CompareInt32;

        public static delegate*<in uint, ulong> UInt32Hash => &HashUInt32;
        public static delegate*<in uint, in uint, bool> UInt32Equals => &EqualsUInt32;
        public static delegate*<in uint, in uint, int> UInt32Compare => &CompareUInt32;

        public static delegate*<in long, ulong> Int64Hash => &HashInt64;
        public static delegate*<in long, in long, bool> Int64Equals => &EqualsInt64;
        public static delegate*<in long, in long, int> Int64Compare => &CompareInt64;

        public static delegate*<in ulong, ulong> UInt64Hash => &HashUInt64;
        public static delegate*<in ulong, in ulong, bool> UInt64Equals => &EqualsUInt64;
        public static delegate*<in ulong, in ulong, int> UInt64Compare => &CompareUInt64;

        public static delegate*<in ArenaString, ulong> ArenaStringHash => &HashArenaString;
        public static delegate*<in ArenaString, in ArenaString, bool> ArenaStringEquals => &EqualsArenaString;
        public static delegate*<in ArenaString, in ArenaString, int> ArenaStringCompare => &CompareArenaString;
    }
}
