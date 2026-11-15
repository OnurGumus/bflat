using System;

namespace Arenas
{
    internal enum ChunkKind
    {
        Normal = 0,
        /// <summary>Oversized one-shot chunk. Released on rewind instead of being kept as a spare.</summary>
        Huge = 1,
    }

    /// <summary>
    /// Header for one block of memory obtained from the <see cref="PageSource"/>. The header
    /// lives at offset 0 of the block and the usable region follows it.
    /// </summary>
    internal unsafe struct ArenaChunk
    {
        public ArenaChunk* Prev;
        public byte* Base;
        public nuint Size;
        public nuint Used;

        /// <summary>Sum of Used across every chunk below this one, so the global offset is O(1).</summary>
        public nuint TotalBefore;

        public void* SourceBase;
        public nuint SourceSize;
        public ChunkKind Kind;
    }

    /// <summary>
    /// The arena's state. Bump-allocated out of the arena's own first chunk, so an empty
    /// arena costs exactly one page-source reservation and zero managed allocations.
    /// It is never a class: zerolib has no free, so every class instance would leak.
    /// </summary>
    internal unsafe struct ArenaCore
    {
        public const uint LiveMagic = 0x41524E41;   // 'ARNA'
        public const uint DeadMagic = 0x44454144;   // 'DEAD'

        public uint Magic;
        public uint Id;

        /// <summary>Bumped by every rewind. Stamped into slices/refs to catch use-after-rewind.</summary>
        public uint Generation;

        /// <summary>
        /// Depth of the innermost live <see cref="ArenaCheckpoint"/>. Checkpoints are the only
        /// mark-backed scope left, and they carry the hard rule that nothing else may allocate
        /// into this arena while one is open.
        /// </summary>
        public uint ActiveDepth;

        /// <summary>
        /// Live child scopes (rentals and scratches) borrowing from this core. Disposing an
        /// arena with live children is a bug; the checked build says so.
        /// </summary>
        public int ActiveChildren;

        /// <summary>
        /// Set on a scratch core. Its chunks are borrowed from, and returned to, this
        /// parent's spare list, so scratch is independent memory that is still free to
        /// acquire in steady state.
        /// </summary>
        public ArenaCore* Parent;

        public ArenaChunk* Head;
        public ArenaChunk* Spare;

        public PageSource Source;

        public nuint NextChunkSize;
        public nuint MaxChunkSize;

        /// <summary>
        /// Bytes consumed in the first chunk by the ArenaCore itself. The arena is
        /// self-hosting, so Reset must not rewind past this or the next allocation would
        /// hand out the arena's own state.
        /// </summary>
        public nuint BaseUsed;

        public nuint TotalUsed;
        public nuint HighWater;

        /// <summary>Outstanding RcArenaLease references. Not atomic - see Leases.cs.</summary>
        public int RefCount;

        public nuint ChunkCount;
        public nuint SpareCount;
        public nuint ReservationCount;
        public nuint BytesReserved;
    }

    /// <summary>A position in an arena that can be rewound to.</summary>
    public readonly unsafe struct ArenaMark
    {
        internal readonly ArenaChunk* Chunk;
        internal readonly nuint Used;
        internal readonly nuint Total;
        internal readonly uint ArenaId;
        internal readonly uint Generation;

        internal ArenaMark(ArenaChunk* chunk, nuint used, nuint total, uint arenaId, uint generation)
        {
            Chunk = chunk;
            Used = used;
            Total = total;
            ArenaId = arenaId;
            Generation = generation;
        }

        /// <summary>Bytes allocated in the arena at the moment this mark was taken.</summary>
        public nuint BytesUsed => Total;
    }

    /// <summary>A snapshot of an arena's allocation counters. Always available, not debug-only.</summary>
    public readonly struct ArenaStats
    {
        public readonly nuint BytesUsed;
        public readonly nuint HighWaterMark;
        public readonly nuint ChunkCount;
        public readonly nuint SpareChunkCount;
        public readonly nuint ReservationCount;
        public readonly nuint BytesReserved;
        public readonly uint Generation;
        public readonly uint ActiveDepth;
        public readonly int ActiveChildren;

        internal ArenaStats(
            nuint bytesUsed, nuint highWater, nuint chunkCount, nuint spareCount,
            nuint reservationCount, nuint bytesReserved, uint generation, uint activeDepth,
            int activeChildren)
        {
            BytesUsed = bytesUsed;
            HighWaterMark = highWater;
            ChunkCount = chunkCount;
            SpareChunkCount = spareCount;
            ReservationCount = reservationCount;
            BytesReserved = bytesReserved;
            Generation = generation;
            ActiveDepth = activeDepth;
            ActiveChildren = activeChildren;
        }
    }
}
