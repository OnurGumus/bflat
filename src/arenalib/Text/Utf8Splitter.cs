using System;

namespace Arenas
{
    /// <summary>
    /// foreach-able split over a separator byte. Allocates nothing - each part is a window
    /// onto the original bytes.
    /// </summary>
    public unsafe ref struct Utf8Splitter
    {
        private Utf8View _remaining;
        private readonly byte _separator;
        private bool _exhausted;
        private Utf8View _current;

        internal Utf8Splitter(Utf8View source, byte separator)
        {
            _remaining = source;
            _separator = separator;
            _exhausted = false;
            _current = default;
        }

        public Utf8View Current => _current;

        public Utf8Splitter GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_exhausted)
                return false;

            if (_remaining.TrySplitOnce(_separator, out Utf8View head, out Utf8View tail))
            {
                _current = head;
                _remaining = tail;
            }
            else
            {
                _current = _remaining;
                _remaining = default;
                _exhausted = true;
            }

            return true;
        }
    }

    /// <summary>foreach-able line split handling both "\n" and "\r\n".</summary>
    public unsafe ref struct Utf8LineSplitter
    {
        private Utf8View _remaining;
        private bool _exhausted;
        private Utf8View _current;

        internal Utf8LineSplitter(Utf8View source)
        {
            _remaining = source;
            _exhausted = false;
            _current = default;
        }

        public Utf8View Current => _current;

        public Utf8LineSplitter GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_exhausted)
                return false;

            int newline = _remaining.IndexOf((byte)'\n');
            if (newline < 0)
            {
                _current = _remaining;
                _remaining = default;
                _exhausted = true;
                return true;
            }

            int end = newline;
            if (end > 0 && _remaining[end - 1] == (byte)'\r')
                end--;

            _current = _remaining.Slice(0, end);
            _remaining = _remaining.Slice(newline + 1);
            return true;
        }
    }

    /// <summary>
    /// foreach-able decode into Unicode scalars. Malformed sequences yield U+FFFD and advance
    /// one byte, so iteration always terminates.
    /// </summary>
    public unsafe ref struct Utf8ScalarEnumerator
    {
        private readonly Utf8View _source;
        private int _offset;
        private uint _current;
        private int _currentOffset;

        internal Utf8ScalarEnumerator(Utf8View source)
        {
            _source = source;
            _offset = 0;
            _current = 0;
            _currentOffset = 0;
        }

        /// <summary>The decoded scalar.</summary>
        public uint Current => _current;

        /// <summary>Byte offset of <see cref="Current"/> within the source.</summary>
        public int CurrentOffset => _currentOffset;

        public Utf8ScalarEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_offset >= _source.Length)
                return false;

            _currentOffset = _offset;
            Utf8.TryDecodeScalar(_source.Data + _offset, _source.Length - _offset, out _current, out int consumed);
            _offset += consumed <= 0 ? 1 : consumed;
            return true;
        }
    }
}
