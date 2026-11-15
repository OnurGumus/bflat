using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// Mark-and-rewind over an arena's live chunk chain. This is a restricted advanced
    /// primitive, deliberately <b>not</b> what <c>Rent()</c> or <c>RentScratch()</c> mean.
    ///
    /// <b>Contract: nothing else may allocate into this arena while a checkpoint is open.</b>
    ///
    /// The reason is structural, and it is why rentals and scratches own independent chunks
    /// instead. A checkpoint is just an offset:
    /// <code>
    ///   arena offset   = 100
    ///   checkpoint     = 100
    ///   inner work allocates to 200
    ///   another handle allocates 200..220     &lt;-- lands above the mark
    ///   checkpoint.Dispose() rewinds to 100   &lt;-- and takes 200..220 with it
    /// </code>
    /// That kills promotion, logging into a parent buffer, growing a parent collection, or any
    /// other allocation made through a different handle. Under ARENA_CHECKS the depth check
    /// catches it; in release it silently corrupts.
    ///
    /// Use it for a tightly controlled inner algorithm that owns the arena for its duration.
    /// For anything else, rent a child.
    /// </summary>
    public unsafe ref struct ArenaCheckpoint
    {
        private ArenaCore* _core;
        private readonly ArenaMark _mark;
        private readonly uint _depth;

        internal ArenaCheckpoint(ArenaCore* core, in ArenaMark mark, uint depth)
        {
            _core = core;
            _mark = mark;
            _depth = depth;
        }

        public readonly bool IsOpen => _core != null && _core->Magic == ArenaCore.LiveMagic;

        /// <summary>Bytes allocated since the checkpoint was taken.</summary>
        public readonly nuint BytesUsed
        {
            get
            {
                Check.Handle(_core);
                return _core->TotalUsed - _mark.Total;
            }
        }

        internal readonly ArenaCore* AllocationCore
        {
            get
            {
                Check.Handle(_core);
                Check.Depth(_core, _depth);
                return _core;
            }
        }

        // ---- allocation ----

        public readonly void* AllocRaw(nuint bytes, nuint align)
            => ArenaImpl.AllocRaw(AllocationCore, bytes, TypeInfo.NormalizeAlign(align), true);

        public readonly void* AllocRawUninit(nuint bytes, nuint align)
            => ArenaImpl.AllocRaw(AllocationCore, bytes, TypeInfo.NormalizeAlign(align), false);

        public readonly T* Alloc<T>() where T : unmanaged
            => (T*)ArenaImpl.AllocRaw(AllocationCore, (nuint)sizeof(T), (nuint)(uint)TypeInfo.AlignOf<T>(), true);

        public readonly T* AllocUninit<T>() where T : unmanaged
            => (T*)ArenaImpl.AllocRaw(AllocationCore, (nuint)sizeof(T), (nuint)(uint)TypeInfo.AlignOf<T>(), false);

        public readonly T* New<T>(in T value) where T : unmanaged
        {
            T* slot = AllocUninit<T>();
            *slot = value;
            return slot;
        }

        public readonly Span<T> AllocSpan<T>(int count) => ArenaImpl.AllocSpan<T>(AllocationCore, count, true);

        public readonly Span<T> AllocSpanUninit<T>(int count) => ArenaImpl.AllocSpan<T>(AllocationCore, count, false);

        public readonly ArenaSlice<T> AllocSlice<T>(int count) => ArenaImpl.AllocSlice<T>(AllocationCore, count, true);

        public readonly ArenaRef<T> AllocRef<T>(in T value) => ArenaImpl.AllocRef<T>(AllocationCore, in value);

        /// <summary>A nested checkpoint, which must be disposed before this one.</summary>
        public readonly ArenaCheckpoint Checkpoint()
        {
            ArenaCore* core = AllocationCore;
            ArenaMark mark = ArenaImpl.Mark(core);
            return new ArenaCheckpoint(core, mark, ArenaImpl.EnterChild(core));
        }

        /// <summary>Rewind the arena to where it was when this checkpoint was taken.</summary>
        public void Dispose()
        {
            ArenaCore* core = _core;
            _core = null;

            if (core == null)
                return;

            ArenaImpl.ExitChild(core, _depth, in _mark);
        }
    }
}
