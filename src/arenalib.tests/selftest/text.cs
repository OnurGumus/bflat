using System;
using Arenas;

// Phase 3 coverage: UTF-8 codec, Utf8View, ArenaString, ArenaStringBuilder,
// Utf8Format and Utf8Parse.
unsafe static partial class Program
{
    static Utf8View V(ReadOnlySpan<byte> literal) => Utf8View.FromLiteral(literal);

    static void TextTests()
    {
        Utf8CodecTests();
        Utf8ViewTests();
        SplitterTests();
        ArenaStringTests();
        BuilderTests();
        FormatTests();
        ParseTests();
    }

    static void Utf8CodecTests()
    {
        byte* buffer = stackalloc byte[8];

        Eq("encode 'A' length", Utf8.EncodeScalar('A', buffer, 8), 1);
        Eq("encode 'A' byte", buffer[0], 0x41);

        Eq("encode U+00E9 length", Utf8.EncodeScalar(0xE9, buffer, 8), 2);
        Ok("encode U+00E9 bytes", buffer[0] == 0xC3 && buffer[1] == 0xA9);

        Eq("encode U+20AC length", Utf8.EncodeScalar(0x20AC, buffer, 8), 3);
        Ok("encode U+20AC bytes", buffer[0] == 0xE2 && buffer[1] == 0x82 && buffer[2] == 0xAC);

        Eq("encode U+1F600 length", Utf8.EncodeScalar(0x1F600, buffer, 8), 4);
        Ok("encode U+1F600 bytes",
            buffer[0] == 0xF0 && buffer[1] == 0x9F && buffer[2] == 0x98 && buffer[3] == 0x80);

        Eq("encode surrogate rejected", Utf8.EncodeScalar(0xD800, buffer, 8), 0);
        Eq("encode out-of-range rejected", Utf8.EncodeScalar(0x110000, buffer, 8), 0);
        Eq("encode no room", Utf8.EncodeScalar(0x1F600, buffer, 3), 0);

        // Round trip every boundary scalar.
        RoundTrip("U+0000", 0);
        RoundTrip("U+007F", 0x7F);
        RoundTrip("U+0080", 0x80);
        RoundTrip("U+07FF", 0x7FF);
        RoundTrip("U+0800", 0x800);
        RoundTrip("U+FFFF", 0xFFFF);
        RoundTrip("U+10000", 0x10000);
        RoundTrip("U+10FFFF", 0x10FFFF);

        // Malformed input must be rejected.
        RejectBytes("overlong 2-byte slash", 0xC0, 0xAF, 0, 0, 2);
        RejectBytes("overlong 3-byte", 0xE0, 0x80, 0xAF, 0, 3);
        RejectBytes("surrogate D800", 0xED, 0xA0, 0x80, 0, 3);
        RejectBytes("above U+10FFFF", 0xF4, 0x90, 0x80, 0x80, 4);
        RejectBytes("stray continuation", 0x80, 0, 0, 0, 1);
        RejectBytes("truncated 3-byte", 0xE2, 0x82, 0, 0, 2);

        Ok("IsWellFormed on ascii", V("hello"u8).IsWellFormed());
        Ok("IsWellFormed on multibyte", V("héllo € \U0001F600"u8).IsWellFormed());

        byte* bad = stackalloc byte[2];
        bad[0] = 0xC0;
        bad[1] = 0xAF;
        Ok("IsWellFormed rejects overlong", !Utf8.IsWellFormed(bad, 2));

        // UTF-16 transcode, including a surrogate pair.
        Eq("GetUtf8ByteCount ascii", Utf8.GetUtf8ByteCount("abc"), 3);
        Eq("GetUtf8ByteCount mixed", Utf8.GetUtf8ByteCount("aé€"), 6);
        Eq("GetUtf8ByteCount astral", Utf8.GetUtf8ByteCount("\U0001F600"), 4);

        byte* transcoded = stackalloc byte[16];
        int written = Utf8.FromUtf16("a€", transcoded, 16);
        Eq("FromUtf16 length", written, 4);
        Ok("FromUtf16 bytes",
            transcoded[0] == (byte)'a' && transcoded[1] == 0xE2 && transcoded[2] == 0x82 && transcoded[3] == 0xAC);

        Eq("FromUtf16 no room", Utf8.FromUtf16("abc", transcoded, 2), -1);
    }

    static void RoundTrip(string name, uint scalar)
    {
        byte* buffer = stackalloc byte[8];
        int encoded = Utf8.EncodeScalar(scalar, buffer, 8);
        if (encoded == 0)
        {
            Ok(name, false);
            return;
        }

        bool decoded = Utf8.TryDecodeScalar(buffer, encoded, out uint result, out int consumed);
        Ok(name, decoded && result == scalar && consumed == encoded);
    }

    static void RejectBytes(string name, byte b0, byte b1, byte b2, byte b3, int available)
    {
        byte* buffer = stackalloc byte[4];
        buffer[0] = b0; buffer[1] = b1; buffer[2] = b2; buffer[3] = b3;
        Ok(name, !Utf8.TryDecodeScalar(buffer, available, out _, out _));
    }

    static void Utf8ViewTests()
    {
        Utf8View hello = V("hello world"u8);

        Eq("view length", hello.Length, 11);
        Ok("view not empty", !hello.IsEmpty);
        Eq("view[0]", hello[0], (byte)'h');

        Ok("view equals", hello.Equals(V("hello world"u8)));
        Ok("view not equals", !hello.Equals(V("hello"u8)));
        Ok("operator ==", hello == V("hello world"u8));
        Ok("operator !=", hello != V("nope"u8));

        Eq("slice(6) length", hello.Slice(6).Length, 5);
        Ok("slice(6) content", hello.Slice(6) == V("world"u8));
        Ok("slice(0,5) content", hello.Slice(0, 5) == V("hello"u8));
        Ok("slice to empty", hello.Slice(11).IsEmpty);

        Ok("startswith", hello.StartsWith(V("hello"u8)));
        Ok("not startswith", !hello.StartsWith(V("world"u8)));
        Ok("endswith", hello.EndsWith(V("world"u8)));
        Ok("startswith byte", hello.StartsWith((byte)'h'));
        Ok("endswith byte", hello.EndsWith((byte)'d'));
        Ok("startswith empty", hello.StartsWith(default(Utf8View)));

        Eq("indexof byte", hello.IndexOf((byte)'o'), 4);
        Eq("lastindexof byte", hello.LastIndexOf((byte)'o'), 7);
        Eq("indexof missing", hello.IndexOf((byte)'z'), -1);
        Eq("indexof view", hello.IndexOf(V("world"u8)), 6);
        Eq("indexof view missing", hello.IndexOf(V("worlds"u8)), -1);
        Eq("indexof empty view", hello.IndexOf(default(Utf8View)), 0);
        Ok("contains", hello.Contains(V("lo w"u8)));

        Utf8View padded = V("  \t trimmed \r\n "u8);
        Ok("trim", padded.Trim() == V("trimmed"u8));
        Ok("trimstart", padded.TrimStart().StartsWith(V("trimmed"u8)));
        Ok("trimend", padded.TrimEnd().EndsWith(V("trimmed"u8)));
        Ok("trimstart byte", V("xxabc"u8).TrimStart((byte)'x') == V("abc"u8));
        Ok("trimend byte", V("abcxx"u8).TrimEnd((byte)'x') == V("abc"u8));

        Ok("ascii ignore case", V("HeLLo"u8).EqualsAsciiIgnoreCase(V("hello"u8)));
        Ok("ascii ignore case negative", !V("HeLLo"u8).EqualsAsciiIgnoreCase(V("hellp"u8)));

        Ok("compare less", V("abc"u8).CompareOrdinal(V("abd"u8)) < 0);
        Ok("compare greater", V("abd"u8).CompareOrdinal(V("abc"u8)) > 0);
        Ok("compare equal", V("abc"u8).CompareOrdinal(V("abc"u8)) == 0);
        Ok("compare prefix shorter", V("ab"u8).CompareOrdinal(V("abc"u8)) < 0);

        Ok("hash is stable", V("hello"u8).Hash() == V("hello"u8).Hash());
        Ok("hash differs", V("hello"u8).Hash() != V("hellp"u8).Hash());
        Ok("hash of empty is stable", V(""u8).Hash() == default(Utf8View).Hash());

        // FromCString
        byte* cstr = stackalloc byte[6];
        cstr[0] = (byte)'a'; cstr[1] = (byte)'b'; cstr[2] = (byte)'c'; cstr[3] = 0;
        Ok("FromCString", Utf8View.FromCString(cstr) == V("abc"u8));

        // CopyTo
        byte* target = stackalloc byte[16];
        Span<byte> targetSpan = new Span<byte>(target, 16);
        hello.CopyTo(targetSpan);
        Ok("view CopyTo", Utf8View.FromBytes(target, 11) == hello);

        Ok("AsBytes length", hello.AsBytes().Length == 11);
    }

    static void SplitterTests()
    {
        int count = 0;
        int totalLength = 0;
        foreach (var part in V("a,bb,,ccc"u8).Split((byte)','))
        {
            count++;
            totalLength += part.Length;
        }
        Eq("split count", count, 4);
        Eq("split total length", totalLength, 6);

        // Single element when the separator is absent.
        count = 0;
        foreach (var part in V("nosep"u8).Split((byte)','))
        {
            count++;
            Ok("split single content", part == V("nosep"u8));
        }
        Eq("split no separator count", count, 1);

        bool sawEmptyOnly = true;
        count = 0;
        foreach (var part in default(Utf8View).Split((byte)','))
        {
            count++;
            if (!part.IsEmpty) sawEmptyOnly = false;
        }
        Eq("split empty count", count, 1);
        Ok("split empty is empty", sawEmptyOnly);

        // TrySplitOnce
        Ok("TrySplitOnce found", V("key=value"u8).TrySplitOnce((byte)'=', out Utf8View k, out Utf8View v));
        Ok("TrySplitOnce head", k == V("key"u8));
        Ok("TrySplitOnce tail", v == V("value"u8));
        Ok("TrySplitOnce missing", !V("keyvalue"u8).TrySplitOnce((byte)'=', out _, out _));

        // Lines, mixing \n and \r\n, with no trailing newline.
        count = 0;
        bool contentsOk = true;
        foreach (var line in V("one\ntwo\r\nthree"u8).Lines())
        {
            if (count == 0 && line != V("one"u8)) contentsOk = false;
            if (count == 1 && line != V("two"u8)) contentsOk = false;   // \r must be stripped
            if (count == 2 && line != V("three"u8)) contentsOk = false;
            count++;
        }
        Eq("lines count", count, 3);
        Ok("lines content", contentsOk);

        // Trailing newline produces a final empty line.
        count = 0;
        foreach (var line in V("a\nb\n"u8).Lines())
            count++;
        Eq("lines trailing newline", count, 3);

        // Scalars, including multi-byte.
        count = 0;
        uint last = 0;
        foreach (var scalar in V("aé€\U0001F600"u8).Chars())
        {
            count++;
            last = scalar;
        }
        Eq("scalar count", count, 4);
        Ok("last scalar", last == 0x1F600);
    }

    static void ArenaStringTests()
    {
        using var arena = Arena.Create();

        ArenaString hello = ArenaString.From(V("hello"u8), arena);
        Eq("ArenaString length", hello.Length, 5);
        Ok("ArenaString view", hello.AsView() == V("hello"u8));
        Ok("ArenaString implicit view", ((Utf8View)hello) == V("hello"u8));
        Eq("ArenaString indexer", hello[1], (byte)'e');

        ArenaString fromSpan = ArenaString.From("world"u8, arena);
        Ok("ArenaString from span", fromSpan.AsView() == V("world"u8));

        // Owning storage: mutating the source would not affect us. Prove it is a copy.
        byte* mutable = stackalloc byte[3];
        mutable[0] = (byte)'a'; mutable[1] = (byte)'b'; mutable[2] = (byte)'c';
        ArenaString copied = ArenaString.From(Utf8View.FromBytes(mutable, 3), arena);
        mutable[0] = (byte)'z';
        Ok("ArenaString is a copy", copied.AsView() == V("abc"u8));

        // UTF-16 transcode from a C# string literal.
        ArenaString transcoded = ArenaString.From("a€b", arena);
        Eq("ArenaString utf16 length", transcoded.Length, 5);
        Ok("ArenaString utf16 content", transcoded.AsView() == V("a€b"u8));

        Ok("ArenaString empty", ArenaString.From("", arena).IsEmpty);

        // Concat
        ArenaString joined = ArenaString.Concat(V("foo"u8), V("bar"u8), arena);
        Ok("ArenaString concat", joined.AsView() == V("foobar"u8));

        // Slicing an owned string yields a BORROW, by design.
        Utf8View borrowed = hello.Slice(1, 3);
        Ok("ArenaString slice is a view", borrowed == V("ell"u8));

        // Delegated operations
        Ok("ArenaString startswith", hello.StartsWith(V("he"u8)));
        Eq("ArenaString indexof", hello.IndexOf((byte)'l'), 2);
        Ok("ArenaString equality", hello == ArenaString.From(V("hello"u8), arena));
        Ok("ArenaString inequality", hello != fromSpan);
        Ok("ArenaString hash matches view", hello.Hash() == V("hello"u8).Hash());

        // Promotion out of a scratch scope - the operation the whole design exists for.
        // Safe now that scratch owns an independent chunk chain: releasing the scratch
        // cannot reclaim memory allocated from an enclosing scope.
        ArenaString promoted;
        using (var request = arena.Rent())
        {
            using (var scratch = request.RentScratch())
            {
                ArenaString temporary = ArenaString.From(V("survives"u8), scratch);
                promoted = temporary.CloneTo(request);
            }

            Ok("CloneTo the enclosing rental survives scratch release",
                promoted.AsView() == V("survives"u8));
        }

        // Promoting straight to the root works too, because a live scratch does not put the
        // arena behind a mark the way a rental does.
        ArenaString promotedToRoot;
        using (var scratch = arena.RentScratch())
        {
            ArenaString temporary = ArenaString.From(V("to the root"u8), scratch);
            promotedToRoot = temporary.CloneTo(arena);
        }
        Ok("CloneTo the root survives scratch release",
            promotedToRoot.AsView() == V("to the root"u8));
    }

    static void BuilderTests()
    {
        using var arena = Arena.Create();

        var builder = ArenaStringBuilder.Create(arena);
        Ok("builder starts empty", builder.IsEmpty);

        builder.Append(V("Box "u8));
        builder.AppendInt32(42);
        builder.Append(V(" does not fit"u8));

        Eq("builder length", builder.Length, 4 + 2 + 13);

        ArenaString message = builder.ToArenaString(arena);
        Ok("builder result", message.AsView() == V("Box 42 does not fit"u8));

        // Contiguous while it fits in one chunk.
        Ok("builder contiguous", builder.TryGetContiguous(out Utf8View contiguous));
        Ok("builder contiguous content", contiguous == V("Box 42 does not fit"u8));

        // Force many chunk boundaries.
        var big = ArenaStringBuilder.Create(arena, 16);
        for (int i = 0; i < 1000; i++)
            big.Append(V("0123456789"u8));

        Eq("big builder length", big.Length, 10000);
        ArenaString bigResult = big.ToArenaString(arena);
        Eq("big result length", bigResult.Length, 10000);
        Ok("big result head", bigResult.Slice(0, 10) == V("0123456789"u8));
        Ok("big result tail", bigResult.Slice(9990, 10) == V("0123456789"u8));

        bool allDigits = true;
        for (int i = 0; i < bigResult.Length; i++)
            if (bigResult[i] != (byte)('0' + (i % 10))) allDigits = false;
        Ok("big result content", allDigits);

        // Extend-in-place: a builder that is the arena's newest allocation should grow
        // without chaining, so the result stays contiguous.
        var grow = ArenaStringBuilder.Create(arena, 16);
        for (int i = 0; i < 100; i++)
            grow.Append(V("abcdefgh"u8));
        Ok("grow stayed contiguous", grow.TryGetContiguous(out _));
        Eq("grow length", grow.Length, 800);

        // Clear reuses the chunks.
        grow.Clear();
        Eq("clear resets length", grow.Length, 0);
        grow.Append(V("after clear"u8));
        Ok("append after clear", grow.ToArenaString(arena).AsView() == V("after clear"u8));

        // Every append overload.
        var mixed = ArenaStringBuilder.Create(arena);
        mixed.Append((byte)'[');
        mixed.Append("utf16 €");
        mixed.Append((byte)' ');
        mixed.AppendScalar(0x1F600);
        mixed.Append((byte)' ');
        mixed.AppendBool(true);
        mixed.Append((byte)' ');
        mixed.AppendHex(0xDEAD, 8, true);
        mixed.Append((byte)' ');
        mixed.AppendFixed(12345, 2);
        mixed.Append((byte)' ');
        mixed.AppendInt64(-9223372036854775808L);
        mixed.Append((byte)']');

        ArenaString mixedResult = mixed.ToArenaString(arena);
        Ok("mixed builder",
            mixedResult.AsView() == V("[utf16 € \U0001F600 true 0000DEAD 123.45 -9223372036854775808]"u8));

        // Padding
        var pad = ArenaStringBuilder.Create(arena);
        pad.AppendPadLeft(V("7"u8), 4, (byte)' ');
        pad.AppendPadRight(V("x"u8), 3, (byte)'.');
        Ok("padding", pad.ToArenaString(arena).AsView() == V("   7x.."u8));

        // AppendLine + Lines round trip
        var lines = ArenaStringBuilder.Create(arena);
        lines.AppendLine(V("one"u8));
        lines.AppendLine(V("two"u8));
        ArenaString linesResult = lines.ToArenaString(arena);
        int lineCount = 0;
        foreach (var _ in linesResult.Lines())
            lineCount++;
        Eq("builder lines", lineCount, 3);

        // CopyTo
        byte* target = stackalloc byte[32];
        Span<byte> targetSpan = new Span<byte>(target, 32);
        var small = ArenaStringBuilder.Create(arena);
        small.Append(V("copy me"u8));
        small.CopyTo(targetSpan);
        Ok("builder CopyTo", Utf8View.FromBytes(target, 7) == V("copy me"u8));
    }

    static void FormatTests()
    {
        byte* raw = stackalloc byte[64];
        Span<byte> buffer = new Span<byte>(raw, 64);

        FormatEq("format 0", Utf8Format.FormatInt32(0, buffer), raw, "0"u8);
        FormatEq("format 42", Utf8Format.FormatInt32(42, buffer), raw, "42"u8);
        FormatEq("format -42", Utf8Format.FormatInt32(-42, buffer), raw, "-42"u8);
        FormatEq("format int.MaxValue", Utf8Format.FormatInt32(int.MaxValue, buffer), raw, "2147483647"u8);
        FormatEq("format int.MinValue", Utf8Format.FormatInt32(int.MinValue, buffer), raw, "-2147483648"u8);
        FormatEq("format long.MinValue", Utf8Format.FormatInt64(long.MinValue, buffer), raw, "-9223372036854775808"u8);
        FormatEq("format long.MaxValue", Utf8Format.FormatInt64(long.MaxValue, buffer), raw, "9223372036854775807"u8);
        FormatEq("format ulong max", Utf8Format.FormatUInt64(18446744073709551615UL, buffer), raw, "18446744073709551615"u8);

        FormatEq("format hex", Utf8Format.FormatHex64(0xDEADBEEF, buffer, 0, false), raw, "deadbeef"u8);
        FormatEq("format HEX", Utf8Format.FormatHex64(0xDEADBEEF, buffer, 0, true), raw, "DEADBEEF"u8);
        FormatEq("format hex padded", Utf8Format.FormatHex64(0xFF, buffer, 8, true), raw, "000000FF"u8);
        FormatEq("format binary", Utf8Format.FormatUInt64(5, buffer, 2, 0, false), raw, "101"u8);
        FormatEq("format base36", Utf8Format.FormatUInt64(35, buffer, 36, 0, false), raw, "z"u8);

        FormatEq("format true", Utf8Format.FormatBool(true, buffer), raw, "true"u8);
        FormatEq("format false", Utf8Format.FormatBool(false, buffer), raw, "false"u8);

        FormatEq("fixed 123.45", Utf8Format.FormatFixed(12345, 2, buffer), raw, "123.45"u8);
        FormatEq("fixed -1.05", Utf8Format.FormatFixed(-105, 2, buffer), raw, "-1.05"u8);
        FormatEq("fixed 0.01", Utf8Format.FormatFixed(1, 2, buffer), raw, "0.01"u8);
        FormatEq("fixed 0 decimals", Utf8Format.FormatFixed(7, 0, buffer), raw, "7"u8);

        FormatEq("bytesize B", Utf8Format.FormatByteSize(512, buffer), raw, "512.0 B"u8);
        FormatEq("bytesize KiB", Utf8Format.FormatByteSize(1536, buffer), raw, "1.5 KiB"u8);
        FormatEq("bytesize MiB", Utf8Format.FormatByteSize(1024 * 1024 * 3, buffer), raw, "3.0 MiB"u8);

        // Destination too small: report -1, do not scribble.
        Span<byte> tiny = new Span<byte>(raw, 2);
        Eq("format too small", Utf8Format.FormatInt32(12345, tiny), -1);
        Eq("format bool too small", Utf8Format.FormatBool(false, tiny), -1);
        Eq("format empty destination", Utf8Format.FormatInt32(1, new Span<byte>(raw, 0)), -1);
    }

    static void FormatEq(string name, int written, byte* raw, ReadOnlySpan<byte> expected)
    {
        if (written < 0)
        {
            Ok(name, false);
            return;
        }

        Ok(name, Utf8View.FromBytes(raw, written) == Utf8View.FromLiteral(expected));
    }

    static void ParseTests()
    {
        Ok("parse 0", V("0"u8).TryParseInt32(out int zero) && zero == 0);
        Ok("parse 42", V("42"u8).TryParseInt32(out int answer) && answer == 42);
        Ok("parse -42", V("-42"u8).TryParseInt32(out int negative) && negative == -42);
        Ok("parse +42", V("+42"u8).TryParseInt32(out int plus) && plus == 42);
        Ok("parse int.MaxValue", V("2147483647"u8).TryParseInt32(out int max) && max == int.MaxValue);
        Ok("parse int.MinValue", V("-2147483648"u8).TryParseInt32(out int min) && min == int.MinValue);

        Ok("parse long.MinValue",
            V("-9223372036854775808"u8).TryParseInt64(out long lmin) && lmin == long.MinValue);
        Ok("parse long.MaxValue",
            V("9223372036854775807"u8).TryParseInt64(out long lmax) && lmax == long.MaxValue);
        Ok("parse ulong max",
            V("18446744073709551615"u8).TryParseUInt64(out ulong umax) && umax == 18446744073709551615UL);

        // Rejections
        Ok("reject empty", !V(""u8).TryParseInt32(out _));
        Ok("reject sign only", !V("-"u8).TryParseInt32(out _));
        Ok("reject letters", !V("12a"u8).TryParseInt32(out _));
        Ok("reject leading space", !V(" 12"u8).TryParseInt32(out _));
        Ok("reject trailing space", !V("12 "u8).TryParseInt32(out _));
        Ok("reject int overflow", !V("2147483648"u8).TryParseInt32(out _));
        Ok("reject int underflow", !V("-2147483649"u8).TryParseInt32(out _));
        Ok("reject ulong overflow", !V("18446744073709551616"u8).TryParseUInt64(out _));
        Ok("reject negative for unsigned", !V("-1"u8).TryParseUInt32(out _));

        // Hex
        Ok("parse hex", V("deadbeef"u8).TryParseHex64(out ulong hex) && hex == 0xDEADBEEF);
        Ok("parse hex prefixed", V("0xFF"u8).TryParseHex64(out ulong hexPrefixed) && hexPrefixed == 255);
        Ok("parse hex uppercase", V("0XAb"u8).TryParseHex64(out ulong hexUpper) && hexUpper == 0xAB);
        Ok("reject bad hex", !V("0xZZ"u8).TryParseHex64(out _));

        // Bool
        Ok("parse true", V("true"u8).TryParseBool(out bool t) && t);
        Ok("parse TRUE", V("TRUE"u8).TryParseBool(out bool t2) && t2);
        Ok("parse false", V("false"u8).TryParseBool(out bool f) && !f);
        Ok("reject bool garbage", !V("yes"u8).TryParseBool(out _));

        // Round trip against the formatter.
        byte* raw = stackalloc byte[32];
        Span<byte> buffer = new Span<byte>(raw, 32);
        bool roundTripOk = true;
        for (int i = 0; i < 6; i++)
        {
            long value = i switch
            {
                0 => 0,
                1 => 1,
                2 => -1,
                3 => long.MaxValue,
                4 => long.MinValue,
                _ => -1234567890123L,
            };

            int written = Utf8Format.FormatInt64(value, buffer);
            if (written < 0 || !Utf8View.FromBytes(raw, written).TryParseInt64(out long parsed) || parsed != value)
                roundTripOk = false;
        }
        Ok("format/parse round trip", roundTripOk);
    }
}
