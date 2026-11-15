using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// A root arena. Owns its memory and gives it all back on Dispose.
    ///
    /// This is a ref struct on purpose: `using` on a ref struct is pattern-based (zerolib
    /// has no IDisposable), and being ref-like mechanically blocks boxing, storing in a
    /// field, and putting it in an array - which is most of the ownership enforcement C#
    /// can give us for free.
    /// </summary>
    public unsafe ref struct Arena
    {
        private ArenaCore* _core;

        internal Arena(ArenaCore* core) => _core = core;

        public static Arena Create()
            => Create(PageSource.Default, ArenaImpl.DefaultFirstChunk, ArenaImpl.DefaultMaxChunk);

        public static Arena Create(nuint firstChunkSize)
            => Create(PageSource.Default, firstChunkSize, ArenaImpl.DefaultMaxChunk);

        public static Arena Create(scoped in PageSource source, nuint firstChunkSize, nuint maxChunkSize)
        {
            ArenaCore* core = ArenaImpl.Create(source, firstChunkSize, maxChunkSize);
            if (core == null)
                Fail.Msg("arena: page source could not satisfy the initial reservation");

            return new Arena(core);
        }

        public readonly bool IsValid => _core != null && _core->Magic == ArenaCore.LiveMagic;

        public readonly ArenaStats Stats
        {
            get
            {
                Check.Handle(_core);
                return ArenaImpl.Stats(_core);
            }
        }

        public readonly nuint BytesUsed
        {
            get
            {
                Check.Handle(_core);
                return _core->TotalUsed;
            }
        }

        public readonly nuint HighWaterMark
        {
            get
            {
                Check.Handle(_core);
                return _core->HighWater;
            }
        }


        /// <summary>Validated core, for arenalib types that allocate on a handle's behalf.</summary>
        internal readonly ArenaCore* AllocationCore
        {
            get
            {
                Check.Handle(_core);
                Check.Depth(_core, 0);
                return _core;
            }
        }

        // ---- allocation ----

        public readonly void* AllocRaw(nuint bytes, nuint align)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return ArenaImpl.AllocRaw(_core, bytes, TypeInfo.NormalizeAlign(align), true);
        }

        public readonly void* AllocRawUninit(nuint bytes, nuint align)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return ArenaImpl.AllocRaw(_core, bytes, TypeInfo.NormalizeAlign(align), false);
        }

        public readonly T* Alloc<T>() where T : unmanaged
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return (T*)ArenaImpl.AllocRaw(_core, (nuint)sizeof(T), (nuint)(uint)TypeInfo.AlignOf<T>(), true);
        }

        public readonly T* AllocUninit<T>() where T : unmanaged
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return (T*)ArenaImpl.AllocRaw(_core, (nuint)sizeof(T), (nuint)(uint)TypeInfo.AlignOf<T>(), false);
        }

        public readonly T* New<T>(in T value) where T : unmanaged
        {
            T* slot = AllocUninit<T>();
            *slot = value;
            return slot;
        }

        public readonly Span<T> AllocSpan<T>(int count)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return ArenaImpl.AllocSpan<T>(_core, count, true);
        }

        public readonly Span<T> AllocSpanUninit<T>(int count)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return ArenaImpl.AllocSpan<T>(_core, count, false);
        }

        public readonly ArenaSlice<T> AllocSlice<T>(int count)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return ArenaImpl.AllocSlice<T>(_core, count, true);
        }

        public readonly ArenaSlice<T> AllocSliceUninit<T>(int count)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return ArenaImpl.AllocSlice<T>(_core, count, false);
        }

        public readonly ArenaRef<T> AllocRef<T>(in T value)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            return ArenaImpl.AllocRef<T>(_core, in value);
        }

        // ---- marks and rentals ----

        public readonly ArenaMark Mark()
        {
            Check.Handle(_core);
            return ArenaImpl.Mark(_core);
        }

        public readonly void Rewind(in ArenaMark mark)
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);
            ArenaImpl.Rewind(_core, in mark);
        }

        /// <summary>Reset the arena to empty, keeping every chunk as a spare for reuse.</summary>
        public readonly void Reset()
        {
            Check.Handle(_core);
            Check.Depth(_core, 0);

            while (_core->Head->Prev != null)
            {
                ArenaChunk* dead = _core->Head;
                _core->Head = dead->Prev;
                _core->ChunkCount--;
                dead->Prev = _core->Spare;
                _core->Spare = dead;
                _core->SpareCount++;
            }

            // Not 0: the ArenaCore lives at the bottom of this chunk.
            Check.Poison(
                (byte*)((nuint)_core->Head->Base + _core->BaseUsed),
                _core->Head->Used - _core->BaseUsed);

            _core->Head->Used = _core->BaseUsed;
            _core->TotalUsed = _core->BaseUsed;
            _core->Generation++;
        }


        /// <summary>
        /// Rent an INDEPENDENT block of memory as a transferable lease. Unlike Rent(), the
        /// result does not share this arena's chunks and does not rewind when this scope ends,
        /// so it is safe to hand to a callback or a worker. It inherits this arena's page
        /// source and max chunk size, and must be Released by whoever ends up owning it.
        /// </summary>
        public readonly ArenaLease RentBlock() => RentBlock(ArenaImpl.DefaultFirstChunk);

        public readonly ArenaLease RentBlock(nuint firstChunkSize)
        {
            Check.Handle(_core);
            return ArenaLease.Create(_core->Source, firstChunkSize, _core->MaxChunkSize);
        }

        /// <summary>
        /// Rent a child scope with its own chunk chain, borrowed from this arena's spare list.
        /// This arena stays freely allocatable while the child is live, siblings may be
        /// released in any order, and promoting a value out of the child is safe.
        /// </summary>
        public readonly ArenaRental Rent() => Rent(4096);

        public readonly ArenaRental Rent(nuint firstChunkSize)
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, firstChunkSize);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a child scope");

            return new ArenaRental(child);
        }

        /// <summary>Mark/rewind over this arena. See <see cref="ArenaCheckpoint"/> for the rules.</summary>
        public readonly ArenaCheckpoint Checkpoint()
        {
            ArenaCore* core = AllocationCore;
            ArenaMark mark = ArenaImpl.Mark(core);
            return new ArenaCheckpoint(core, mark, ArenaImpl.EnterChild(core));
        }

        /// <summary>
        /// Rent independent scratch space. Unlike Rent(), a scratch borrows whole chunks from
        /// this arena's spare list rather than taking a mark, so this arena stays freely
        /// allocatable while the scratch is live - which is what makes promoting a value out
        /// of the scratch (CloneTo) safe.
        /// </summary>
        public readonly ScratchArena RentScratch() => RentScratch(4096);

        public readonly ScratchArena RentScratch(nuint firstChunkSize)
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, firstChunkSize);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a scratch scope");

            return new ScratchArena(child);
        }

        public void Dispose()
        {
            // Unconditional: in release this is a silent no-op on a second call, and under
            // ARENA_CHECKS any later use trips Check.Handle on the null core.
            ArenaCore* core = _core;
            _core = null;
            ArenaImpl.Destroy(core);
        }
    }
}
