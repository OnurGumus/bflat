using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// A list that never reallocates: elements live in fixed-size chunks reached through a
    /// directory, so indexing stays O(1) and no buffer is ever abandoned.
    ///
    /// The trade against <see cref="ArenaList{T}"/> is that there is no contiguous backing
    /// array, so no <c>AsSpan()</c>. Use this when the final size is unknown and the wasted
    /// arena bytes from doubling would matter.
    /// </summary>
    public unsafe struct ChunkedArenaList<T>
    {
        private ArenaCore* _arena;
        private ArenaList<IntPtr> _directory;
        private int _shift;
        private int _mask;
        private int _count;
        private uint _generation;

        public static ChunkedArenaList<T> Create(ArenaScope scope) => Create(scope, 256);

        public static ChunkedArenaList<T> Create(ArenaScope scope, int elementsPerChunk)
        {
            int shift = 0;
            int size = 1;
            while (size < elementsPerChunk)
            {
                size <<= 1;
                shift++;
            }

            ChunkedArenaList<T> list = default;
            list._arena = scope.Core;
            list._generation = scope.Core->Generation;
            list._directory = ArenaList<IntPtr>.Create(scope, 8);
            list._shift = shift;
            list._mask = size - 1;
            return list;
        }

        public int Count => _count;

        public bool IsEmpty => _count == 0;

        public int ElementsPerChunk => _mask + 1;

        public int ChunkCount => _directory.Count;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Check.Bounds(index, _count);
                void* chunk = (void*)_directory[index >> _shift];
                return ref Unsafe.Add(ref Unsafe.AsRef<T>(chunk), index & _mask);
            }
        }

        public void Add(in T item)
        {
            int chunkIndex = _count >> _shift;

            if (chunkIndex == _directory.Count)
            {
                nuint bytes = ArenaImpl.ByteCount<T>(_mask + 1);
                void* chunk = ArenaImpl.AllocRaw(_arena, bytes, (nuint)(uint)TypeInfo.AlignOf<T>(), true);
                _directory.Add((IntPtr)chunk);
            }

            void* target = (void*)_directory[chunkIndex];
            Unsafe.Add(ref Unsafe.AsRef<T>(target), _count & _mask) = item;
            _count++;
        }

        public void Clear() => _count = 0;

        /// <summary>Flatten into contiguous memory in <paramref name="destination"/>.</summary>
        public ArenaSlice<T> ToSlice(ArenaScope destination)
        {
            ArenaSlice<T> result = ArenaImpl.AllocSlice<T>(destination.Core, _count, false);

            for (int i = 0; i < _count; i++)
                result[i] = this[i];

            return result;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public ref struct Enumerator
        {
            private ChunkedArenaList<T> _list;
            private int _index;

            internal Enumerator(ChunkedArenaList<T> list)
            {
                _list = list;
                _index = -1;
            }

            public ref T Current => ref _list[_index];

            public bool MoveNext()
            {
                _index++;
                return _index < _list._count;
            }
        }
    }

    /// <summary>A FIFO ring buffer in arena memory.</summary>
    public unsafe struct ArenaQueue<T>
    {
        private ArenaCore* _arena;
        private void* _items;
        private int _head;
        private int _count;
        private int _capacity;
        private uint _generation;

        public static ArenaQueue<T> Create(ArenaScope scope, int capacity)
        {
            ArenaQueue<T> queue = default;
            queue._arena = scope.Core;
            queue._generation = scope.Core->Generation;

            int target = 4;
            while (target < capacity)
                target <<= 1;

            queue._items = ArenaImpl.AllocRaw(
                scope.Core, ArenaImpl.ByteCount<T>(target), (nuint)(uint)TypeInfo.AlignOf<T>(), true);
            queue._capacity = target;
            return queue;
        }

        public int Count => _count;

        public bool IsEmpty => _count == 0;

        public int Capacity => _capacity;

        public void Enqueue(in T item)
        {
            if (_count == _capacity)
                Grow();

            int tail = (_head + _count) & (_capacity - 1);
            Unsafe.Add(ref Unsafe.AsRef<T>(_items), tail) = item;
            _count++;
        }

        public bool TryDequeue(out T item)
        {
            if (_count == 0)
            {
                item = default;
                return false;
            }

            item = Unsafe.Add(ref Unsafe.AsRef<T>(_items), _head);
            _head = (_head + 1) & (_capacity - 1);
            _count--;
            return true;
        }

        public ref T Peek()
        {
            if (_count == 0)
                Fail.Msg("ArenaQueue: empty");

            return ref Unsafe.Add(ref Unsafe.AsRef<T>(_items), _head);
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        private void Grow()
        {
            Check.Value(_arena, _generation);

            int capacity = _capacity * 2;
            void* fresh = ArenaImpl.AllocRaw(
                _arena, ArenaImpl.ByteCount<T>(capacity), (nuint)(uint)TypeInfo.AlignOf<T>(), true);

            // Linearize while copying: the ring wraps, the new buffer starts at 0.
            for (int i = 0; i < _count; i++)
            {
                Unsafe.Add(ref Unsafe.AsRef<T>(fresh), i) =
                    Unsafe.Add(ref Unsafe.AsRef<T>(_items), (_head + i) & (_capacity - 1));
            }

            _items = fresh;
            _capacity = capacity;
            _head = 0;
        }
    }

    /// <summary>A LIFO stack, a thin facade over <see cref="ArenaList{T}"/>.</summary>
    public unsafe struct ArenaStack<T>
    {
        private ArenaList<T> _items;

        public static ArenaStack<T> Create(ArenaScope scope) => Create(scope, 0);

        public static ArenaStack<T> Create(ArenaScope scope, int capacity)
        {
            ArenaStack<T> stack = default;
            stack._items = ArenaList<T>.Create(scope, capacity);
            return stack;
        }

        public int Count => _items.Count;

        public bool IsEmpty => _items.IsEmpty;

        public void Push(in T item) => _items.Add(in item);

        public bool TryPop(out T item)
        {
            if (_items.Count == 0)
            {
                item = default;
                return false;
            }

            item = _items.RemoveLast();
            return true;
        }

        public ref T Peek()
        {
            if (_items.Count == 0)
                Fail.Msg("ArenaStack: empty");

            return ref _items[_items.Count - 1];
        }

        public void Clear() => _items.Clear();

        public Span<T> AsSpan() => _items.AsSpan();
    }

    /// <summary>A dense bit set backed by 64-bit words.</summary>
    public unsafe struct ArenaBitSet
    {
        private ulong* _words;
        private int _bitLength;
        private int _wordCount;

        public static ArenaBitSet Create(ArenaScope scope, int bitLength)
        {
            if (bitLength < 0)
                Fail.Msg("ArenaBitSet: negative length");

            int words = (bitLength + 63) >> 6;

            ArenaBitSet set = default;
            set._words = words == 0
                ? null
                : (ulong*)ArenaImpl.AllocRaw(scope.Core, (nuint)(uint)words * 8, 8, true);
            set._bitLength = bitLength;
            set._wordCount = words;
            return set;
        }

        public int Length => _bitLength;

        public bool this[int index]
        {
            get
            {
                Check.Bounds(index, _bitLength);
                return (_words[index >> 6] & (1UL << (index & 63))) != 0;
            }
            set
            {
                Check.Bounds(index, _bitLength);
                if (value)
                    _words[index >> 6] |= 1UL << (index & 63);
                else
                    _words[index >> 6] &= ~(1UL << (index & 63));
            }
        }

        public void SetAll(bool value)
        {
            if (_wordCount == 0)
                return;

            MemOps.Set(_words, value ? (byte)0xFF : (byte)0, (nuint)(uint)_wordCount * 8);
            TrimTail();
        }

        public void Clear() => SetAll(false);

        /// <summary>Number of set bits. SWAR popcount - zerolib exposes no Popcnt intrinsic.</summary>
        public int PopCount()
        {
            int total = 0;
            for (int i = 0; i < _wordCount; i++)
                total += PopCount64(_words[i]);

            return total;
        }

        /// <summary>Index of the first set bit at or after <paramref name="from"/>, or -1.</summary>
        public int FindNextSet(int from)
        {
            if (from < 0)
                from = 0;

            for (int index = from; index < _bitLength; )
            {
                int word = index >> 6;
                ulong bits = _words[word] >> (index & 63);

                if (bits != 0)
                {
                    while ((bits & 1) == 0)
                    {
                        bits >>= 1;
                        index++;
                    }

                    return index < _bitLength ? index : -1;
                }

                index = (word + 1) << 6;
            }

            return -1;
        }

        public int FindFirstSet() => FindNextSet(0);

        /// <summary>Zero the bits past <see cref="Length"/> in the final word, so counts stay honest.</summary>
        private void TrimTail()
        {
            int remainder = _bitLength & 63;
            if (remainder != 0 && _wordCount != 0)
                _words[_wordCount - 1] &= (1UL << remainder) - 1;
        }

        private static int PopCount64(ulong value)
        {
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
        }
    }
}
