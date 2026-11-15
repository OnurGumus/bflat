using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// An open-addressed hash map in arena memory, keyed by function pointers rather than an
    /// IEqualityComparer (zerolib has no interfaces, and System.Object has no GetHashCode).
    ///
    /// Linear probing, power-of-two capacity, 0.75 load factor, and backward-shift deletion so
    /// there are no tombstones to accumulate.
    ///
    /// Growth rehashes into a fresh table and abandons the old one, which in an arena stays
    /// resident until the scope is released. That is the one unavoidable abandon in the whole
    /// library, so <b>pass a capacity if you have any idea of the size</b>.
    /// </summary>
    public unsafe struct ArenaMap<TKey, TValue>
    {
        private struct Entry
        {
            public ulong Hash;      // 0 means empty; a real hash of 0 is folded to 1
            public TKey Key;
            public TValue Value;
        }

        private ArenaCore* _arena;
        private void* _entries;
        private int _count;
        private int _capacity;
        private uint _generation;
        private delegate*<in TKey, ulong> _hash;
        private delegate*<in TKey, in TKey, bool> _equals;

        public static ArenaMap<TKey, TValue> Create(
            ArenaScope scope,
            int capacity,
            delegate*<in TKey, ulong> hash,
            delegate*<in TKey, in TKey, bool> equals)
        {
            if (hash == null || equals == null)
                Fail.Msg("ArenaMap: hash and equality functions are required");

            ArenaMap<TKey, TValue> map = default;
            map._arena = scope.Core;
            map._generation = scope.Core->Generation;
            map._hash = hash;
            map._equals = equals;

            int target = 8;
            while (target * 3 < capacity * 4)
                target <<= 1;

            map.Allocate(target);
            return map;
        }

        public int Count => _count;

        public int Capacity => _capacity;

        public bool IsEmpty => _count == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Entry EntryAt(int index) => ref Unsafe.Add(ref Unsafe.AsRef<Entry>(_entries), index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong HashOf(in TKey key)
        {
            ulong hash = _hash(in key);
            return hash == 0 ? 1 : hash;
        }

        public bool TryGetValue(in TKey key, out TValue value)
        {
            int index = FindEntry(in key);
            if (index < 0)
            {
                value = default;
                return false;
            }

            value = EntryAt(index).Value;
            return true;
        }

        public bool ContainsKey(in TKey key) => FindEntry(in key) >= 0;

        /// <summary>Reference to the stored value, for in-place update. Fails if absent.</summary>
        public ref TValue GetValueRef(in TKey key)
        {
            int index = FindEntry(in key);
            if (index < 0)
                Fail.Msg("ArenaMap: key not found");

            return ref EntryAt(index).Value;
        }

        /// <summary>
        /// Get a reference to the value for <paramref name="key"/>, inserting a default entry if
        /// it is missing. This is the counter-increment idiom: one probe instead of two.
        /// </summary>
        public ref TValue GetOrAddRef(in TKey key, out bool added)
        {
            Check.Value(_arena, _generation);

            if ((_count + 1) * 4 > _capacity * 3)
                Resize(_capacity * 2);

            ulong hash = HashOf(in key);
            int mask = _capacity - 1;
            int slot = (int)(hash & (ulong)(uint)mask);

            while (true)
            {
                ref Entry entry = ref EntryAt(slot);

                if (entry.Hash == 0)
                {
                    entry.Hash = hash;
                    entry.Key = key;
                    entry.Value = default;
                    _count++;
                    added = true;
                    return ref entry.Value;
                }

                if (entry.Hash == hash && _equals(in entry.Key, in key))
                {
                    added = false;
                    return ref entry.Value;
                }

                slot = (slot + 1) & mask;
            }
        }

        /// <summary>Insert or overwrite.</summary>
        public void Set(in TKey key, in TValue value)
        {
            ref TValue slot = ref GetOrAddRef(in key, out _);
            slot = value;
        }

        /// <summary>Insert only if absent. False if the key was already present.</summary>
        public bool TryAdd(in TKey key, in TValue value)
        {
            ref TValue slot = ref GetOrAddRef(in key, out bool added);
            if (!added)
                return false;

            slot = value;
            return true;
        }

        public bool Remove(in TKey key)
        {
            int index = FindEntry(in key);
            if (index < 0)
                return false;

            RemoveAt(index);
            return true;
        }

        public void Clear()
        {
            if (_capacity != 0)
                MemOps.Zero(_entries, (nuint)(uint)_capacity * (nuint)(uint)TypeInfo.SizeOf<Entry>());

            _count = 0;
        }

        private int FindEntry(in TKey key)
        {
            if (_count == 0)
                return -1;

            Check.Value(_arena, _generation);

            ulong hash = HashOf(in key);
            int mask = _capacity - 1;
            int slot = (int)(hash & (ulong)(uint)mask);

            while (true)
            {
                ref Entry entry = ref EntryAt(slot);

                if (entry.Hash == 0)
                    return -1;

                if (entry.Hash == hash && _equals(in entry.Key, in key))
                    return slot;

                slot = (slot + 1) & mask;
            }
        }

        /// <summary>
        /// Backward-shift deletion: pull following entries of the probe chain back into the
        /// hole instead of leaving a tombstone, so repeated add/remove cycles do not degrade.
        /// </summary>
        private void RemoveAt(int index)
        {
            int mask = _capacity - 1;
            int hole = index;

            while (true)
            {
                EntryAt(hole).Hash = 0;

                int probe = hole;
                while (true)
                {
                    probe = (probe + 1) & mask;

                    ref Entry candidate = ref EntryAt(probe);
                    if (candidate.Hash == 0)
                    {
                        _count--;
                        return;
                    }

                    int ideal = (int)(candidate.Hash & (ulong)(uint)mask);

                    // Is `ideal` cyclically within (hole, probe]? If so the entry is already
                    // where it belongs and must not be moved into the hole.
                    bool inRange = hole <= probe
                        ? (hole < ideal && ideal <= probe)
                        : (hole < ideal || ideal <= probe);

                    if (inRange)
                        continue;

                    EntryAt(hole) = candidate;
                    hole = probe;
                    break;
                }
            }
        }

        private void Allocate(int capacity)
        {
            nuint bytes = (nuint)(uint)capacity * (nuint)(uint)TypeInfo.SizeOf<Entry>();
            _entries = ArenaImpl.AllocRaw(_arena, bytes, (nuint)(uint)TypeInfo.AlignOf<Entry>(), true);
            _capacity = capacity;
        }

        private void Resize(int capacity)
        {
            void* oldEntries = _entries;
            int oldCapacity = _capacity;

            Allocate(capacity);
            _count = 0;

            for (int i = 0; i < oldCapacity; i++)
            {
                ref Entry entry = ref Unsafe.Add(ref Unsafe.AsRef<Entry>(oldEntries), i);
                if (entry.Hash == 0)
                    continue;

                Insert(entry.Hash, in entry.Key, in entry.Value);
            }
        }

        private void Insert(ulong hash, in TKey key, in TValue value)
        {
            int mask = _capacity - 1;
            int slot = (int)(hash & (ulong)(uint)mask);

            while (EntryAt(slot).Hash != 0)
                slot = (slot + 1) & mask;

            ref Entry target = ref EntryAt(slot);
            target.Hash = hash;
            target.Key = key;
            target.Value = value;
            _count++;
        }

        public Enumerator GetEnumerator() => new Enumerator(_entries, _capacity);

        public ref struct Enumerator
        {
            private readonly void* _entries;
            private readonly int _capacity;
            private int _index;

            internal Enumerator(void* entries, int capacity)
            {
                _entries = entries;
                _capacity = capacity;
                _index = -1;
            }

            public ref TKey CurrentKey => ref Unsafe.Add(ref Unsafe.AsRef<Entry>(_entries), _index).Key;

            public ref TValue CurrentValue => ref Unsafe.Add(ref Unsafe.AsRef<Entry>(_entries), _index).Value;

            /// <summary>The enumerator is its own Current, so `foreach` yields it and you read both sides.</summary>
            public Enumerator Current => this;

            public Enumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                while (++_index < _capacity)
                {
                    if (Unsafe.Add(ref Unsafe.AsRef<Entry>(_entries), _index).Hash != 0)
                        return true;
                }

                return false;
            }
        }
    }

    /// <summary>Zero-payload value type, so a set is a map with nothing on the side.</summary>
    public struct Unit
    {
    }

    /// <summary>A hash set, implemented as a map with no values.</summary>
    public unsafe struct ArenaSet<T>
    {
        private ArenaMap<T, Unit> _map;

        public static ArenaSet<T> Create(
            ArenaScope scope, int capacity, delegate*<in T, ulong> hash, delegate*<in T, in T, bool> equals)
        {
            ArenaSet<T> set = default;
            set._map = ArenaMap<T, Unit>.Create(scope, capacity, hash, equals);
            return set;
        }

        public int Count => _map.Count;

        public bool IsEmpty => _map.IsEmpty;

        /// <summary>True if the item was newly added.</summary>
        public bool Add(in T item)
        {
            Unit nothing = default;
            return _map.TryAdd(in item, in nothing);
        }

        public bool Contains(in T item) => _map.ContainsKey(in item);

        public bool Remove(in T item) => _map.Remove(in item);

        public void Clear() => _map.Clear();

        public ArenaMap<T, Unit>.Enumerator GetEnumerator() => _map.GetEnumerator();
    }
}
