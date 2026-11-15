using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// A borrowed window onto UTF-8 bytes. This is a ref struct on purpose: the compiler then
    /// mechanically forbids storing it in a field, an array, or a generic, which is exactly
    /// the "borrowed, do not keep" guarantee. To retain one, CloneTo it into an arena and get
    /// back an <see cref="ArenaString"/>.
    /// </summary>
    public readonly unsafe ref struct Utf8View
    {
        private readonly byte* _data;
        private readonly int _length;

        internal Utf8View(byte* data, int length)
        {
            _data = data;
            _length = length;
        }

        public int Length => _length;

        public bool IsEmpty => _length == 0;

        public byte* Data => _data;

        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Check.Bounds(index, _length);
                return _data[index];
            }
        }

        // ---- construction ----

        public static Utf8View FromBytes(byte* data, int length) => new Utf8View(data, length);

        /// <summary>Wrap a UTF-8 literal (<c>"..."u8</c>) with no copy and no allocation.</summary>
        public static Utf8View FromLiteral(ReadOnlySpan<byte> utf8)
        {
            if (utf8.Length == 0)
                return default;

            return new Utf8View((byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(utf8)), utf8.Length);
        }

        public static Utf8View FromSpan(ReadOnlySpan<byte> utf8) => FromLiteral(utf8);

        /// <summary>Wrap a NUL-terminated C string in place.</summary>
        public static Utf8View FromCString(byte* nulTerminated)
        {
            if (nulTerminated == null)
                return default;

            int length = 0;
            while (nulTerminated[length] != 0)
                length++;

            return new Utf8View(nulTerminated, length);
        }

        public ReadOnlySpan<byte> AsBytes()
            => _length == 0 ? default : MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef<byte>(_data), _length);

        // ---- slicing ----

        public Utf8View Slice(int start)
        {
            Check.Range(start, _length - start, _length);
            return new Utf8View(_data + start, _length - start);
        }

        public Utf8View Slice(int start, int length)
        {
            Check.Range(start, length, _length);
            return new Utf8View(_data + start, length);
        }

        // ---- comparison ----

        public bool Equals(Utf8View other)
        {
            if (_length != other._length)
                return false;
            if (_length == 0)
                return true;

            return MemOps.Equal(_data, other._data, (nuint)(uint)_length);
        }

        /// <summary>Ordinal byte comparison. UTF-8 orders identically to UTF-32 by code point.</summary>
        public int CompareOrdinal(Utf8View other)
        {
            int shared = _length < other._length ? _length : other._length;
            if (shared != 0)
            {
                int difference = MemOps.Compare(_data, other._data, (nuint)(uint)shared);
                if (difference != 0)
                    return difference;
            }

            return _length - other._length;
        }

        public bool EqualsAsciiIgnoreCase(Utf8View other)
        {
            if (_length != other._length)
                return false;

            for (int i = 0; i < _length; i++)
            {
                if (Utf8.ToAsciiLower(_data[i]) != Utf8.ToAsciiLower(other._data[i]))
                    return false;
            }

            return true;
        }

        public static bool operator ==(Utf8View left, Utf8View right) => left.Equals(right);

        public static bool operator !=(Utf8View left, Utf8View right) => !left.Equals(right);

        // ---- searching ----

        public bool StartsWith(Utf8View prefix)
        {
            if (prefix._length > _length)
                return false;
            if (prefix._length == 0)
                return true;

            return MemOps.Equal(_data, prefix._data, (nuint)(uint)prefix._length);
        }

        public bool EndsWith(Utf8View suffix)
        {
            if (suffix._length > _length)
                return false;
            if (suffix._length == 0)
                return true;

            return MemOps.Equal(_data + (_length - suffix._length), suffix._data, (nuint)(uint)suffix._length);
        }

        public bool StartsWith(byte value) => _length != 0 && _data[0] == value;

        public bool EndsWith(byte value) => _length != 0 && _data[_length - 1] == value;

        public int IndexOf(byte value)
        {
            for (int i = 0; i < _length; i++)
            {
                if (_data[i] == value)
                    return i;
            }

            return -1;
        }

        public int LastIndexOf(byte value)
        {
            for (int i = _length - 1; i >= 0; i--)
            {
                if (_data[i] == value)
                    return i;
            }

            return -1;
        }

        public int IndexOf(Utf8View needle)
        {
            if (needle._length == 0)
                return 0;
            if (needle._length > _length)
                return -1;

            int last = _length - needle._length;
            for (int i = 0; i <= last; i++)
            {
                if (_data[i] == needle._data[0]
                    && MemOps.Equal(_data + i, needle._data, (nuint)(uint)needle._length))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool Contains(byte value) => IndexOf(value) >= 0;

        public bool Contains(Utf8View needle) => IndexOf(needle) >= 0;

        // ---- trimming ----

        public Utf8View TrimStart()
        {
            int start = 0;
            while (start < _length && Utf8.IsAsciiWhiteSpace(_data[start]))
                start++;

            return new Utf8View(_data + start, _length - start);
        }

        public Utf8View TrimEnd()
        {
            int end = _length;
            while (end > 0 && Utf8.IsAsciiWhiteSpace(_data[end - 1]))
                end--;

            return new Utf8View(_data, end);
        }

        public Utf8View Trim() => TrimStart().TrimEnd();

        public Utf8View TrimStart(byte value)
        {
            int start = 0;
            while (start < _length && _data[start] == value)
                start++;

            return new Utf8View(_data + start, _length - start);
        }

        public Utf8View TrimEnd(byte value)
        {
            int end = _length;
            while (end > 0 && _data[end - 1] == value)
                end--;

            return new Utf8View(_data, end);
        }

        // ---- splitting ----

        public Utf8Splitter Split(byte separator) => new Utf8Splitter(this, separator);

        public Utf8LineSplitter Lines() => new Utf8LineSplitter(this);

        public Utf8ScalarEnumerator Chars() => new Utf8ScalarEnumerator(this);

        /// <summary>Split once at the first occurrence of <paramref name="separator"/>.</summary>
        public bool TrySplitOnce(byte separator, out Utf8View head, out Utf8View tail)
        {
            int index = IndexOf(separator);
            if (index < 0)
            {
                head = this;
                tail = default;
                return false;
            }

            head = new Utf8View(_data, index);
            tail = new Utf8View(_data + index + 1, _length - index - 1);
            return true;
        }

        // ---- hashing ----

        /// <summary>FNV-1a 64 with a SplitMix64 finalizer, so the low bits are usable directly.</summary>
        public ulong Hash()
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < _length; i++)
            {
                hash ^= _data[i];
                hash *= 1099511628211UL;
            }

            hash ^= hash >> 30;
            hash *= 0xBF58476D1CE4E5B9UL;
            hash ^= hash >> 27;
            hash *= 0x94D049BB133111EBUL;
            hash ^= hash >> 31;
            return hash;
        }

        // ---- copying / promotion ----

        public void CopyTo(Span<byte> destination)
        {
            if (destination.Length < _length)
                Fail.Msg("Utf8View: CopyTo destination is too small");

            if (_length != 0)
                MemOps.Copy(Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination)), _data, (nuint)(uint)_length);
        }

        public bool IsWellFormed() => Utf8.IsWellFormed(_data, _length);

        public ArenaString CloneTo(Arena arena) => ArenaString.From(this, arena);

        public ArenaString CloneTo(ArenaRental arena) => ArenaString.From(this, arena);

        public ArenaString CloneTo(ScratchArena arena) => ArenaString.From(this, arena);

        public ArenaString CloneTo(ArenaLease arena) => ArenaString.From(this, arena);

        // ---- parsing ----

        public bool TryParseInt32(out int value) => Utf8Parse.TryParseInt32(this, out value);

        public bool TryParseInt64(out long value) => Utf8Parse.TryParseInt64(this, out value);

        public bool TryParseUInt32(out uint value) => Utf8Parse.TryParseUInt32(this, out value);

        public bool TryParseUInt64(out ulong value) => Utf8Parse.TryParseUInt64(this, out value);

        public bool TryParseHex64(out ulong value) => Utf8Parse.TryParseHex64(this, out value);

        public bool TryParseBool(out bool value) => Utf8Parse.TryParseBool(this, out value);
    }
}
