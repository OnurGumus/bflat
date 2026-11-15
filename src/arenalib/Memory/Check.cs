using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// Every lifetime check lives here and nowhere else. Under ARENA_CHECKS the bodies are
    /// real; otherwise they are empty and RyuJIT inlines them to nothing.
    ///
    /// Field layouts never differ between the checked and release builds - only statements
    /// are conditional - so the single reference assembly stays honest.
    ///
    /// Bounds checks are the deliberate exception: they are always on, matching Span&lt;T&gt;.
    /// </summary>
    internal static unsafe class Check
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Handle(ArenaCore* core)
        {
#if ARENA_CHECKS
            if (core == null)
                Fail.Msg("arena: use of a default or already-disposed handle");

            if (core->Magic != ArenaCore.LiveMagic)
                Fail.Msg("arena: handle refers to a destroyed arena");
#endif
        }

        /// <summary>LIFO: a handle may only be used while it is the innermost live one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Depth(ArenaCore* core, uint depth)
        {
#if ARENA_CHECKS
            if (core->ActiveDepth != depth)
                Fail.Msg("arena: LIFO violation - a nested rental is still live, or this rental was already released");
#endif
        }

        /// <summary>A lease keeps its arena's id so a moved-from or released lease is detectable.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Lease(ArenaCore* core, uint id)
        {
#if ARENA_CHECKS
            if (core == null)
                Fail.Msg("arena: use of a released or moved-from lease");

            if (core->Magic != ArenaCore.LiveMagic || core->Id != id)
                Fail.Msg("arena: lease refers to a destroyed arena");
#endif
        }

        /// <summary>A child scope handle is stale once its core has been released or reused.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ChildScope(ArenaCore* core, uint id)
        {
#if ARENA_CHECKS
            if (core == null)
                Fail.Msg("arena: use of a default child-scope handle");

            if (core->Magic != ArenaCore.LiveMagic || core->Id != id)
                Fail.Msg("arena: child scope already released (or its handle was copied and released twice)");
#endif
        }

        /// <summary>Disposing an arena that still owns live children loses their memory.</summary>
        public static void NoLiveChildren(ArenaCore* core)
        {
#if ARENA_CHECKS
            if (core->ActiveChildren != 0)
                Fail.Msg("arena: disposed while it still owns live child scopes (rentals/scratches)");

            if (core->ActiveDepth != 0)
                Fail.Msg("arena: disposed while a checkpoint is still open");
#endif
        }

        public static void MarkBelongsTo(ArenaCore* core, in ArenaMark mark)
        {
#if ARENA_CHECKS
            if (mark.Chunk == null)
                Fail.Msg("arena: default/uninitialized mark");

            if (mark.ArenaId != core->Id)
                Fail.Msg("arena: mark belongs to a different arena");
#endif
        }

        /// <summary>Validate a value (slice/ref) against the arena generation it was born in.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Value(ArenaCore* core, uint generation)
        {
#if ARENA_CHECKS
            if (core == null || core->Magic != ArenaCore.LiveMagic)
                Fail.Msg("arena: value outlived its arena");

            if (core->Generation != generation)
                Fail.Msg("arena: use-after-rewind - this value was allocated before a rewind or scratch release");
#endif
        }

        public static void Poison(byte* start, nuint bytes)
        {
#if ARENA_CHECKS
            if (bytes != 0)
                MemOps.Set(start, 0xDD, bytes);
#endif
        }

        /// <summary>Always on, like Span&lt;T&gt;. Out-of-range access is memory corruption, not a lifetime bug.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Bounds(int index, int length)
        {
            if ((uint)index >= (uint)length)
                Fail.Msg("arena: index out of range");
        }

        public static void Range(int start, int length, int actual)
        {
            if ((uint)start > (uint)actual || (uint)length > (uint)(actual - start))
                Fail.Msg("arena: slice range out of bounds");
        }
    }

    /// <summary>
    /// The one member that intentionally differs between the release and checked builds of
    /// arenalib. RyuJIT folds it, so `if (ArenaConfig.ChecksEnabled)` costs nothing.
    /// </summary>
    public static class ArenaConfig
    {
        public static bool ChecksEnabled =>
#if ARENA_CHECKS
            true;
#else
            false;
#endif
    }
}
