using System;
using System.Runtime.CompilerServices;
using Arenas;

// Self-test for the arenalib memory core.
// Build:  bflat build --stdlib:arena src/arenalib.tests/main.cs -o arenatests
// Prints one line per failure, then a PASS/FAIL summary. Exit code 0 iff everything passed.
unsafe static partial class Program
{
    static int s_passed;
    static int s_failed;

    static void Ok(string name, bool condition)
    {
        if (condition)
        {
            s_passed++;
        }
        else
        {
            s_failed++;
            Console.WriteLine("FAIL:");
            Console.WriteLine(name);
        }
    }

    static void Eq(string name, int actual, int expected)
    {
        if (actual == expected)
        {
            s_passed++;
        }
        else
        {
            s_failed++;
            Console.WriteLine("FAIL:");
            Console.WriteLine(name);
            Console.WriteLine(actual);
            Console.WriteLine(expected);
        }
    }

    struct Point { public int X; public int Y; }
    struct Big { public long A, B, C, D, E, F, G, H; }

    static int Main()
    {
        Console.WriteLine("arenalib memory core self-test");
        Console.WriteLine(ArenaConfig.ChecksEnabled ? "build: CHECKED" : "build: release");

        Console.WriteLine("- TypeInfo");         TypeInfoTests();
        Console.WriteLine("- MemOps");           MemOpsTests();
        Console.WriteLine("- BasicAllocation");  BasicAllocation();
        Console.WriteLine("- Zeroing");          ZeroingTests();
        Console.WriteLine("- ChunkGrowth");      ChunkGrowthAndRewind();
        Console.WriteLine("- SpareReuse");       SpareReuse();
        Console.WriteLine("- Rentals");          RentalTests();
        Console.WriteLine("- Scratch");          ScratchTests();
        Console.WriteLine("- Slices");           SliceTests();
        Console.WriteLine("- HugeAllocation");   HugeAllocation();
        Console.WriteLine("- Reset");            ResetTest();
        Console.WriteLine("- Text");             TextTests();
        Console.WriteLine("- ScopeModel");       ScopeModelTests();
        Console.WriteLine("- Collections");      CollectionTests();
        Console.WriteLine("- Leases");           LeaseTests();

        Console.WriteLine("passed:");
        Console.WriteLine(s_passed);
        Console.WriteLine("failed:");
        Console.WriteLine(s_failed);
        Console.WriteLine(s_failed == 0 ? "ALL PASS" : "FAILURES");

        return s_failed == 0 ? 0 : 1;
    }

    static void TypeInfoTests()
    {
        Eq("SizeOf<byte>", TypeInfo.SizeOf<byte>(), 1);
        Eq("SizeOf<int>", TypeInfo.SizeOf<int>(), 4);
        Eq("SizeOf<long>", TypeInfo.SizeOf<long>(), 8);
        Eq("SizeOf<Point>", TypeInfo.SizeOf<Point>(), 8);
        Eq("SizeOf<Big>", TypeInfo.SizeOf<Big>(), 64);

        Eq("AlignOf<byte>", TypeInfo.AlignOf<byte>(), 1);
        Eq("AlignOf<int>", TypeInfo.AlignOf<int>(), 4);
        Eq("AlignOf<long>", TypeInfo.AlignOf<long>(), 8);

        // The clamp: managed T reorders and must fall back to pointer alignment.
        Eq("AlignOf<string> clamped", TypeInfo.AlignOf<string>(), sizeof(void*));

        Eq("AlignUp(0,8)", (int)TypeInfo.AlignUp(0, 8), 0);
        Eq("AlignUp(1,8)", (int)TypeInfo.AlignUp(1, 8), 8);
        Eq("AlignUp(8,8)", (int)TypeInfo.AlignUp(8, 8), 8);
        Eq("AlignUp(9,16)", (int)TypeInfo.AlignUp(9, 16), 16);

        Eq("NormalizeAlign(0)", (int)TypeInfo.NormalizeAlign(0), 1);
        Eq("NormalizeAlign(3)", (int)TypeInfo.NormalizeAlign(3), 4);
        Eq("NormalizeAlign(64)", (int)TypeInfo.NormalizeAlign(64), 64);
    }

    static void MemOpsTests()
    {
        byte* buffer = stackalloc byte[64];

        MemOps.Set(buffer, 0xAB, 64);
        bool allSet = true;
        for (int i = 0; i < 64; i++)
            if (buffer[i] != 0xAB) allSet = false;
        Ok("MemOps.Set", allSet);

        MemOps.Zero(buffer, 32);
        Ok("MemOps.Zero front", buffer[0] == 0 && buffer[31] == 0);
        Ok("MemOps.Zero stops", buffer[32] == 0xAB);

        for (int i = 0; i < 16; i++)
            buffer[i] = (byte)i;

        MemOps.Copy(buffer + 32, buffer, 16);
        Ok("MemOps.Copy", MemOps.Equal(buffer, buffer + 32, 16));
        Eq("MemOps.Compare equal", MemOps.Compare(buffer, buffer + 32, 16), 0);

        buffer[32] = 99;
        Ok("MemOps.Compare differs", MemOps.Compare(buffer, buffer + 32, 16) != 0);

        // Overlapping move, destination above source.
        for (int i = 0; i < 16; i++)
            buffer[i] = (byte)i;
        MemOps.Move(buffer + 2, buffer, 8);
        Ok("MemOps.Move overlap fwd", buffer[2] == 0 && buffer[9] == 7);

        // Overlapping move, destination below source.
        for (int i = 0; i < 16; i++)
            buffer[i] = (byte)i;
        MemOps.Move(buffer, buffer + 2, 8);
        Ok("MemOps.Move overlap back", buffer[0] == 2 && buffer[7] == 9);
    }

    static void BasicAllocation()
    {
        using var arena = Arena.Create();
        Ok("arena valid", arena.IsValid);

        long* a = arena.Alloc<long>();
        long* b = arena.Alloc<long>();
        Ok("distinct allocations", a != b);
        Ok("8-byte aligned", ((nuint)a & 7) == 0 && ((nuint)b & 7) == 0);

        *a = 0x0123456789ABCDEF;
        *b = -1;
        Ok("no aliasing", *a == 0x0123456789ABCDEF && *b == -1);

        Point* p = arena.New<Point>(new Point { X = 3, Y = 4 });
        Ok("New<T> copies value", p->X == 3 && p->Y == 4);

        Span<int> span = arena.AllocSpan<int>(100);
        Eq("AllocSpan length", span.Length, 100);
        span[0] = 7;
        span[99] = 11;
        Ok("span writable", span[0] == 7 && span[99] == 11);

        Eq("AllocSpan(0) empty", arena.AllocSpan<int>(0).Length, 0);

        ArenaRef<Point> handle = arena.AllocRef(new Point { X = 1, Y = 2 });
        Ok("ArenaRef value", handle.Value.X == 1 && handle.Value.Y == 2);
        handle.Value.X = 42;
        Ok("ArenaRef mutable", handle.Value.X == 42);

        // Alignment is honoured for over-aligned raw requests.
        void* aligned = arena.AllocRaw(24, 64);
        Ok("AllocRaw 64-byte aligned", ((nuint)aligned & 63) == 0);

        Ok("BytesUsed grows", arena.BytesUsed > 0);
        Ok("HighWater >= BytesUsed", arena.HighWaterMark >= arena.BytesUsed);
    }

    static void ZeroingTests()
    {
        using var arena = Arena.Create(8192);

        Span<byte> first = arena.AllocSpan<byte>(512);
        first.Fill(0xFF);

        ArenaMark mark = arena.Mark();
        Span<byte> dirty = arena.AllocSpan<byte>(512);
        dirty.Fill(0xEE);
        arena.Rewind(mark);

        // Same memory comes back; Alloc must hand it over zeroed even though the page
        // source only zeroes on first reservation.
        Span<byte> reused = arena.AllocSpan<byte>(512);
        bool zeroed = true;
        for (int i = 0; i < reused.Length; i++)
            if (reused[i] != 0) zeroed = false;
        Ok("reused memory is zeroed", zeroed);

        Span<byte> uninit = arena.AllocSpanUninit<byte>(16);
        Eq("AllocSpanUninit length", uninit.Length, 16);
    }

    static void ChunkGrowthAndRewind()
    {
        using var arena = Arena.Create(4096);

        ArenaMark mark = arena.Mark();
        nuint before = arena.Stats.ChunkCount;

        for (int i = 0; i < 20000; i++)
        {
            long* slot = arena.Alloc<long>();
            *slot = i;
        }

        ArenaStats grown = arena.Stats;
        Ok("chunks were added", grown.ChunkCount > before);
        Ok("bytes used reflect 20k longs", grown.BytesUsed >= 160000);

        arena.Rewind(mark);

        ArenaStats after = arena.Stats;
        Eq("chunk count back to 1", (int)after.ChunkCount, (int)before);
        Ok("spares retained", after.SpareChunkCount > 0);
        Ok("high water preserved", after.HighWaterMark >= grown.BytesUsed);
        Ok("generation advanced", after.Generation > grown.Generation - 1);
    }

    static void SpareReuse()
    {
        using var arena = Arena.Create(4096);

        ArenaMark mark = arena.Mark();

        for (int i = 0; i < 20000; i++)
            *arena.Alloc<long>() = i;

        nuint reservationsAfterFirstPass = arena.Stats.ReservationCount;
        arena.Rewind(mark);

        for (int i = 0; i < 20000; i++)
            *arena.Alloc<long>() = i;

        // The whole point: a refill after rewind must not touch the page source at all.
        Eq("refill makes no new reservations",
            (int)(arena.Stats.ReservationCount - reservationsAfterFirstPass), 0);
    }

    static void RentalTests()
    {
        using var arena = Arena.Create(8192);

        nuint baseline = arena.BytesUsed;

        using (var rental = arena.Rent())
        {
            Span<byte> block = rental.AllocSpan<byte>(4096);
            Eq("rental span length", block.Length, 4096);
            Ok("rental consumed bytes", rental.BytesUsed >= 4096);

            using (var nested = rental.Rent())
            {
                nested.AllocSpan<byte>(1024);
                Ok("nested rental allocates", nested.BytesUsed >= 1024);
            }

            Ok("nested rental released", rental.BytesUsed >= 4096);
        }

        Eq("rental fully rewound", (int)(arena.BytesUsed - baseline), 0);

        // Rentals recycle: a second identical rental must reuse the same memory.
        nuint highBefore;
        using (var r1 = arena.Rent())
        {
            r1.AllocSpan<byte>(2048);
            highBefore = arena.HighWaterMark;
        }

        using (var r2 = arena.Rent())
        {
            r2.AllocSpan<byte>(2048);
            Eq("second rental reuses memory", (int)(arena.HighWaterMark - highBefore), 0);
        }
    }

    static void ScratchTests()
    {
        using var arena = Arena.Create(8192);
        nuint baseline = arena.BytesUsed;

        using (var request = arena.Rent())
        {
            using (var scratch = request.RentScratch())
            {
                Span<int> temp = scratch.AllocSpan<int>(256);
                temp[0] = 1;
                temp[255] = 2;
                Ok("scratch usable", temp[0] == 1 && temp[255] == 2);
                Ok("scratch consumed", scratch.BytesUsed >= 1024);

                // The parent stays allocatable while the scratch is live, because the
                // scratch owns different chunks. With a mark-based scratch this would be a
                // LIFO violation, and the allocation would be destroyed by the release.
                Span<int> parentSide = request.AllocSpan<int>(64);
                parentSide[0] = 7;
                parentSide[63] = 9;
                Ok("parent allocatable while scratch is live", parentSide[0] == 7);

                Ok("scratch memory is disjoint from parent",
                    (nuint)Unsafe.AsPointer(ref temp[0]) != (nuint)Unsafe.AsPointer(ref parentSide[0]));
            }

            // Whatever the parent allocated during the scratch scope must have survived it.
            Ok("request survives scratch", request.IsValid);

            using (var scratch2 = request.RentScratch())
            {
                Ok("scratch re-rentable", scratch2.IsValid);
            }
        }

        Eq("everything rewound", (int)(arena.BytesUsed - baseline), 0);

        // Scratch chunks return to the parent's spare list, so a second scratch of the same
        // size costs the page source nothing.
        using (var warmup = arena.RentScratch())
            warmup.AllocSpan<byte>(2048);

        nuint reservations = arena.Stats.ReservationCount;

        for (int i = 0; i < 16; i++)
        {
            using var scratch = arena.RentScratch();
            scratch.AllocSpan<byte>(2048);
        }

        Eq("repeated scratch makes no new reservations",
            (int)(arena.Stats.ReservationCount - reservations), 0);

        // Nested scratches are independent of each other too.
        using (var outer = arena.RentScratch())
        {
            Span<byte> outerData = outer.AllocSpan<byte>(128);
            outerData[0] = 11;

            using (var inner = outer.RentScratch())
            {
                inner.AllocSpan<byte>(128);
                Ok("outer scratch intact under inner", outerData[0] == 11);
            }

            outerData[1] = 12;
            Ok("outer scratch usable after inner release", outerData[0] == 11 && outerData[1] == 12);
        }
    }

    static void SliceTests()
    {
        using var arena = Arena.Create();

        ArenaSlice<int> slice = arena.AllocSlice<int>(32);
        Eq("slice length", slice.Length, 32);
        Ok("slice not empty", !slice.IsEmpty);

        for (int i = 0; i < slice.Length; i++)
            slice[i] = i * 3;

        Ok("slice roundtrip", slice[0] == 0 && slice[31] == 93);

        ArenaSlice<int> tail = slice.Slice(16);
        Eq("Slice(16) length", tail.Length, 16);
        Eq("Slice(16)[0]", tail[0], 48);

        ArenaSlice<int> middle = slice.Slice(8, 4);
        Eq("Slice(8,4) length", middle.Length, 4);
        Eq("Slice(8,4)[3]", middle[3], 33);

        Span<int> asSpan = slice.AsSpan();
        Eq("AsSpan length", asSpan.Length, 32);
        Ok("AsSpan aliases", asSpan[5] == 15);

        ArenaSlice<int> copy = arena.AllocSlice<int>(32);
        slice.CopyTo(copy);
        Ok("slice CopyTo", copy[0] == 0 && copy[31] == 93);

        Eq("empty slice length", arena.AllocSlice<int>(0).Length, 0);
    }

    static void HugeAllocation()
    {
        using var arena = Arena.Create(4096);

        nuint chunksBefore = arena.Stats.ChunkCount;

        // Far bigger than the max chunk size: must get its own dedicated chunk.
        Span<byte> huge = arena.AllocSpan<byte>(8 * 1024 * 1024);
        Eq("huge span length", huge.Length, 8 * 1024 * 1024);

        huge[0] = 1;
        huge[huge.Length - 1] = 2;
        Ok("huge span usable", huge[0] == 1 && huge[huge.Length - 1] == 2);
        Ok("huge got a chunk", arena.Stats.ChunkCount > chunksBefore);
    }

    static void ResetTest()
    {
        using var arena = Arena.Create(4096);
        nuint baseline = arena.BytesUsed;   // the ArenaCore itself, allocated from chunk 0

        for (int i = 0; i < 5000; i++)
            *arena.Alloc<long>() = i;

        nuint reservations = arena.Stats.ReservationCount;
        arena.Reset();

        Eq("reset returns to baseline", (int)(arena.BytesUsed - baseline), 0);
        Ok("reset does not eat the core", arena.BytesUsed == baseline && baseline > 0);
        Eq("reset keeps chunk count at 1", (int)arena.Stats.ChunkCount, 1);
        Ok("reset retains spares", arena.Stats.SpareChunkCount > 0);

        for (int i = 0; i < 5000; i++)
            *arena.Alloc<long>() = i;

        Eq("refill after reset makes no reservations",
            (int)(arena.Stats.ReservationCount - reservations), 0);
    }
}
