using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// A transferable, independently-owned arena.
    ///
    /// Rentals are lexical: they share the parent's chunks and rewind on scope exit, which is
    /// exactly right for nested synchronous work and exactly wrong for anything that outlives
    /// the call that started it. A lease instead owns its own chunk chain, so it has no LIFO
    /// relationship with anything and can be handed to a callback, a work queue, or a worker.
    ///
    /// The rule is: <b>scratch and rentals are lexical, leases are transferable.</b>
    ///
    /// This is NOT a ref struct - it has to be storable in a context object, which is the whole
    /// point. That means C# cannot stop you from copying it, so transfer goes through
    /// <see cref="Move"/>, which invalidates the source. Reusing a moved-from lease is caught
    /// under ARENA_CHECKS.
    /// </summary>
    public unsafe struct ArenaLease
    {
        private ArenaCore* _core;
        private uint _id;

        internal ArenaLease(ArenaCore* core)
        {
            _core = core;
            _id = core == null ? 0 : core->Id;
        }

        public static ArenaLease Create()
            => Create(PageSource.Default, ArenaImpl.DefaultFirstChunk, ArenaImpl.DefaultMaxChunk);

        public static ArenaLease Create(nuint firstChunkSize)
            => Create(PageSource.Default, firstChunkSize, ArenaImpl.DefaultMaxChunk);

        public static ArenaLease Create(scoped in PageSource source, nuint firstChunkSize, nuint maxChunkSize)
        {
            ArenaCore* core = ArenaImpl.Create(source, firstChunkSize, maxChunkSize);
            if (core == null)
                Fail.Msg("arena: page source could not satisfy a lease reservation");

            return new ArenaLease(core);
        }

        public readonly bool IsValid => _core != null && _core->Magic == ArenaCore.LiveMagic && _core->Id == _id;

        internal readonly ArenaCore* AllocationCore
        {
            get
            {
                Check.Lease(_core, _id);
                Check.Depth(_core, 0);
                return _core;
            }
        }

        public readonly ArenaStats Stats
        {
            get
            {
                Check.Lease(_core, _id);
                return ArenaImpl.Stats(_core);
            }
        }

        public readonly nuint BytesUsed
        {
            get
            {
                Check.Lease(_core, _id);
                return _core->TotalUsed;
            }
        }

        // ---- allocation ----

        public readonly void* AllocRaw(nuint bytes, nuint align)
            => ArenaImpl.AllocRaw(AllocationCore, bytes, TypeInfo.NormalizeAlign(align), true);

        public readonly T* Alloc<T>() where T : unmanaged
            => (T*)ArenaImpl.AllocRaw(AllocationCore, (nuint)sizeof(T), (nuint)(uint)TypeInfo.AlignOf<T>(), true);

        public readonly T* New<T>(in T value) where T : unmanaged
        {
            T* slot = (T*)ArenaImpl.AllocRaw(AllocationCore, (nuint)sizeof(T), (nuint)(uint)TypeInfo.AlignOf<T>(), false);
            *slot = value;
            return slot;
        }

        public readonly Span<T> AllocSpan<T>(int count) => ArenaImpl.AllocSpan<T>(AllocationCore, count, true);

        public readonly ArenaSlice<T> AllocSlice<T>(int count) => ArenaImpl.AllocSlice<T>(AllocationCore, count, true);

        public readonly ArenaRef<T> AllocRef<T>(in T value) => ArenaImpl.AllocRef<T>(AllocationCore, in value);

        /// <summary>Nested independent child scope inside the lease, for synchronous work.</summary>
        public readonly ArenaRental Rent()
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, 4096);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a child scope");

            return new ArenaRental(child);
        }

        public readonly ScratchArena RentScratch()
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, 4096);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a scratch scope");

            return new ScratchArena(child);
        }

        /// <summary>Discard everything allocated so far but keep the chunks, ready for the next unit of work.</summary>
        public readonly void Recycle()
        {
            ArenaCore* core = AllocationCore;

            while (core->Head->Prev != null)
            {
                ArenaChunk* dead = core->Head;
                core->Head = dead->Prev;
                core->ChunkCount--;
                dead->Prev = core->Spare;
                core->Spare = dead;
                core->SpareCount++;
            }

            core->Head->Used = core->BaseUsed;
            core->TotalUsed = core->BaseUsed;
            core->Generation++;
        }

        /// <summary>
        /// Transfer ownership. The source becomes invalid; the returned lease is the only
        /// live handle. C# has no move-only structs, so this is a convention the runtime
        /// checks rather than something the compiler proves.
        /// </summary>
        public static ArenaLease Move(ref ArenaLease source)
        {
            ArenaLease moved = source;
            source._core = null;
            source._id = 0;
            return moved;
        }

        /// <summary>Give the memory back. Idempotent.</summary>
        public void Release()
        {
            ArenaCore* core = _core;
            _core = null;
            _id = 0;
            ArenaImpl.Destroy(core);
        }

        /// <summary>Lets a lease be used with `using` when it happens to be scope-bound after all.</summary>
        public void Dispose() => Release();
    }

    /// <summary>
    /// A reference-counted arena ("Rc" as in single-threaded, NOT atomic - a future
    /// thread-safe variant would be ArcArenaLease). For the case hierarchical ownership
    /// genuinely cannot express:
    /// several outstanding operations over one context that complete in arbitrary order (a
    /// socket read, a timeout, and a cancellation, say). Each outstanding operation holds a
    /// reference; the arena dies when the last one drops.
    ///
    /// <b>The count is not atomic.</b> zerolib has no Interlocked and no way for arenalib to add
    /// one (IntrinsicAttribute is internal to zerolib), so this is safe for a single-threaded
    /// event loop - which is the shape most of these programs have - and is NOT safe across
    /// threads. Making it thread-safe requires an interlocked increment in zerolib first.
    /// </summary>
    public unsafe struct RcArenaLease
    {
        private ArenaCore* _core;
        private uint _id;

        public static RcArenaLease Create()
            => Create(PageSource.Default, ArenaImpl.DefaultFirstChunk, ArenaImpl.DefaultMaxChunk);

        public static RcArenaLease Create(scoped in PageSource source, nuint firstChunkSize, nuint maxChunkSize)
        {
            ArenaCore* core = ArenaImpl.Create(source, firstChunkSize, maxChunkSize);
            if (core == null)
                Fail.Msg("arena: page source could not satisfy a shared lease reservation");

            core->RefCount = 1;

            RcArenaLease lease = default;
            lease._core = core;
            lease._id = core->Id;
            return lease;
        }

        /// <summary>Adopt an existing exclusive lease into shared ownership. The source is consumed.</summary>
        public static RcArenaLease FromExclusive(ref ArenaLease source)
        {
            ArenaLease moved = ArenaLease.Move(ref source);
            ArenaCore* core = moved.AllocationCore;
            core->RefCount = 1;

            RcArenaLease lease = default;
            lease._core = core;
            lease._id = core->Id;
            return lease;
        }

        public readonly bool IsValid => _core != null && _core->Magic == ArenaCore.LiveMagic && _core->Id == _id;

        public readonly int ReferenceCount
        {
            get
            {
                Check.Lease(_core, _id);
                return _core->RefCount;
            }
        }

        internal readonly ArenaCore* AllocationCore
        {
            get
            {
                Check.Lease(_core, _id);
                Check.Depth(_core, 0);
                return _core;
            }
        }

        public readonly ArenaStats Stats
        {
            get
            {
                Check.Lease(_core, _id);
                return ArenaImpl.Stats(_core);
            }
        }

        public readonly void* AllocRaw(nuint bytes, nuint align)
            => ArenaImpl.AllocRaw(AllocationCore, bytes, TypeInfo.NormalizeAlign(align), true);

        public readonly T* Alloc<T>() where T : unmanaged
            => (T*)ArenaImpl.AllocRaw(AllocationCore, (nuint)sizeof(T), (nuint)(uint)TypeInfo.AlignOf<T>(), true);

        public readonly Span<T> AllocSpan<T>(int count) => ArenaImpl.AllocSpan<T>(AllocationCore, count, true);

        public readonly ArenaSlice<T> AllocSlice<T>(int count) => ArenaImpl.AllocSlice<T>(AllocationCore, count, true);

        public readonly ArenaRental Rent()
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, 4096);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a child scope");

            return new ArenaRental(child);
        }

        public readonly ScratchArena RentScratch()
        {
            ArenaCore* child = ArenaImpl.CreateChild(AllocationCore, 4096);
            if (child == null)
                Fail.Msg("arena: could not acquire memory for a scratch scope");

            return new ScratchArena(child);
        }

        /// <summary>Take an additional reference, one per outstanding operation.</summary>
        public RcArenaLease Retain()
        {
            Check.Lease(_core, _id);
            _core->RefCount++;

            RcArenaLease copy = default;
            copy._core = _core;
            copy._id = _id;
            return copy;
        }

        /// <summary>Drop this reference. The arena dies when the last one goes.</summary>
        public void Release()
        {
            ArenaCore* core = _core;
            _core = null;
            _id = 0;

            if (core == null || core->Magic != ArenaCore.LiveMagic)
                return;

            core->RefCount--;

            if (core->RefCount <= 0)
                ArenaImpl.Destroy(core);
        }

        public void Dispose() => Release();
    }
}
