using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// Arena-owned memory. Unlike <see cref="Span{T}"/> this is a plain struct, so it can be
    /// a field of another arena value - which is what makes ArenaList&lt;ArenaSlice&lt;byte&gt;&gt;
    /// possible. It carries its arena and birth generation so that use-after-rewind is a
    /// deterministic abort under ARENA_CHECKS instead of silent corruption.
    /// </summary>
    public readonly unsafe struct ArenaSlice<T>
    {
        internal readonly ArenaCore* _arena;
        internal readonly void* _data;
        internal readonly int _length;
        internal readonly uint _generation;

        internal ArenaSlice(ArenaCore* arena, void* data, int length, uint generation)
        {
            _arena = arena;
            _data = data;
            _length = length;
            _generation = generation;
        }

        public int Length => _length;

        public bool IsEmpty => _length == 0;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Check.Bounds(index, _length);
                Check.Value(_arena, _generation);
                return ref Unsafe.Add(ref Unsafe.AsRef<T>(_data), index);
            }
        }

        /// <summary>Borrow the contents. The result must not outlive the arena scope.</summary>
        public Span<T> AsSpan()
        {
            Check.Value(_arena, _generation);
            if (_length == 0)
                return default;

            return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(_data), _length);
        }

        public ArenaSlice<T> Slice(int start)
            => Slice(start, _length - start);

        public ArenaSlice<T> Slice(int start, int length)
        {
            Check.Range(start, length, _length);
            Check.Value(_arena, _generation);

            if (length == 0)
                return new ArenaSlice<T>(_arena, null, 0, _generation);

            void* data = (void*)((nuint)_data + (nuint)(uint)start * (nuint)(uint)TypeInfo.SizeOf<T>());
            return new ArenaSlice<T>(_arena, data, length, _generation);
        }

        public void CopyTo(ArenaSlice<T> destination)
        {
            if (_length > destination._length)
                Fail.Msg("arena: CopyTo destination is too small");

            Check.Value(_arena, _generation);
            Check.Value(destination._arena, destination._generation);

            if (_length != 0)
                MemOps.Move(destination._data, _data, (nuint)(uint)_length * (nuint)(uint)TypeInfo.SizeOf<T>());
        }
    }

    /// <summary>A single arena-allocated value, with the same generation guard as ArenaSlice.</summary>
    public readonly unsafe struct ArenaRef<T>
    {
        internal readonly ArenaCore* _arena;
        internal readonly void* _pointer;
        internal readonly uint _generation;

        internal ArenaRef(ArenaCore* arena, void* pointer, uint generation)
        {
            _arena = arena;
            _pointer = pointer;
            _generation = generation;
        }

        public bool IsNull => _pointer == null;

        public ref T Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Check.Value(_arena, _generation);
                return ref Unsafe.AsRef<T>(_pointer);
            }
        }
    }
}
