using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// A stable-address slab pool. Slots live in slabs that are never moved or reallocated, so
    /// unlike every other growable container here, a pointer into a pool stays valid for the
    /// life of the arena. Freed slots go on an intrusive free list.
    ///
    /// This is what backs <see cref="ArenaHandle{T}"/>: the answer to "raw pointers defeat
    /// escape analysis" is to hand out an index and a stamp instead of an address.
    /// </summary>
    public unsafe struct ArenaPool<T> where T : unmanaged
    {
        private struct Slot
        {
            public uint Stamp;      // odd = live, even = free; bumped on every release
            public int NextFree;    // -1 terminates
            public T Value;
        }

        private ArenaCore* _arena;
        private ArenaList<IntPtr> _slabs;
        private int _slabShift;
        private int _slabMask;
        private int _capacity;
        private int _count;
        private int _firstFree;
        private uint _generation;

        public static ArenaPool<T> Create(ArenaScope scope) => Create(scope, 64);

        public static ArenaPool<T> Create(ArenaScope scope, int slotsPerSlab)
        {
            int shift = 0;
            int size = 1;
            while (size < slotsPerSlab)
            {
                size <<= 1;
                shift++;
            }

            ArenaPool<T> pool = default;
            pool._arena = scope.Core;
            pool._generation = scope.Core->Generation;
            pool._slabs = ArenaList<IntPtr>.Create(scope, 4);
            pool._slabShift = shift;
            pool._slabMask = size - 1;
            pool._firstFree = -1;
            return pool;
        }

        public int Count => _count;

        public int Capacity => _capacity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Slot SlotAt(int index)
        {
            void* slab = (void*)_slabs[index >> _slabShift];
            return ref Unsafe.Add(ref Unsafe.AsRef<Slot>(slab), index & _slabMask);
        }

        public ArenaHandle<T> Add(in T value)
        {
            int index;

            if (_firstFree >= 0)
            {
                index = _firstFree;
                _firstFree = SlotAt(index).NextFree;
            }
            else
            {
                if (_capacity == _slabs.Count << _slabShift)
                    AddSlab();

                index = _capacity;
                _capacity++;
            }

            ref Slot slot = ref SlotAt(index);
            slot.Stamp |= 1;            // make it odd: live
            slot.NextFree = -1;
            slot.Value = value;
            _count++;

            return new ArenaHandle<T>(index, slot.Stamp);
        }

        public bool IsValid(ArenaHandle<T> handle)
        {
            if (handle.Index < 0 || handle.Index >= _capacity)
                return false;

            ref Slot slot = ref SlotAt(handle.Index);
            return slot.Stamp == handle.Stamp && (slot.Stamp & 1) != 0;
        }

        /// <summary>Resolve a handle. Aborts if it is stale.</summary>
        public ref T this[ArenaHandle<T> handle]
        {
            get
            {
                if (!IsValid(handle))
                    Fail.Msg("ArenaPool: stale or invalid handle");

                return ref SlotAt(handle.Index).Value;
            }
        }

        public bool TryGet(ArenaHandle<T> handle, out T value)
        {
            if (!IsValid(handle))
            {
                value = default;
                return false;
            }

            value = SlotAt(handle.Index).Value;
            return true;
        }

        /// <summary>Return a slot to the free list, invalidating every handle to it.</summary>
        public bool Remove(ArenaHandle<T> handle)
        {
            if (!IsValid(handle))
                return false;

            ref Slot slot = ref SlotAt(handle.Index);
            slot.Stamp++;               // now even: free, and no old handle can match again
            slot.Value = default;
            slot.NextFree = _firstFree;
            _firstFree = handle.Index;
            _count--;
            return true;
        }

        private void AddSlab()
        {
            nuint bytes = (nuint)(uint)(_slabMask + 1) * (nuint)(uint)TypeInfo.SizeOf<Slot>();
            void* slab = ArenaImpl.AllocRaw(_arena, bytes, (nuint)(uint)TypeInfo.AlignOf<Slot>(), true);
            _slabs.Add((IntPtr)slab);
        }
    }

    /// <summary>
    /// A relocation-stable reference into an <see cref="ArenaPool{T}"/>: an index plus a stamp,
    /// not an address. Storing one is safe across pool growth, and resolving a handle to a slot
    /// that has since been freed and reused fails instead of silently aliasing.
    /// </summary>
    public readonly struct ArenaHandle<T> where T : unmanaged
    {
        internal readonly int Index;
        internal readonly uint Stamp;

        internal ArenaHandle(int index, uint stamp)
        {
            Index = index;
            Stamp = stamp;
        }

        public bool IsDefault => Stamp == 0;

        public static ArenaHandle<T> None => default;

        public bool Equals(ArenaHandle<T> other) => Index == other.Index && Stamp == other.Stamp;

        public static bool operator ==(ArenaHandle<T> left, ArenaHandle<T> right) => left.Equals(right);

        public static bool operator !=(ArenaHandle<T> left, ArenaHandle<T> right) => !left.Equals(right);
    }
}
