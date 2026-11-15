using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    internal unsafe struct SbChunk
    {
        public SbChunk* Next;
        public int Length;
        public int Capacity;
        // Payload follows this header inline.
    }

    /// <summary>
    /// A chunked UTF-8 builder.
    ///
    /// Growth never copies and never abandons a buffer: it first tries to extend the current
    /// chunk in place (which succeeds whenever the builder is the arena's most recent
    /// allocation - the common "build one string in a loop" case), and otherwise chains a new
    /// chunk. The single copy happens in <see cref="ToArenaString(ref Arena)"/>.
    ///
    /// That matters because in an arena, List-style double-and-abandon leaves every
    /// intermediate buffer resident until the next rewind.
    /// </summary>
    public unsafe struct ArenaStringBuilder
    {
        private ArenaCore* _arena;
        private SbChunk* _head;
        private SbChunk* _tail;
        private int _length;
        private int _minChunk;
        private uint _generation;

        public const int DefaultMinChunk = 256;

        public static ArenaStringBuilder Create(Arena arena, int minChunk = DefaultMinChunk)
            => CreateCore(arena.AllocationCore, minChunk);

        public static ArenaStringBuilder Create(ArenaRental arena, int minChunk = DefaultMinChunk)
            => CreateCore(arena.AllocationCore, minChunk);

        public static ArenaStringBuilder Create(ScratchArena arena, int minChunk = DefaultMinChunk)
            => CreateCore(arena.AllocationCore, minChunk);

        public static ArenaStringBuilder Create(ArenaLease arena, int minChunk = DefaultMinChunk)
            => CreateCore(arena.AllocationCore, minChunk);

        private static ArenaStringBuilder CreateCore(ArenaCore* core, int minChunk)
        {
            if (minChunk < 16)
                minChunk = 16;

            ArenaStringBuilder builder = default;
            builder._arena = core;
            builder._minChunk = minChunk;
            builder._generation = core->Generation;
            return builder;
        }

        public int Length => _length;

        public bool IsEmpty => _length == 0;

        public void Clear()
        {
            // Retain the chunks we already own; only reset their fill.
            for (SbChunk* chunk = _head; chunk != null; chunk = chunk->Next)
                chunk->Length = 0;

            _tail = _head;
            _length = 0;
        }

        // ---- appending ----

        public void Append(Utf8View text)
        {
            if (text.Length == 0)
                return;

            Check.Value(_arena, _generation);
            EnsureTail(text.Length);

            MemOps.Copy(Payload(_tail) + _tail->Length, text.Data, (nuint)(uint)text.Length);
            _tail->Length += text.Length;
            _length += text.Length;
        }

        public void Append(ArenaString text) => Append(text.AsView());

        public void Append(ReadOnlySpan<byte> utf8) => Append(Utf8View.FromLiteral(utf8));

        public void Append(byte value)
        {
            Check.Value(_arena, _generation);
            EnsureTail(1);

            Payload(_tail)[_tail->Length] = value;
            _tail->Length++;
            _length++;
        }

        /// <summary>Append a Unicode scalar, encoded as UTF-8.</summary>
        public void AppendScalar(uint scalar)
        {
            Check.Value(_arena, _generation);
            EnsureTail(4);

            int written = Utf8.EncodeScalar(scalar, Payload(_tail) + _tail->Length, _tail->Capacity - _tail->Length);
            if (written == 0)
            {
                written = Utf8.EncodeScalar(Utf8.ReplacementChar, Payload(_tail) + _tail->Length, _tail->Capacity - _tail->Length);
                if (written == 0)
                    Fail.Msg("ArenaStringBuilder: could not encode scalar");
            }

            _tail->Length += written;
            _length += written;
        }

        public void Append(char value) => AppendScalar(value);

        /// <summary>Append a UTF-16 C# string, transcoding to UTF-8.</summary>
        public void Append(string value)
        {
            if (value == null || value.Length == 0)
                return;

            int byteCount = Utf8.GetUtf8ByteCount(value);
            Check.Value(_arena, _generation);
            EnsureTail(byteCount);

            int written = Utf8.FromUtf16(value, Payload(_tail) + _tail->Length, _tail->Capacity - _tail->Length);
            if (written < 0)
                Fail.Msg("ArenaStringBuilder: UTF-16 transcode did not fit");

            _tail->Length += written;
            _length += written;
        }

        public void AppendLine()
        {
            Append((byte)'\n');
        }

        public void AppendLine(Utf8View text)
        {
            Append(text);
            Append((byte)'\n');
        }

        public void AppendRepeat(byte value, int count)
        {
            if (count <= 0)
                return;

            Check.Value(_arena, _generation);
            EnsureTail(count);

            MemOps.Set(Payload(_tail) + _tail->Length, value, (nuint)(uint)count);
            _tail->Length += count;
            _length += count;
        }

        // ---- numbers ----

        public void AppendInt32(int value) => AppendInt64(value);

        public void AppendUInt32(uint value) => AppendUInt64(value);

        public void AppendInt64(long value, int radix = 10, int minDigits = 0, bool upper = false)
        {
            byte* scratch = stackalloc byte[Utf8Format.MaxRadix2Length + 1];
            int written = Utf8Format.FormatInt64(value, scratch, Utf8Format.MaxRadix2Length + 1, radix, minDigits, upper);
            if (written < 0)
                Fail.Msg("ArenaStringBuilder: number formatting overflowed");

            Append(Utf8View.FromBytes(scratch, written));
        }

        public void AppendUInt64(ulong value, int radix = 10, int minDigits = 0, bool upper = false)
        {
            byte* scratch = stackalloc byte[Utf8Format.MaxRadix2Length];
            int written = Utf8Format.FormatUInt64(value, scratch, Utf8Format.MaxRadix2Length, radix, minDigits, upper);
            if (written < 0)
                Fail.Msg("ArenaStringBuilder: number formatting overflowed");

            Append(Utf8View.FromBytes(scratch, written));
        }

        public void AppendHex(ulong value, int digits = 0, bool upper = false)
            => AppendUInt64(value, 16, digits, upper);

        public void AppendBool(bool value)
            => Append(value ? "true"u8 : "false"u8);

        public void AppendFixed(long scaledValue, int decimals)
        {
            byte* scratch = stackalloc byte[Utf8Format.MaxIntegerLength + 24];
            Span<byte> span = MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>(scratch), Utf8Format.MaxIntegerLength + 24);

            int written = Utf8Format.FormatFixed(scaledValue, decimals, span);
            if (written < 0)
                Fail.Msg("ArenaStringBuilder: fixed-point formatting overflowed");

            Append(Utf8View.FromBytes(scratch, written));
        }

        public void AppendPadLeft(Utf8View text, int width, byte pad)
        {
            if (text.Length < width)
                AppendRepeat(pad, width - text.Length);
            Append(text);
        }

        public void AppendPadRight(Utf8View text, int width, byte pad)
        {
            Append(text);
            if (text.Length < width)
                AppendRepeat(pad, width - text.Length);
        }

        // ---- output ----

        /// <summary>True when the content lives in a single chunk, so no copy is needed to view it.</summary>
        public bool TryGetContiguous(out Utf8View view)
        {
            if (_head == null)
            {
                view = default;
                return true;
            }

            if (_head->Next == null || _length == _head->Length)
            {
                view = Utf8View.FromBytes(Payload(_head), _head->Length);
                return true;
            }

            view = default;
            return false;
        }

        public void CopyTo(Span<byte> destination)
        {
            if (destination.Length < _length)
                Fail.Msg("ArenaStringBuilder: CopyTo destination is too small");

            byte* target = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination));
            int offset = 0;

            for (SbChunk* chunk = _head; chunk != null; chunk = chunk->Next)
            {
                if (chunk->Length != 0)
                    MemOps.Copy(target + offset, Payload(chunk), (nuint)(uint)chunk->Length);
                offset += chunk->Length;
            }
        }

        public ArenaString ToArenaString(Arena arena) => ToArenaStringCore(arena.AllocationCore);

        public ArenaString ToArenaString(ArenaRental arena) => ToArenaStringCore(arena.AllocationCore);

        public ArenaString ToArenaString(ScratchArena arena) => ToArenaStringCore(arena.AllocationCore);

        public ArenaString ToArenaString(ArenaLease arena) => ToArenaStringCore(arena.AllocationCore);

        private ArenaString ToArenaStringCore(ArenaCore* target)
        {
            if (_length == 0)
                return new ArenaString(target, null, 0, target->Generation);

            byte* data = (byte*)ArenaImpl.AllocRaw(target, (nuint)(uint)_length, 1, false);
            int offset = 0;

            for (SbChunk* chunk = _head; chunk != null; chunk = chunk->Next)
            {
                if (chunk->Length != 0)
                    MemOps.Copy(data + offset, Payload(chunk), (nuint)(uint)chunk->Length);
                offset += chunk->Length;
            }

            return new ArenaString(target, data, _length, target->Generation);
        }

        // ---- internals ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* Payload(SbChunk* chunk) => (byte*)chunk + sizeof(SbChunk);

        private void EnsureTail(int needed)
        {
            if (_tail != null && _tail->Capacity - _tail->Length >= needed)
                return;

            // Prefer growing in place: free and copy-free whenever this builder is the most
            // recent thing allocated from the arena.
            if (_tail != null && _tail->Next == null)
            {
                int wanted = _tail->Capacity;
                while (wanted - _tail->Length < needed)
                {
                    if (wanted > int.MaxValue / 2)
                    {
                        wanted = _tail->Length + needed;
                        break;
                    }

                    wanted *= 2;
                }

                nuint oldSize = (nuint)(uint)(sizeof(SbChunk) + _tail->Capacity);
                nuint newSize = (nuint)(uint)(sizeof(SbChunk) + wanted);

                if (ArenaImpl.TryExtendInPlace(_arena, _tail, oldSize, newSize))
                {
                    _tail->Capacity = wanted;
                    return;
                }
            }

            // Reuse a chunk we already own (after Clear) if it is big enough.
            if (_tail != null && _tail->Next != null && _tail->Next->Capacity >= needed && _tail->Next->Length == 0)
            {
                _tail = _tail->Next;
                return;
            }

            int capacity = needed > _minChunk ? needed : _minChunk;
            SbChunk* chunk = (SbChunk*)ArenaImpl.AllocRaw(
                _arena, (nuint)(uint)(sizeof(SbChunk) + capacity), (nuint)sizeof(void*), false);

            chunk->Next = null;
            chunk->Length = 0;
            chunk->Capacity = capacity;

            if (_tail == null)
            {
                _head = chunk;
                _tail = chunk;
            }
            else
            {
                _tail->Next = chunk;
                _tail = chunk;
            }
        }
    }
}
