using System;
using System.Runtime.CompilerServices;
using Arenas;

// Phase 4: collections.
unsafe static partial class Program
{
    struct Item { public int Id; public long Weight; }

    static void CollectionTests()
    {
        ListTests();
        FixedListTests();
        ChunkedListTests();
        MapTests();
        SetTests();
        QueueTests();
        StackTests();
        BitSetTests();
        PoolTests();
        SortingTests();
        CrossScopeTests();
    }

    static void ListTests()
    {
        using var arena = Arena.Create();

        var list = ArenaList<int>.Create(arena);
        Ok("list starts empty", list.IsEmpty && list.Count == 0);

        for (int i = 0; i < 1000; i++)
            list.Add(i * 3);

        Eq("list count", list.Count, 1000);
        Eq("list[0]", list[0], 0);
        Eq("list[999]", list[999], 2997);
        Ok("list capacity covers count", list.Capacity >= 1000);

        bool contents = true;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != i * 3) contents = false;
        Ok("list contents after growth", contents);

        // foreach with ref access
        int sum = 0;
        foreach (ref int value in list)
            sum += value;
        Eq("list foreach sum", sum, 999 * 1000 / 2 * 3);

        // Mutation through the enumerator's ref.
        foreach (ref int value in list)
            value = 1;
        Eq("list mutated via ref", list[500], 1);

        // AsSpan aliases the same memory.
        Span<int> span = list.AsSpan();
        Eq("AsSpan length", span.Length, 1000);
        span[7] = 42;
        Eq("AsSpan aliases", list[7], 42);

        // Extend-in-place: while the list is the newest allocation, growth must not move it.
        var grow = ArenaList<long>.Create(arena, 4);
        grow.Add(1);
        void* before = Unsafe.AsPointer(ref grow[0]);
        for (int i = 0; i < 500; i++)
            grow.Add(i);
        void* after = Unsafe.AsPointer(ref grow[0]);
        Ok("growth extended in place", before == after);
        Eq("extended list count", grow.Count, 501);

        // ...and when something else is allocated in between, it relocates but stays correct.
        var interleavedA = ArenaList<int>.Create(arena, 2);
        interleavedA.Add(10);
        var interleavedB = ArenaList<int>.Create(arena, 2);
        interleavedB.Add(20);
        for (int i = 0; i < 100; i++)
            interleavedA.Add(i);
        Eq("relocated list head", interleavedA[0], 10);
        Eq("relocated list tail", interleavedA[100], 99);
        Eq("neighbour untouched", interleavedB[0], 20);

        // Removal
        var removal = ArenaList<int>.Create(arena, 8);
        for (int i = 0; i < 8; i++)
            removal.Add(i);

        removal.RemoveAt(2);
        Eq("RemoveAt count", removal.Count, 7);
        Eq("RemoveAt shifted", removal[2], 3);
        Eq("RemoveAt preserved order", removal[6], 7);

        removal.RemoveAtSwapBack(0);
        Eq("RemoveAtSwapBack count", removal.Count, 6);
        Eq("RemoveAtSwapBack moved last", removal[0], 7);

        Eq("RemoveLast", removal.RemoveLast(), 6);

        // AddRange
        var ranged = ArenaList<int>.Create(arena);
        Span<int> source = arena.AllocSpan<int>(5);
        for (int i = 0; i < 5; i++)
            source[i] = i + 100;
        ranged.AddRange(source);
        Eq("AddRange count", ranged.Count, 5);
        Eq("AddRange content", ranged[4], 104);

        // Reference types are fine: T is unconstrained.
        var strings = ArenaList<ArenaString>.Create(arena, 4);
        strings.Add(ArenaString.From(V("alpha"u8), arena));
        strings.Add(ArenaString.From(V("beta"u8), arena));
        Ok("list of arena strings", strings[1].AsView() == V("beta"u8));

        var uninit = ArenaList<Item>.Create(arena, 2);
        ref Item slot = ref uninit.AddUninitialized();
        slot.Id = 5;
        slot.Weight = 9;
        Ok("AddUninitialized fills in place", uninit[0].Id == 5 && uninit[0].Weight == 9);

        list.Clear();
        Ok("list cleared", list.IsEmpty);
    }

    static void FixedListTests()
    {
        using var arena = Arena.Create();

        var fixedList = FixedArenaList<int>.Create(arena, 4);
        Eq("fixed capacity", fixedList.Capacity, 4);

        for (int i = 0; i < 4; i++)
            Ok("fixed TryAdd succeeds", fixedList.TryAdd(i));

        Ok("fixed is full", fixedList.IsFull);
        Ok("fixed TryAdd fails when full", !fixedList.TryAdd(99));
        Eq("fixed count", fixedList.Count, 4);
        Eq("fixed content", fixedList[3], 3);

        // Wrapping existing memory allocates nothing.
        Span<int> buffer = arena.AllocSpan<int>(3);
        var wrapped = FixedArenaList<int>.FromSpan(buffer);
        wrapped.Add(7);
        Eq("FromSpan writes through", buffer[0], 7);
        Eq("FromSpan capacity", wrapped.Capacity, 3);
    }

    static void ChunkedListTests()
    {
        using var arena = Arena.Create();

        var chunked = ChunkedArenaList<long>.Create(arena, 64);
        chunked.Add(11);

        void* first = Unsafe.AsPointer(ref chunked[0]);

        for (int i = 1; i < 10000; i++)
            chunked.Add(i);

        Eq("chunked count", chunked.Count, 10000);
        Ok("chunked never relocates", first == Unsafe.AsPointer(ref chunked[0]));
        Eq("chunked head", (int)chunked[0], 11);
        Eq("chunked tail", (int)chunked[9999], 9999);
        Ok("chunked allocated many chunks", chunked.ChunkCount > 1);

        bool contents = true;
        for (int i = 1; i < chunked.Count; i++)
            if (chunked[i] != i) contents = false;
        Ok("chunked contents", contents);

        int count = 0;
        foreach (ref long value in chunked)
            count++;
        Eq("chunked foreach", count, 10000);

        ArenaSlice<long> flattened = chunked.ToSlice(arena);
        Eq("ToSlice length", flattened.Length, 10000);
        Eq("ToSlice tail", (int)flattened[9999], 9999);
    }

    static void MapTests()
    {
        using var arena = Arena.Create();

        var map = ArenaMap<int, int>.Create(arena, 16, Hashers.Int32Hash, Hashers.Int32Equals);
        Ok("map starts empty", map.IsEmpty);

        for (int i = 0; i < 1000; i++)
            map.Set(i, i * i);

        Eq("map count", map.Count, 1000);

        bool lookups = true;
        for (int i = 0; i < 1000; i++)
        {
            if (!map.TryGetValue(i, out int value) || value != i * i)
                lookups = false;
        }
        Ok("map lookups after rehash", lookups);

        Ok("map miss", !map.TryGetValue(5000, out _));
        Ok("map ContainsKey", map.ContainsKey(999));

        // Overwrite
        map.Set(10, -1);
        Ok("map overwrite", map.TryGetValue(10, out int overwritten) && overwritten == -1);
        Eq("overwrite does not grow count", map.Count, 1000);

        // TryAdd respects existing keys
        Ok("TryAdd rejects duplicate", !map.TryAdd(10, 5));
        Ok("TryAdd accepts new", map.TryAdd(12345, 7));

        // GetOrAddRef: the counter idiom, one probe.
        var counts = ArenaMap<int, int>.Create(arena, 8, Hashers.Int32Hash, Hashers.Int32Equals);
        for (int i = 0; i < 300; i++)
        {
            ref int slot = ref counts.GetOrAddRef(i % 10, out bool added);
            slot++;
        }
        Eq("counter map size", counts.Count, 10);
        Ok("counter map values", counts.TryGetValue(3, out int c3) && c3 == 30);

        // Removal with backward shift: hammer a small table so probe chains actually form.
        var churn = ArenaMap<int, int>.Create(arena, 64, Hashers.Int32Hash, Hashers.Int32Equals);
        for (int i = 0; i < 40; i++)
            churn.Set(i, i);

        for (int i = 0; i < 40; i += 2)
            Ok("remove existing", churn.Remove(i));

        Eq("count after removals", churn.Count, 20);

        bool survivors = true;
        for (int i = 1; i < 40; i += 2)
            if (!churn.TryGetValue(i, out int v) || v != i) survivors = false;
        Ok("odd keys survive removal", survivors);

        bool removedGone = true;
        for (int i = 0; i < 40; i += 2)
            if (churn.ContainsKey(i)) removedGone = false;
        Ok("even keys are gone", removedGone);

        Ok("remove missing returns false", !churn.Remove(1000));

        // Re-add after removal must reuse the shifted slots cleanly.
        for (int i = 0; i < 40; i += 2)
            churn.Set(i, i * 10);
        Eq("count after re-add", churn.Count, 40);
        Ok("re-added value", churn.TryGetValue(8, out int readded) && readded == 80);

        // Enumeration visits every live entry exactly once.
        int visited = 0;
        long keySum = 0;
        foreach (var entry in churn)
        {
            visited++;
            keySum += entry.CurrentKey;
        }
        Eq("map enumeration count", visited, 40);
        Eq("map enumeration key sum", (int)keySum, 39 * 40 / 2);

        churn.Clear();
        Ok("map cleared", churn.IsEmpty && !churn.ContainsKey(8));

        // String keys.
        var byName = ArenaMap<ArenaString, int>.Create(
            arena, 16, Hashers.ArenaStringHash, Hashers.ArenaStringEquals);

        byName.Set(ArenaString.From(V("one"u8), arena), 1);
        byName.Set(ArenaString.From(V("two"u8), arena), 2);
        byName.Set(ArenaString.From(V("one"u8), arena), 11);   // distinct instance, equal content

        Eq("string-keyed map dedupes by content", byName.Count, 2);
        Ok("string-keyed lookup",
            byName.TryGetValue(ArenaString.From(V("one"u8), arena), out int one) && one == 11);
    }

    static void SetTests()
    {
        using var arena = Arena.Create();

        var set = ArenaSet<int>.Create(arena, 16, Hashers.Int32Hash, Hashers.Int32Equals);

        Ok("set add is new", set.Add(5));
        Ok("set add duplicate", !set.Add(5));
        Eq("set count", set.Count, 1);
        Ok("set contains", set.Contains(5));
        Ok("set does not contain", !set.Contains(6));

        for (int i = 0; i < 500; i++)
            set.Add(i);

        Eq("set count after bulk", set.Count, 500);
        Ok("set remove", set.Remove(250));
        Ok("set removed is gone", !set.Contains(250));
        Eq("set count after remove", set.Count, 499);
    }

    static void QueueTests()
    {
        using var arena = Arena.Create();

        var queue = ArenaQueue<int>.Create(arena, 4);
        Ok("queue starts empty", queue.IsEmpty);
        Ok("dequeue on empty", !queue.TryDequeue(out _));

        for (int i = 0; i < 4; i++)
            queue.Enqueue(i);

        Ok("queue peek", queue.Peek() == 0);

        // Wrap the ring: dequeue two, enqueue two.
        queue.TryDequeue(out int a);
        queue.TryDequeue(out int b);
        Ok("fifo order", a == 0 && b == 1);
        queue.Enqueue(4);
        queue.Enqueue(5);
        Eq("queue count after wrap", queue.Count, 4);

        // Force growth while wrapped: the copy must linearize correctly.
        queue.Enqueue(6);
        Eq("queue count after growth", queue.Count, 5);

        bool order = true;
        for (int expected = 2; expected <= 6; expected++)
        {
            if (!queue.TryDequeue(out int value) || value != expected)
                order = false;
        }
        Ok("fifo order preserved across wrap and growth", order);
        Ok("queue drained", queue.IsEmpty);
    }

    static void StackTests()
    {
        using var arena = Arena.Create();

        var stack = ArenaStack<int>.Create(arena, 4);
        Ok("stack starts empty", stack.IsEmpty);
        Ok("pop on empty", !stack.TryPop(out _));

        for (int i = 0; i < 100; i++)
            stack.Push(i);

        Eq("stack count", stack.Count, 100);
        Ok("stack peek", stack.Peek() == 99);

        bool order = true;
        for (int expected = 99; expected >= 0; expected--)
        {
            if (!stack.TryPop(out int value) || value != expected)
                order = false;
        }
        Ok("lifo order", order);
        Ok("stack drained", stack.IsEmpty);
    }

    static void BitSetTests()
    {
        using var arena = Arena.Create();

        var bits = ArenaBitSet.Create(arena, 200);
        Eq("bitset length", bits.Length, 200);
        Eq("bitset starts clear", bits.PopCount(), 0);
        Eq("no set bit", bits.FindFirstSet(), -1);

        bits[0] = true;
        bits[63] = true;
        bits[64] = true;
        bits[199] = true;

        Eq("bitset popcount", bits.PopCount(), 4);
        Ok("bitset get", bits[0] && bits[63] && bits[64] && bits[199]);
        Ok("bitset unset stays clear", !bits[1] && !bits[62] && !bits[198]);

        Eq("find first", bits.FindFirstSet(), 0);
        Eq("find next after 0", bits.FindNextSet(1), 63);
        Eq("find next across word", bits.FindNextSet(64), 64);
        Eq("find last", bits.FindNextSet(65), 199);
        Eq("find past end", bits.FindNextSet(200), -1);

        bits[63] = false;
        Eq("popcount after clear", bits.PopCount(), 3);

        // SetAll must not count the padding bits past Length.
        bits.SetAll(true);
        Eq("SetAll counts only real bits", bits.PopCount(), 200);

        bits.Clear();
        Eq("Clear", bits.PopCount(), 0);
    }

    static void PoolTests()
    {
        using var arena = Arena.Create();

        var pool = ArenaPool<Item>.Create(arena, 8);

        Item first = default;
        first.Id = 1;
        ArenaHandle<Item> firstHandle = pool.Add(first);

        void* firstAddress = Unsafe.AsPointer(ref pool[firstHandle]);

        // Force many slabs; addresses must not move.
        var handles = ArenaList<ArenaHandle<Item>>.Create(arena, 600);
        for (int i = 0; i < 600; i++)
        {
            Item item = default;
            item.Id = i;
            item.Weight = i * 2;
            handles.Add(pool.Add(item));
        }

        Eq("pool count", pool.Count, 601);
        Ok("pool addresses are stable", firstAddress == Unsafe.AsPointer(ref pool[firstHandle]));
        Eq("first item intact", pool[firstHandle].Id, 1);

        bool resolved = true;
        for (int i = 0; i < 600; i++)
        {
            if (pool[handles[i]].Id != i || pool[handles[i]].Weight != i * 2)
                resolved = false;
        }
        Ok("all handles resolve", resolved);

        // Mutate through the handle.
        pool[handles[10]].Id = -5;
        Eq("mutation through handle", pool[handles[10]].Id, -5);

        // Freeing invalidates the handle; the slot is reused with a new stamp.
        ArenaHandle<Item> victim = handles[100];
        Ok("remove succeeds", pool.Remove(victim));
        Ok("stale handle is invalid", !pool.IsValid(victim));
        Ok("stale handle TryGet fails", !pool.TryGet(victim, out _));
        Ok("double remove fails", !pool.Remove(victim));
        Eq("pool count after remove", pool.Count, 600);

        Item replacement = default;
        replacement.Id = 777;
        ArenaHandle<Item> reused = pool.Add(replacement);
        Ok("freed slot is reused", pool[reused].Id == 777);
        Ok("old handle still invalid after reuse", !pool.IsValid(victim));
        Ok("new handle differs from old", reused != victim);

        Ok("default handle is invalid", !pool.IsValid(ArenaHandle<Item>.None));
    }

    static void SortingTests()
    {
        using var arena = Arena.Create();

        // Already sorted, reversed, all-equal, and a pseudo-random spread.
        SortCase(arena, 0);
        SortCase(arena, 1);
        SortCase(arena, 2);
        SortCase(arena, 3);

        // Small inputs take the insertion path; big ones exercise quicksort and the depth bound.
        var big = ArenaList<int>.Create(arena, 5000);
        uint state = 12345;
        for (int i = 0; i < 5000; i++)
        {
            state = state * 1664525u + 1013904223u;
            big.Add((int)(state >> 8));
        }

        big.Sort(Hashers.Int32Compare);
        Ok("large sort is ordered", Sorting.IsSorted(big.AsSpan(), Hashers.Int32Compare));

        // Binary search over the sorted result.
        int probe = big[1234];
        int found = Sorting.BinarySearch(big.AsSpan(), probe, Hashers.Int32Compare);
        Ok("binary search finds element", found >= 0 && big[found] == probe);

        int missing = int.MinValue;
        Eq("binary search miss", Sorting.BinarySearch(big.AsSpan(), missing, Hashers.Int32Compare), -1);

        // Sorting arena strings by content.
        var names = ArenaList<ArenaString>.Create(arena, 4);
        names.Add(ArenaString.From(V("pear"u8), arena));
        names.Add(ArenaString.From(V("apple"u8), arena));
        names.Add(ArenaString.From(V("fig"u8), arena));
        names.Sort(Hashers.ArenaStringCompare);
        Ok("sorted strings", names[0].AsView() == V("apple"u8) && names[2].AsView() == V("pear"u8));
    }

    static void SortCase(Arena arena, int shape)
    {
        const int Count = 200;
        var list = ArenaList<int>.Create(arena, Count);

        for (int i = 0; i < Count; i++)
        {
            int value = shape switch
            {
                0 => i,
                1 => Count - i,
                2 => 7,
                _ => (i * 37) % Count,
            };
            list.Add(value);
        }

        list.Sort(Hashers.Int32Compare);
        Ok("sort case ordered", Sorting.IsSorted(list.AsSpan(), Hashers.Int32Compare));
        Eq("sort case preserved count", list.Count, Count);
    }

    // Collections may now be built in a child scope and promoted, because children own their
    // chunks. This is what the scope redesign was for.
    static void CrossScopeTests()
    {
        using var arena = Arena.Create();

        ArenaList<int> promoted;
        ArenaString built;

        using (var scratch = arena.RentScratch())
        {
            var temporary = ArenaList<int>.Create(scratch, 8);
            for (int i = 0; i < 500; i++)
                temporary.Add(i);

            promoted = temporary.CloneTo(arena);

            var builder = ArenaStringBuilder.Create(scratch);
            builder.Append(V("count="u8));
            builder.AppendInt32(temporary.Count);
            built = builder.ToArenaString(arena);
        }

        // Churn the returned chunks hard before checking.
        for (int i = 0; i < 8; i++)
        {
            using var churn = arena.RentScratch();
            churn.AllocSpan<byte>(8192).Fill(0xA5);
        }

        Eq("promoted list count", promoted.Count, 500);
        Eq("promoted list head", promoted[0], 0);
        Eq("promoted list tail", promoted[499], 499);
        Ok("promoted builder output", built.AsView() == V("count=500"u8));
    }
}
