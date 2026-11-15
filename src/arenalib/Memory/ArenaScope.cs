using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// A validated, type-erased reference to whichever arena scope you are allocating from.
    ///
    /// Every handle converts to one implicitly, so a library type needs a single
    /// <c>Create(ArenaScope)</c> overload instead of one per handle kind:
    /// <code>
    ///   using var request = app.Rent();
    ///   var names = ArenaList&lt;int&gt;.Create(request);      // no ref, no overload explosion
    /// </code>
    /// The conversion performs the handle's own validity and checkpoint checks, so by the time
    /// you hold an ArenaScope the core behind it has already been vetted.
    ///
    /// It is not a ref struct: it holds a bare pointer, so it can be stored inside collections
    /// that live in the arena. It carries no ownership - disposing is always the handle's job.
    /// </summary>
    public readonly unsafe struct ArenaScope
    {
        internal readonly ArenaCore* Core;

        internal ArenaScope(ArenaCore* core) => Core = core;

        public bool IsValid => Core != null && Core->Magic == ArenaCore.LiveMagic;

        public ArenaStats Stats
        {
            get
            {
                Check.Handle(Core);
                return ArenaImpl.Stats(Core);
            }
        }

        public static implicit operator ArenaScope(Arena handle) => new ArenaScope(handle.AllocationCore);

        public static implicit operator ArenaScope(ArenaRental handle) => new ArenaScope(handle.AllocationCore);

        public static implicit operator ArenaScope(ScratchArena handle) => new ArenaScope(handle.AllocationCore);

        public static implicit operator ArenaScope(ArenaCheckpoint handle) => new ArenaScope(handle.AllocationCore);

        public static implicit operator ArenaScope(ArenaLease handle) => new ArenaScope(handle.AllocationCore);

        public static implicit operator ArenaScope(RcArenaLease handle) => new ArenaScope(handle.AllocationCore);

        // ---- allocation ----

        public void* AllocRaw(nuint bytes, nuint align)
            => ArenaImpl.AllocRaw(Core, bytes, TypeInfo.NormalizeAlign(align), true);

        public void* AllocRawUninit(nuint bytes, nuint align)
            => ArenaImpl.AllocRaw(Core, bytes, TypeInfo.NormalizeAlign(align), false);

        public Span<T> AllocSpan<T>(int count) => ArenaImpl.AllocSpan<T>(Core, count, true);

        public ArenaSlice<T> AllocSlice<T>(int count) => ArenaImpl.AllocSlice<T>(Core, count, true);

        public ArenaRef<T> AllocRef<T>(in T value) => ArenaImpl.AllocRef<T>(Core, in value);

        internal uint Generation => Core->Generation;
    }
}
