using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    /// <summary>
    /// An arena-owned UTF-8 string. A plain struct, so it can live inside collections and
    /// other arena values - unlike <see cref="Utf8View"/>, which is borrowed and ref-like.
    ///
    /// Note that <see cref="Slice(int)"/> returns a Utf8View, not another ArenaString. Slicing
    /// something you own yields a *borrow*, so a sub-range can never be quietly retained past
    /// the arena scope. Call CloneTo on the view to own it.
    /// </summary>
    public readonly unsafe struct ArenaString
    {
        internal readonly ArenaCore* _arena;
        internal readonly byte* _data;
        internal readonly int _length;
        internal readonly uint _generation;

        internal ArenaString(ArenaCore* arena, byte* data, int length, uint generation)
        {
            _arena = arena;
            _data = data;
            _length = length;
            _generation = generation;
        }

        public int Length => _length;

        public bool IsEmpty => _length == 0;

        /// <summary>Borrow the contents. Must not outlive the owning arena scope.</summary>
        public Utf8View AsView()
        {
            Check.Value(_arena, _generation);
            return new Utf8View(_data, _length);
        }

        public static implicit operator Utf8View(ArenaString value) => value.AsView();

        public ReadOnlySpan<byte> AsBytes() => AsView().AsBytes();

        // ---- construction ----

        public static ArenaString From(Utf8View source, Arena arena)
            => FromCore(source, arena.AllocationCore);

        public static ArenaString From(Utf8View source, ArenaRental arena)
            => FromCore(source, arena.AllocationCore);

        public static ArenaString From(Utf8View source, ScratchArena arena)
            => FromCore(source, arena.AllocationCore);

        public static ArenaString From(Utf8View source, ArenaLease arena)
            => FromCore(source, arena.AllocationCore);

        public static ArenaString From(Utf8View source, RcArenaLease arena)
            => FromCore(source, arena.AllocationCore);

        public static ArenaString From(ReadOnlySpan<byte> utf8, Arena arena)
            => FromCore(Utf8View.FromLiteral(utf8), arena.AllocationCore);

        public static ArenaString From(ReadOnlySpan<byte> utf8, ArenaRental arena)
            => FromCore(Utf8View.FromLiteral(utf8), arena.AllocationCore);

        public static ArenaString From(ReadOnlySpan<byte> utf8, ScratchArena arena)
            => FromCore(Utf8View.FromLiteral(utf8), arena.AllocationCore);

        /// <summary>Transcode a UTF-16 C# string into the arena as UTF-8.</summary>
        public static ArenaString From(string value, Arena arena)
            => FromUtf16Core(value, arena.AllocationCore);

        public static ArenaString From(string value, ArenaRental arena)
            => FromUtf16Core(value, arena.AllocationCore);

        public static ArenaString From(string value, ScratchArena arena)
            => FromUtf16Core(value, arena.AllocationCore);

        public static ArenaString Concat(Utf8View left, Utf8View right, Arena arena)
            => ConcatCore(left, right, arena.AllocationCore);

        public static ArenaString Concat(Utf8View left, Utf8View right, ArenaRental arena)
            => ConcatCore(left, right, arena.AllocationCore);

        public static ArenaString Concat(Utf8View left, Utf8View right, ScratchArena arena)
            => ConcatCore(left, right, arena.AllocationCore);

        // ---- promotion ----

        /// <summary>Copy into another (typically longer-lived) arena.</summary>
        public ArenaString CloneTo(Arena arena) => FromCore(AsView(), arena.AllocationCore);

        public ArenaString CloneTo(ArenaRental arena) => FromCore(AsView(), arena.AllocationCore);

        public ArenaString CloneTo(ScratchArena arena) => FromCore(AsView(), arena.AllocationCore);

        public ArenaString CloneTo(ArenaLease arena) => FromCore(AsView(), arena.AllocationCore);

        // ---- delegated operations ----

        public byte this[int index]
        {
            get
            {
                Check.Bounds(index, _length);
                Check.Value(_arena, _generation);
                return _data[index];
            }
        }

        public Utf8View Slice(int start) => AsView().Slice(start);

        public Utf8View Slice(int start, int length) => AsView().Slice(start, length);

        public bool Equals(Utf8View other) => AsView().Equals(other);

        public bool Equals(ArenaString other) => AsView().Equals(other.AsView());

        public int CompareOrdinal(Utf8View other) => AsView().CompareOrdinal(other);

        public bool StartsWith(Utf8View prefix) => AsView().StartsWith(prefix);

        public bool EndsWith(Utf8View suffix) => AsView().EndsWith(suffix);

        public int IndexOf(byte value) => AsView().IndexOf(value);

        public int IndexOf(Utf8View needle) => AsView().IndexOf(needle);

        public bool Contains(Utf8View needle) => AsView().Contains(needle);

        public ulong Hash() => AsView().Hash();

        public Utf8Splitter Split(byte separator) => AsView().Split(separator);

        public Utf8LineSplitter Lines() => AsView().Lines();

        public Utf8ScalarEnumerator Chars() => AsView().Chars();

        public bool TryParseInt32(out int value) => AsView().TryParseInt32(out value);

        public bool TryParseInt64(out long value) => AsView().TryParseInt64(out value);

        public static bool operator ==(ArenaString left, ArenaString right) => left.Equals(right);

        public static bool operator !=(ArenaString left, ArenaString right) => !left.Equals(right);

        // ---- internals ----

        internal static ArenaString FromCore(Utf8View source, ArenaCore* core)
        {
            if (source.Length == 0)
                return new ArenaString(core, null, 0, core->Generation);

            byte* data = (byte*)ArenaImpl.AllocRaw(core, (nuint)(uint)source.Length, 1, false);
            MemOps.Copy(data, source.Data, (nuint)(uint)source.Length);
            return new ArenaString(core, data, source.Length, core->Generation);
        }

        private static ArenaString FromUtf16Core(string value, ArenaCore* core)
        {
            if (value == null || value.Length == 0)
                return new ArenaString(core, null, 0, core->Generation);

            int byteCount = Utf8.GetUtf8ByteCount(value);
            byte* data = (byte*)ArenaImpl.AllocRaw(core, (nuint)(uint)byteCount, 1, false);

            int written = Utf8.FromUtf16(value, data, byteCount);
            if (written != byteCount)
                Fail.Msg("ArenaString: UTF-16 transcode length mismatch");

            return new ArenaString(core, data, byteCount, core->Generation);
        }

        private static ArenaString ConcatCore(Utf8View left, Utf8View right, ArenaCore* core)
        {
            int total = left.Length + right.Length;
            if (total == 0)
                return new ArenaString(core, null, 0, core->Generation);

            byte* data = (byte*)ArenaImpl.AllocRaw(core, (nuint)(uint)total, 1, false);

            if (left.Length != 0)
                MemOps.Copy(data, left.Data, (nuint)(uint)left.Length);
            if (right.Length != 0)
                MemOps.Copy(data + left.Length, right.Data, (nuint)(uint)right.Length);

            return new ArenaString(core, data, total, core->Generation);
        }
    }
}
