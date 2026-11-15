using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// A child scope with an <b>independent</b> chunk chain, borrowed from its parent's spare
    /// list and returned there on disposal.
    ///
    /// Independence is the whole point. A mark-backed child cannot safely coexist with any
    /// allocation into its parent - not promotion, not logging into a parent buffer, not
    /// growing a parent collection - because every one of those lands above the child's mark
    /// and dies when the child rewinds. Owning chunks outright removes the entire class of bug,
    /// and as a bonus siblings can be released in any order.
    ///
    /// The handle is a small immutable descriptor pointing at a mutable core: allocation
    /// mutates the core, never the descriptor. That is what lets it work with <c>using var</c>,
    /// which makes the local readonly and forbids passing it by ref.
    ///
    /// Costs nothing in steady state: chunks come from, and go back to, the parent's spares.
    /// </summary>
    public unsafe ref struct ArenaRental
    {
        private ArenaCore* _core;
        private readonly uint _id;

        internal ArenaRental(ArenaCore* core)
        {
            _core = core;
            _id = core == null ? 0 : core->Id;
        }

        public readonly bool IsValid
            => _core != null && _core->Magic == ArenaCore.LiveMagic && _core->Id == _id;

        public readonly ArenaStats Stats
        {
            get
            {
                Check.ChildScope(_core, _id);
                return ArenaImpl.Stats(_core);
            }
        }

        /// <summary>Bytes allocated inside this scope.</summary>
        public readonly nuint BytesUsed
        {
            get
            {
                Check.ChildScope(_core, _id);
                return _core->TotalUsed - _core->BaseUsed;
            }
        }

        internal readonly ArenaCore* AllocationCore
        {
            get
            {
                Check.ChildScope(_core, _id);
                Check.Depth(_core, 0);
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

        public readonly ArenaSlice<T> AllocSliceUninit<T>(int count) => ArenaImpl.AllocSlice<T>(AllocationCore, count, false);

        public readonly ArenaRef<T> AllocRef<T>(in T value) => ArenaImpl.AllocRef<T>(AllocationCore, in value);

        // ---- nesting ----

        public readonly ArenaRental Rent() => Rent(4096);

        public readonly ArenaRental Rent(nuint firstChunkSize)
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, firstChunkSize);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a child scope");

            return new ArenaRental(child);
        }

        public readonly ScratchArena RentScratch() => RentScratch(4096);

        public readonly ScratchArena RentScratch(nuint firstChunkSize)
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, firstChunkSize);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a scratch scope");

            return new ScratchArena(child);
        }

        public readonly ArenaLease RentBlock() => RentBlock(ArenaImpl.DefaultFirstChunk);

        public readonly ArenaLease RentBlock(nuint firstChunkSize)
        {
            ArenaCore* core = AllocationCore;
            return ArenaLease.Create(core->Source, firstChunkSize, core->MaxChunkSize);
        }

        /// <summary>Mark/rewind within this scope. See <see cref="ArenaCheckpoint"/> for the rules.</summary>
        public readonly ArenaCheckpoint Checkpoint()
        {
            ArenaCore* core = AllocationCore;
            ArenaMark mark = ArenaImpl.Mark(core);
            return new ArenaCheckpoint(core, mark, ArenaImpl.EnterChild(core));
        }

        public void Dispose()
        {
            ArenaCore* core = _core;
            _core = null;
            ArenaImpl.ReleaseChild(core, _id);
        }
    }
}
