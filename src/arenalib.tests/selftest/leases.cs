using System;
using Arenas;

// Transferable ownership: ArenaLease and RcArenaLease.
//
// Rentals are lexical - they share the parent's chunks and rewind on scope exit. That is
// right for nested synchronous work and wrong for anything that outlives the call which
// started it. Leases own their chunks outright, so they can be stored in a context struct
// and handed to a callback or a worker.
unsafe static partial class Program
{
    // The classic C shape: everything the operation needs, reachable from one void*.
    struct Request
    {
        public ArenaLease Lease;
        public int Value;
        public int Result;
    }

    static int s_callbackResult;

    static void OnRequestComplete(void* context)
    {
        Request* request = (Request*)context;
        request->Result = request->Value * 2;
        s_callbackResult = request->Result;

        // The Request itself lives inside the lease, so copy the lease out before releasing:
        // after Release the memory holding `request` is gone.
        ArenaLease lease = request->Lease;
        lease.Release();
    }

    static void LeaseTests()
    {
        CallbackWithContext();
        LeaseIndependence();
        LeaseMoveSemantics();
        LeaseRecycle();
        SharedLeaseTests();
    }

    static void CallbackWithContext()
    {
        using var server = Arena.Create();

        s_callbackResult = 0;

        ArenaLease lease = server.RentBlock();
        Request* request = lease.Alloc<Request>();
        request->Value = 21;
        request->Lease = lease;    // storable precisely because ArenaLease is not a ref struct

        delegate*<void*, void> completion = &OnRequestComplete;
        completion(request);

        Eq("callback saw its context", s_callbackResult, 42);

        // The lease released itself inside the callback; the server arena is untouched.
        Ok("server arena still valid", server.IsValid);
    }

    static void LeaseIndependence()
    {
        using var parent = Arena.Create(8192);

        ArenaMark mark = parent.Mark();
        parent.AllocSpan<byte>(1024);

        ArenaLease lease = parent.RentBlock(4096);
        Span<byte> leased = lease.AllocSpan<byte>(2048);
        leased[0] = 7;
        leased[2047] = 9;

        // Rewinding the parent must not touch the lease: it owns different memory.
        parent.Rewind(mark);

        Ok("lease survives parent rewind", lease.IsValid);
        Ok("lease contents survive", leased[0] == 7 && leased[2047] == 9);

        // ...and the lease is not counted in the parent's usage.
        Eq("parent rewound to mark", (int)(parent.BytesUsed - mark.BytesUsed), 0);

        lease.Release();
        Ok("released lease is invalid", !lease.IsValid);
        Ok("parent unaffected by release", parent.IsValid);
    }

    static void LeaseMoveSemantics()
    {
        ArenaLease original = ArenaLease.Create(4096);
        Span<byte> data = original.AllocSpan<byte>(64);
        data[0] = 5;

        ArenaLease moved = ArenaLease.Move(ref original);

        Ok("moved-from lease is invalid", !original.IsValid);
        Ok("moved-to lease is valid", moved.IsValid);
        Ok("memory survived the move", data[0] == 5);

        // Releasing the moved-from handle is a no-op, not a double free.
        original.Release();
        Ok("moved-to still valid after source release", moved.IsValid);

        moved.Release();
        Ok("both released", !moved.IsValid && !original.IsValid);
    }

    static void LeaseRecycle()
    {
        ArenaLease lease = ArenaLease.Create(4096);

        for (int i = 0; i < 5000; i++)
            *lease.Alloc<long>() = i;

        nuint reservations = lease.Stats.ReservationCount;
        nuint used = lease.BytesUsed;
        Ok("lease accumulated bytes", used > 0);

        lease.Recycle();
        Ok("recycle drops usage", lease.BytesUsed < used);
        Eq("recycle keeps one chunk", (int)lease.Stats.ChunkCount, 1);

        for (int i = 0; i < 5000; i++)
            *lease.Alloc<long>() = i;

        Eq("recycled lease makes no new reservations",
            (int)(lease.Stats.ReservationCount - reservations), 0);

        // A lease can host nested lexical scopes just like a root arena.
        using (var request = lease.Rent())
        {
            // Build and materialize at the same depth: allocating from the lease itself here
            // would be the same above-the-mark hazard as promoting out of a scratch.
            var builder = ArenaStringBuilder.Create(request);
            builder.Append(V("in a lease"u8));
            Ok("builder inside a lease", builder.ToArenaString(request).AsView() == V("in a lease"u8));
        }

        lease.Release();
    }

    // A PageSource wrapper that counts reservations and releases. This is how we observe
    // destruction without touching freed memory - and it exercises the pluggable seam.
    static PageSource s_backing;
    static int s_reserveCount;
    static int s_releaseCount;

    static void* CountingReserve(nuint size)
    {
        s_reserveCount++;
        return s_backing.Reserve(size);
    }

    static void CountingRelease(void* address, nuint size)
    {
        s_releaseCount++;
        s_backing.Release(address, size);
    }

    static PageSource Counting()
    {
        s_backing = PageSource.Default;
        return new PageSource(&CountingReserve, &CountingRelease, s_backing.Granularity, s_backing.Flags);
    }

    static void SharedLeaseTests()
    {
        PageSource counting = Counting();

        var shared = RcArenaLease.Create(counting, 4096, 65536);
        Eq("shared starts at one reference", shared.ReferenceCount, 1);

        Span<byte> data = shared.AllocSpan<byte>(128);
        data[0] = 3;

        // Three outstanding operations over one context: a read, a timeout, a cancellation.
        var readOperation = shared.Retain();
        var timeoutOperation = shared.Retain();
        Eq("three outstanding references", shared.ReferenceCount, 3);

        int releasesBefore = s_releaseCount;

        // They complete in arbitrary order; nothing is freed until the last one drops.
        timeoutOperation.Release();
        Eq("timeout completed", shared.ReferenceCount, 2);
        Ok("still alive after timeout", shared.IsValid && data[0] == 3);
        Eq("nothing freed yet", s_releaseCount - releasesBefore, 0);

        readOperation.Release();
        Eq("read completed", shared.ReferenceCount, 1);
        Ok("still alive after read", shared.IsValid);
        Eq("still nothing freed", s_releaseCount - releasesBefore, 0);

        // NOTE: we deliberately do NOT keep a copy and probe IsValid after this. The
        // ArenaCore lives inside the memory being unmapped, so a stale copy is a dangling
        // pointer - exactly the hazard Move() exists to avoid. Observe via the page source.
        shared.Release();
        Ok("memory returned when the last reference drops", s_releaseCount > releasesBefore);
        Ok("releasing handle is self-invalidating", !shared.IsValid);

        // Adopting an exclusive lease into shared ownership consumes it.
        int beforeAdopt = s_releaseCount;
        ArenaLease exclusive = ArenaLease.Create(counting, 4096, 65536);
        var adopted = RcArenaLease.FromExclusive(ref exclusive);
        Ok("exclusive lease consumed", !exclusive.IsValid);
        Eq("adopted starts at one", adopted.ReferenceCount, 1);

        adopted.Release();
        Ok("adopted memory returned", s_releaseCount > beforeAdopt);

        // Reserve/release should balance across everything this function created.
        Ok("counting page source saw traffic", s_reserveCount > 0);
    }
}
