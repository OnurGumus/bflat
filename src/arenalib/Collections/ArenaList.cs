using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// A growable list in arena memory.
    ///
    /// Growth is where an arena differs from the heap. <c>List&lt;T&gt;</c> doubles and abandons,
    /// which on a heap is free (the GC collects the old buffer) and in an arena is not: every
    /// intermediate buffer stays resident until the scope is released. So this first tries to
    /// extend the existing block in place, which succeeds whenever the list is the arena's most
    /// recent allocation - the common "fill one list in a loop" case - and only copies when
    /// something else has been allocated in between.
    ///
    /// If you know the size, say so: <c>WithCapacity</c> makes growth a non-issue. If you do not
    /// and cannot tolerate the abandoned buffers, use <see cref="ChunkedArenaList{T}"/>.
    /// </summary>
    public unsafe struct ArenaList<T>
    {
        private ArenaCore* _arena;
        private void* _items;
        private int _count;
        private int _capacity;
        private uint _generation;

        public static ArenaList<T> Create(ArenaScope scope) => Create(scope, 0);

        public static ArenaList<T> Create(ArenaScope scope, int capacity)
        {
            ArenaList<T> list = default;
            list._arena = scope.Core;
            list._generation = scope.Core->Generation;

            if (capacity > 0)
                list.Reserve(capacity);

            return list;
        }

        /// <summary>Alias for Create(scope, capacity) that reads better at call sites.</summary>
        public static ArenaList<T> WithCapacity(ArenaScope scope, int capacity) => Create(scope, capacity);

        public int Count => _count;

        public int Capacity => _capacity;

        public bool IsEmpty => _count == 0;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Check.Bounds(index, _count);
                Check.Value(_arena, _generation);
                return ref Unsafe.Add(ref Unsafe.AsRef<T>(_items), index);
            }
        }

        public void Add(in T item)
        {
            if (_count == _capacity)
                Grow(_count + 1);

            Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count) = item;
            _count++;
        }

        /// <summary>Append without growing. False if there is no spare capacity.</summary>
        public bool TryAdd(in T item)
        {
            if (_count == _capacity)
                return false;

            Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count) = item;
            _count++;
            return true;
        }

        public void AddRange(ReadOnlySpan<T> items)
        {
            if (items.Length == 0)
                return;

            Reserve(_count + items.Length);

            // Write straight past _count into reserved capacity - AsSpan validates against
            // Count, which has not been advanced yet.
            nuint elementSize = (nuint)(uint)TypeInfo.SizeOf<T>();
            void* target = (void*)((nuint)_items + (nuint)(uint)_count * elementSize);
            void* source = Unsafe.AsPointer(ref MemoryMarshal.GetReference(items));
            MemOps.Copy(target, source, (nuint)(uint)items.Length * elementSize);

            _count += items.Length;
        }

        /// <summary>Append a default-valued element and hand back a reference to fill in place.</summary>
        public ref T AddUninitialized()
        {
            if (_count == _capacity)
                Grow(_count + 1);

            _count++;
            return ref Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count - 1);
        }

        public T RemoveLast()
        {
            Check.Bounds(_count - 1, _count);
            _count--;
            return Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count);
        }

        /// <summary>O(1) removal that does not preserve order.</summary>
        public void RemoveAtSwapBack(int index)
        {
            Check.Bounds(index, _count);
            _count--;

            if (index != _count)
                Unsafe.Add(ref Unsafe.AsRef<T>(_items), index) = Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count);
        }

        /// <summary>Order-preserving removal.</summary>
        public void RemoveAt(int index)
        {
            Check.Bounds(index, _count);

            int trailing = _count - index - 1;
            if (trailing > 0)
            {
                nuint elementSize = (nuint)(uint)TypeInfo.SizeOf<T>();
                void* target = (void*)((nuint)_items + (nuint)(uint)index * elementSize);
                void* source = (void*)((nuint)target + elementSize);
                MemOps.Move(target, source, (nuint)(uint)trailing * elementSize);
            }

            _count--;
        }

        public void Clear() => _count = 0;

        public void Reserve(int capacity)
        {
            if (capacity > _capacity)
                Grow(capacity);
        }

        public Span<T> AsSpan()
        {
            Check.Value(_arena, _generation);
            return _count == 0 ? default : MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(_items), _count);
        }

        public Span<T> AsSpan(int start, int length)
        {
            Check.Range(start, length, _count);
            Check.Value(_arena, _generation);

            if (length == 0)
                return default;

            void* data = (void*)((nuint)_items + (nuint)(uint)start * (nuint)(uint)TypeInfo.SizeOf<T>());
            return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(data), length);
        }

        public ArenaSlice<T> AsSlice()
        {
            Check.Value(_arena, _generation);
            return new ArenaSlice<T>(_arena, _count == 0 ? null : _items, _count, _generation);
        }

        /// <summary>Copy this list into another scope - safe across scopes now that children own their chunks.</summary>
        public ArenaList<T> CloneTo(ArenaScope destination)
        {
            ArenaList<T> copy = Create(destination, _count);
            if (_count != 0)
            {
                MemOps.Copy(copy._items, _items, (nuint)(uint)_count * (nuint)(uint)TypeInfo.SizeOf<T>());
                copy._count = _count;
            }

            return copy;
        }

        public int IndexOf(in T item, delegate*<in T, in T, bool> equals)
        {
            for (int i = 0; i < _count; i++)
            {
                if (equals(in item, in Unsafe.Add(ref Unsafe.AsRef<T>(_items), i)))
                    return i;
            }

            return -1;
        }

        public void Sort(delegate*<in T, in T, int> compare) => Sorting.Sort(AsSpan(), compare);

        private void Grow(int required)
        {
            Check.Value(_arena, _generation);

            int capacity = _capacity == 0 ? 4 : _capacity;
            while (capacity < required)
            {
                if (capacity > int.MaxValue / 2)
                {
                    capacity = required;
                    break;
                }

                capacity *= 2;
            }

            nuint elementSize = (nuint)(uint)TypeInfo.SizeOf<T>();
            nuint oldBytes = (nuint)(uint)_capacity * elementSize;
            nuint newBytes = (nuint)(uint)capacity * elementSize;

            // Free and copy-free when this list is the newest thing in the arena.
            if (_items != null && ArenaImpl.TryExtendInPlace(_arena, _items, oldBytes, newBytes))
            {
                _capacity = capacity;
                return;
            }

            void* fresh = ArenaImpl.AllocRaw(_arena, newBytes, (nuint)(uint)TypeInfo.AlignOf<T>(), false);

            nuint usedBytes = (nuint)(uint)_count * elementSize;
            if (usedBytes != 0)
                MemOps.Copy(fresh, _items, usedBytes);

            MemOps.Zero((void*)((nuint)fresh + usedBytes), newBytes - usedBytes);

            _items = fresh;
            _capacity = capacity;
        }

        public Enumerator GetEnumerator() => new Enumerator(_items, _count);

        public ref struct Enumerator
        {
            private readonly void* _items;
            private readonly int _count;
            private int _index;

            internal Enumerator(void* items, int count)
            {
                _items = items;
                _count = count;
                _index = -1;
            }

            public ref T Current => ref Unsafe.Add(ref Unsafe.AsRef<T>(_items), _index);

            public bool MoveNext()
            {
                _index++;
                return _index < _count;
            }
        }
    }

    /// <summary>
    /// A list over a fixed block that never grows. Use it in hot loops and inside other arena
    /// structures where the bound is known: no growth path means no copying and no surprises.
    /// </summary>
    public unsafe struct FixedArenaList<T>
    {
        private void* _items;
        private int _count;
        private int _capacity;

        public static FixedArenaList<T> Create(ArenaScope scope, int capacity)
        {
            FixedArenaList<T> list = default;

            if (capacity > 0)
            {
                nuint bytes = ArenaImpl.ByteCount<T>(capacity);
                list._items = ArenaImpl.AllocRaw(scope.Core, bytes, (nuint)(uint)TypeInfo.AlignOf<T>(), true);
            }

            list._capacity = capacity;
            return list;
        }

        /// <summary>Wrap memory you already have, without allocating.</summary>
        public static FixedArenaList<T> FromSpan(Span<T> buffer)
        {
            FixedArenaList<T> list = default;
            list._items = buffer.Length == 0 ? null : Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer));
            list._capacity = buffer.Length;
            return list;
        }

        public int Count => _count;

        public int Capacity => _capacity;

        public bool IsFull => _count == _capacity;

        public bool IsEmpty => _count == 0;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Check.Bounds(index, _count);
                return ref Unsafe.Add(ref Unsafe.AsRef<T>(_items), index);
            }
        }

        public void Add(in T item)
        {
            if (!TryAdd(in item))
                Fail.Msg("FixedArenaList: capacity exceeded");
        }

        public bool TryAdd(in T item)
        {
            if (_count == _capacity)
                return false;

            Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count) = item;
            _count++;
            return true;
        }

        public T RemoveLast()
        {
            Check.Bounds(_count - 1, _count);
            _count--;
            return Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count);
        }

        public void RemoveAtSwapBack(int index)
        {
            Check.Bounds(index, _count);
            _count--;

            if (index != _count)
                Unsafe.Add(ref Unsafe.AsRef<T>(_items), index) = Unsafe.Add(ref Unsafe.AsRef<T>(_items), _count);
        }

        public void Clear() => _count = 0;

        public Span<T> AsSpan()
            => _count == 0 ? default : MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(_items), _count);

        public void Sort(delegate*<in T, in T, int> compare) => Sorting.Sort(AsSpan(), compare);

        public ArenaList<T>.Enumerator GetEnumerator() => new ArenaList<T>.Enumerator(_items, _count);
    }
}
