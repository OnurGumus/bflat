using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// The arena engine. All three public handle types (Arena, ArenaRental, ScratchArena)
    /// are thin forwarders onto these operations.
    /// </summary>
    internal static unsafe class ArenaImpl
    {
        internal const nuint DefaultFirstChunk = 64 * 1024;
        internal const nuint DefaultMaxChunk = 4 * 1024 * 1024;

        // Arenas are single-threaded by design, so a plain counter is fine. No initializer,
        // so this needs no class constructor.
        private static uint s_nextId;

        // ---- lifecycle ----

        public static ArenaCore* Create(scoped in PageSource source, nuint firstChunkSize, nuint maxChunkSize)
        {
            if (!source.IsValid)
                Fail.Msg("arena: page source has no Reserve function");

            if (firstChunkSize < 4096)
                firstChunkSize = 4096;
            if (maxChunkSize < firstChunkSize)
                maxChunkSize = firstChunkSize;

            ArenaChunk* chunk = NewChunk(source, firstChunkSize, null, 0, ChunkKind.Normal);
            if (chunk == null)
                return null;

            // Self-hosting: the core lives in the arena's own first chunk.
            ArenaCore* core = (ArenaCore*)BumpIn(chunk, (nuint)sizeof(ArenaCore), 16);
            if (core == null)
            {
                if (source.CanRelease)
                    source.Release(chunk->SourceBase, chunk->SourceSize);
                return null;
            }

            MemOps.Zero(core, (nuint)sizeof(ArenaCore));

            core->Magic = ArenaCore.LiveMagic;
            core->Id = ++s_nextId;
            core->Generation = 1;
            core->ActiveDepth = 0;
            core->Head = chunk;
            core->Spare = null;
            core->Source = source;
            core->NextChunkSize = firstChunkSize * 2 > maxChunkSize ? maxChunkSize : firstChunkSize * 2;
            core->MaxChunkSize = maxChunkSize;
            core->BaseUsed = chunk->Used;
            core->TotalUsed = chunk->Used;
            core->HighWater = chunk->Used;
            core->ChunkCount = 1;
            core->SpareCount = 0;
            core->ReservationCount = 1;
            core->BytesReserved = chunk->SourceSize;

            return core;
        }

        public static void Destroy(ArenaCore* core)
        {
            if (core == null || core->Magic != ArenaCore.LiveMagic)
                return;

            Check.NoLiveChildren(core);

            // The core itself lives inside the first chunk, so copy everything we need out
            // before releasing anything.
            PageSource source = core->Source;
            ArenaChunk* head = core->Head;
            ArenaChunk* spare = core->Spare;

            core->Magic = ArenaCore.DeadMagic;

            if (!source.CanRelease)
                return;

            while (spare != null)
            {
                ArenaChunk* prev = spare->Prev;
                source.Release(spare->SourceBase, spare->SourceSize);
                spare = prev;
            }

            while (head != null)
            {
                ArenaChunk* prev = head->Prev;
                source.Release(head->SourceBase, head->SourceSize);
                head = prev;
            }
        }

        /// <summary>
        /// The owning root of an arena tree. Chunk reservation accounting and the spare pool
        /// both live here, so that chunks migrating between a parent and its children never
        /// drift the counters and never strand free memory in a scope that is not using it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ArenaCore* RootOf(ArenaCore* core)
        {
            while (core->Parent != null)
                core = core->Parent;

            return core;
        }

        // ---- chunks ----

        private static ArenaChunk* NewChunk(
            scoped in PageSource source, nuint payload, ArenaChunk* prev, nuint totalBefore, ChunkKind kind)
        {
            nuint overhead = (nuint)sizeof(ArenaChunk) + 16;
            nuint want = payload + overhead;
            if (want < payload)
                return null;   // overflow

            nuint aligned = TypeInfo.AlignUp(want, source.Granularity);
            if (aligned < want)
                return null;   // rounding up to the granularity wrapped

            want = aligned;

            void* memory = source.Reserve(want);
            if (memory == null)
                return null;

            ArenaChunk* chunk = (ArenaChunk*)memory;
            byte* payloadStart = (byte*)TypeInfo.AlignUp((nuint)memory + (nuint)sizeof(ArenaChunk), 16);

            chunk->Prev = prev;
            chunk->Base = payloadStart;
            chunk->Size = want - ((nuint)payloadStart - (nuint)memory);
            chunk->Used = 0;
            chunk->TotalBefore = totalBefore;
            chunk->SourceBase = memory;
            chunk->SourceSize = want;
            chunk->Kind = kind;

            return chunk;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* BumpIn(ArenaChunk* chunk, nuint size, nuint align)
        {
            nuint offset = TypeInfo.AlignUp(chunk->Used, align);
            nuint end = offset + size;

            if (end < offset || end > chunk->Size)
                return null;

            chunk->Used = end;
            return (void*)((nuint)chunk->Base + offset);
        }

        // ---- allocation ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AllocRaw(ArenaCore* core, nuint size, nuint align, bool zero)
        {
            void* result = BumpIn(core->Head, size, align);
            if (result == null)
                result = AllocSlow(core, size, align);

            if (result == null)
                Fail.Msg("arena: out of memory");

            core->TotalUsed = core->Head->TotalBefore + core->Head->Used;
            if (core->TotalUsed > core->HighWater)
                core->HighWater = core->TotalUsed;

            if (zero)
                MemOps.Zero(result, size);

            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void* AllocSlow(ArenaCore* core, nuint size, nuint align)
        {
            nuint totalBefore = core->Head->TotalBefore + core->Head->Used;
            nuint need = size + align;
            if (need < size)
                return null;   // overflow

            // Oversized request: give it a dedicated chunk that gets released on rewind
            // rather than retained as a spare.
            if (need > core->MaxChunkSize / 2)
            {
                ArenaChunk* huge = NewChunk(core->Source, need, core->Head, totalBefore, ChunkKind.Huge);
                if (huge == null)
                    return null;

                CountReservation(core, huge);
                PushChunk(core, huge);
                return BumpIn(huge, size, align);
            }

            // Reuse a retained chunk before asking the OS again. The spare pool is shared by
            // the whole arena tree, so a child growing here reuses chunks a sibling or the
            // parent released - which is what makes the per-request loop allocation-free.
            ArenaChunk* spare = TakeSpare(core, need);

            if (spare != null)
            {
                spare->Used = 0;
                spare->TotalBefore = totalBefore;
                spare->Prev = core->Head;
                core->Head = spare;
                core->ChunkCount++;

                return BumpIn(spare, size, align);
            }

            nuint want = core->NextChunkSize;
            if (want < need)
                want = need;

            ArenaChunk* fresh = NewChunk(core->Source, want, core->Head, totalBefore, ChunkKind.Normal);
            if (fresh == null)
                return null;

            CountReservation(core, fresh);

            if (core->NextChunkSize < core->MaxChunkSize)
            {
                core->NextChunkSize *= 2;
                if (core->NextChunkSize > core->MaxChunkSize)
                    core->NextChunkSize = core->MaxChunkSize;
            }

            PushChunk(core, fresh);
            return BumpIn(fresh, size, align);
        }

        private static void PushChunk(ArenaCore* core, ArenaChunk* chunk)
        {
            core->Head = chunk;
            core->ChunkCount++;
        }

        /// <summary>Record a fresh page-source reservation against the tree's root.</summary>
        private static void CountReservation(ArenaCore* core, ArenaChunk* chunk)
        {
            ArenaCore* root = RootOf(core);
            root->ReservationCount++;
            root->BytesReserved += chunk->SourceSize;
        }

        /// <summary>
        /// Grow the most recent allocation in place. Returns false if it is not on top, or
        /// does not fit. This is what stops arena lists and string builders from leaving a
        /// trail of abandoned buffers behind.
        /// </summary>
        public static bool TryExtendInPlace(ArenaCore* core, void* block, nuint oldSize, nuint newSize)
        {
            if (newSize <= oldSize)
                return true;

            ArenaChunk* head = core->Head;
            nuint blockOffset = (nuint)block - (nuint)head->Base;

            if ((nuint)block < (nuint)head->Base || blockOffset + oldSize != head->Used)
                return false;   // not the most recent allocation

            nuint end = blockOffset + newSize;
            if (end < blockOffset || end > head->Size)
                return false;

            head->Used = end;
            core->TotalUsed = head->TotalBefore + head->Used;
            if (core->TotalUsed > core->HighWater)
                core->HighWater = core->TotalUsed;

            MemOps.Zero((void*)((nuint)block + oldSize), newSize - oldSize);
            return true;
        }

        /// <summary>Give back the most recent allocation. Returns false if it is not on top.</summary>
        public static bool TryPopIfTop(ArenaCore* core, void* block, nuint size)
        {
            ArenaChunk* head = core->Head;
            nuint blockOffset = (nuint)block - (nuint)head->Base;

            if ((nuint)block < (nuint)head->Base || blockOffset + size != head->Used)
                return false;

            head->Used = blockOffset;
            core->TotalUsed = head->TotalBefore + head->Used;
            return true;
        }

        /// <summary>Detach a spare chunk of at least <paramref name="need"/> usable bytes.</summary>
        private static ArenaChunk* TakeSpare(ArenaCore* owner, nuint need)
        {
            ArenaCore* core = RootOf(owner);

            ArenaChunk* previous = null;
            ArenaChunk* spare = core->Spare;

            while (spare != null && spare->Size < need)
            {
                previous = spare;
                spare = spare->Prev;
            }

            if (spare == null)
                return null;

            if (previous == null)
                core->Spare = spare->Prev;
            else
                previous->Prev = spare->Prev;

            core->SpareCount--;
            return spare;
        }

        /// <summary>Hand a chunk to <paramref name="core"/>'s spare list, or back to the OS if it is oversized.</summary>
        private static void PushSpare(ArenaCore* owner, ArenaChunk* chunk)
        {
            ArenaCore* core = RootOf(owner);

            if (chunk->Kind == ChunkKind.Huge && core->Source.CanRelease)
            {
                core->ReservationCount--;
                core->BytesReserved -= chunk->SourceSize;
                core->Source.Release(chunk->SourceBase, chunk->SourceSize);
                return;
            }

            chunk->Prev = core->Spare;
            core->Spare = chunk;
            core->SpareCount++;
        }

        // ---- scratch children ----

        /// <summary>
        /// Create an independent scratch arena whose chunks are borrowed from
        /// <paramref name="parent"/>'s spare list and returned there on release.
        ///
        /// Independent memory is the whole point: a mark-based scratch shares the parent's
        /// bump pointer, so anything allocated from an ancestor while the scratch is live
        /// lands above the scratch's mark and dies with it - which breaks promotion
        /// (CloneTo) out of a scratch scope, the operation the whole design is built around.
        /// </summary>
        public static ArenaCore* CreateChild(ArenaCore* parent, nuint firstChunkSize)
        {
            nuint overhead = (nuint)sizeof(ArenaCore) + 32;
            nuint need = firstChunkSize + overhead;

            ArenaChunk* chunk = TakeSpare(parent, need);
            if (chunk == null)
            {
                chunk = NewChunk(parent->Source, need, null, 0, ChunkKind.Normal);
                if (chunk == null)
                    return null;

                CountReservation(parent, chunk);
            }

            chunk->Prev = null;
            chunk->Used = 0;
            chunk->TotalBefore = 0;

            // A borrowed spare may carry a stale Kind; the child's control block lives here,
            // so it must never be mistaken for a one-shot Huge chunk.
            chunk->Kind = ChunkKind.Normal;

            ArenaCore* core = (ArenaCore*)BumpIn(chunk, (nuint)sizeof(ArenaCore), 16);
            if (core == null)
            {
                PushSpare(parent, chunk);
                return null;
            }

            MemOps.Zero(core, (nuint)sizeof(ArenaCore));

            core->Magic = ArenaCore.LiveMagic;
            core->Id = ++s_nextId;
            core->Generation = 1;
            core->Parent = parent;
            core->Head = chunk;
            core->Source = parent->Source;
            core->NextChunkSize = parent->NextChunkSize;
            core->MaxChunkSize = parent->MaxChunkSize;
            core->BaseUsed = chunk->Used;
            core->TotalUsed = chunk->Used;
            core->HighWater = chunk->Used;
            core->ChunkCount = 1;

            parent->ActiveChildren++;
            return core;
        }

        /// <summary>
        /// Release a child scope, returning every chunk it holds to the parent's spare list.
        ///
        /// Validated against the handle's id so that a copied handle disposed twice is caught
        /// while the control block is still mapped. Once the parent itself is gone, no promise
        /// is made - inspecting a stale handle at that point is already use-after-free.
        /// </summary>
        public static void ReleaseChild(ArenaCore* child, uint id)
        {
            Check.ChildScope(child, id);

            if (child == null || child->Magic != ArenaCore.LiveMagic || child->Id != id)
                return;

            Check.NoLiveChildren(child);

            // The child's core lives inside one of these chunks, so copy out first.
            ArenaCore* parent = child->Parent;
            ArenaChunk* head = child->Head;
            ArenaChunk* spare = child->Spare;

            child->Magic = ArenaCore.DeadMagic;

            while (spare != null)
            {
                ArenaChunk* previous = spare->Prev;
                PoisonExceptCore(spare, child);
                PushSpare(parent, spare);
                spare = previous;
            }

            while (head != null)
            {
                ArenaChunk* previous = head->Prev;
                PoisonExceptCore(head, child);
                PushSpare(parent, head);
                head = previous;
            }

            if (parent != null)
                parent->ActiveChildren--;
        }

        /// <summary>
        /// Poison a released chunk, but never the bytes holding the child's own control block:
        /// that is what lets a second disposal of a copied handle be reported instead of
        /// reading shredded memory.
        /// </summary>
        private static void PoisonExceptCore(ArenaChunk* chunk, ArenaCore* core)
        {
#if ARENA_CHECKS
            nuint start = (nuint)chunk->Base;
            nuint end = start + chunk->Used;

            if ((nuint)core >= start && (nuint)core < start + chunk->Size)
            {
                nuint afterCore = (nuint)core + (nuint)sizeof(ArenaCore);
                if (afterCore < end)
                    MemOps.Set((byte*)afterCore, 0xDD, end - afterCore);

                return;
            }

            if (chunk->Used != 0)
                MemOps.Set(chunk->Base, 0xDD, chunk->Used);
#endif
        }

        // ---- marks / rewind ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ArenaMark Mark(ArenaCore* core)
            => new ArenaMark(core->Head, core->Head->Used, core->TotalUsed, core->Id, core->Generation);

        public static void Rewind(ArenaCore* core, in ArenaMark mark)
        {
            Check.MarkBelongsTo(core, mark);

            while (core->Head != mark.Chunk)
            {
                ArenaChunk* dead = core->Head;
                core->Head = dead->Prev;
                core->ChunkCount--;

                PushSpare(core, dead);
            }

            if (mark.Chunk->Used > mark.Used)
                Check.Poison((byte*)((nuint)mark.Chunk->Base + mark.Used), mark.Chunk->Used - mark.Used);

            mark.Chunk->Used = mark.Used;
            core->TotalUsed = mark.Total;

            // Invalidates every slice/ref handed out since the mark.
            core->Generation++;
        }

        // ---- rentals ----

        public static uint EnterChild(ArenaCore* core)
        {
            core->ActiveDepth++;
            return core->ActiveDepth;
        }

        public static void ExitChild(ArenaCore* core, uint depth, in ArenaMark mark)
        {
            Check.Depth(core, depth);
            Rewind(core, mark);
            core->ActiveDepth = depth - 1;
        }

        // ---- stats ----

        public static ArenaStats Stats(ArenaCore* core)
            => new ArenaStats(
                core->TotalUsed, core->HighWater, core->ChunkCount, core->SpareCount,
                core->ReservationCount, core->BytesReserved, core->Generation, core->ActiveDepth,
                core->ActiveChildren);

        // ---- typed helpers (shared by all three handle types) ----

        public static nuint ByteCount<T>(int count)
        {
            if (count < 0)
                Fail.Msg("arena: negative element count");

            nuint elementSize = (nuint)(uint)TypeInfo.SizeOf<T>();
            nuint total = (nuint)(uint)count * elementSize;

            if (count != 0 && total / (nuint)(uint)count != elementSize)
                Fail.Msg("arena: allocation size overflow");

            return total;
        }

        public static Span<T> AllocSpan<T>(ArenaCore* core, int count, bool zero)
        {
            nuint bytes = ByteCount<T>(count);
            if (count == 0)
                return default;

            void* memory = AllocRaw(core, bytes, (nuint)(uint)TypeInfo.AlignOf<T>(), zero);
            return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(memory), count);
        }

        public static ArenaSlice<T> AllocSlice<T>(ArenaCore* core, int count, bool zero)
        {
            nuint bytes = ByteCount<T>(count);
            void* memory = count == 0
                ? null
                : AllocRaw(core, bytes, (nuint)(uint)TypeInfo.AlignOf<T>(), zero);

            return new ArenaSlice<T>(core, memory, count, core->Generation);
        }

        public static ArenaRef<T> AllocRef<T>(ArenaCore* core, in T value)
        {
            void* memory = AllocRaw(core, (nuint)(uint)TypeInfo.SizeOf<T>(), (nuint)(uint)TypeInfo.AlignOf<T>(), false);
            Unsafe.AsRef<T>(memory) = value;
            return new ArenaRef<T>(core, memory, core->Generation);
        }
    }
}
