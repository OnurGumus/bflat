using System;
using System.Runtime.CompilerServices;
using Arenas;

// The scope-model regression suite: the seven properties that must hold before anything
// (collections especially) is allowed to depend on promotion between arenas.
unsafe static partial class Program
{
    static void ScopeModelTests()
    {
        PromotionSurvivesDisposal();
        SiblingReleaseOrder();
        SteadyStateReuse();
        MovedFromLease();
        ParentAllocationDuringChild();
        CheckpointStillWorks();
        ChildIndependenceAcrossTypes();
        AccountingAndPoolSharing();
    }

    // 1. Promotion survives disposal - and survives aggressive reuse of the returned chunks.
    static void PromotionSurvivesDisposal()
    {
        using var arena = Arena.Create(16384);

        ArenaString promoted;
        ArenaSlice<int> promotedSlice;

        using (var scratch = arena.RentScratch())
        {
            // Dirty the scratch thoroughly so a stale pointer would be obvious.
            Span<byte> noise = scratch.AllocSpan<byte>(8192);
            noise.Fill(0x5A);

            ArenaString temporary = ArenaString.From(V("promote me"u8), scratch);
            promoted = temporary.CloneTo(arena);

            ArenaSlice<int> temporarySlice = scratch.AllocSlice<int>(64);
            for (int i = 0; i < 64; i++)
                temporarySlice[i] = i * 7;

            promotedSlice = arena.AllocSlice<int>(64);
            temporarySlice.CopyTo(promotedSlice);
        }

        // Now aggressively reuse and overwrite whatever came back to the spare list.
        for (int round = 0; round < 8; round++)
        {
            using var churn = arena.RentScratch();
            Span<byte> overwrite = churn.AllocSpan<byte>(8192);
            overwrite.Fill(0xA5);
        }

        Ok("promoted string intact after chunk reuse", promoted.AsView() == V("promote me"u8));

        bool sliceIntact = true;
        for (int i = 0; i < 64; i++)
            if (promotedSlice[i] != i * 7) sliceIntact = false;
        Ok("promoted slice intact after chunk reuse", sliceIntact);
    }

    // 2. Siblings release in any order.
    static void SiblingReleaseOrder()
    {
        using var arena = Arena.Create(8192);

        // Forward: dispose A before B.
        {
            var a = arena.Rent();
            var b = arena.Rent();

            Span<byte> aData = a.AllocSpan<byte>(512);
            Span<byte> bData = b.AllocSpan<byte>(512);
            aData[0] = 1;
            bData[0] = 2;

            a.Dispose();
            Ok("sibling B intact after A released", bData[0] == 2 && b.IsValid);
            b.Dispose();
        }

        // Reverse: dispose B before A.
        {
            var a = arena.Rent();
            var b = arena.Rent();

            Span<byte> aData = a.AllocSpan<byte>(512);
            aData[0] = 3;

            b.Dispose();
            Ok("sibling A intact after B released", aData[0] == 3 && a.IsValid);
            a.Dispose();
        }

        Eq("no children left", (int)arena.Stats.ActiveChildren, 0);
    }

    // 4. Steady state: thousands of rentals, zero extra page-source reservations.
    static void SteadyStateReuse()
    {
        PageSource counting = Counting();
        int reservesAtStart;

        using (var arena = Arena.Create(counting, 16384, 262144))
        {
            // Warm the spare list with the SAME concurrency the loop uses: two live children
            // at once need two chunks available, not one.
            for (int i = 0; i < 4; i++)
            {
                using var warmScratch = arena.RentScratch();
                warmScratch.AllocSpan<byte>(4096);
                using var warmRental = arena.Rent();
                warmRental.AllocSpan<byte>(4096);
            }

            reservesAtStart = s_reserveCount;

            for (int i = 0; i < 2000; i++)
            {
                using var scratch = arena.RentScratch();
                Span<byte> data = scratch.AllocSpan<byte>(2048);
                data[0] = (byte)i;

                using var nested = arena.Rent();
                nested.AllocSpan<byte>(1024);
            }

            Eq("2000 scope pairs make no new reservations", s_reserveCount - reservesAtStart, 0);
            Eq("all children released", (int)arena.Stats.ActiveChildren, 0);
        }
    }

    // 5. Every operation on a moved-from lease fails deterministically.
    static void MovedFromLease()
    {
        ArenaLease source = ArenaLease.Create(4096);
        ArenaLease target = ArenaLease.Move(ref source);

        Ok("moved-from reports invalid", !source.IsValid);
        Ok("moved-to reports valid", target.IsValid);

        // Release on a moved-from handle is a no-op, not a double free: the target survives.
        source.Release();
        Ok("target survives moved-from release", target.IsValid);

        Span<byte> data = target.AllocSpan<byte>(64);
        data[0] = 8;
        Ok("target still allocates", data[0] == 8);

        target.Release();
        Ok("target released", !target.IsValid);
    }

    // 7. THE regression test for the architectural bug: allocate into the parent while a child
    //    is live, release the child, and confirm the parent's allocation survived.
    static void ParentAllocationDuringChild()
    {
        using var arena = Arena.Create(16384);

        Span<byte> parentBefore = arena.AllocSpan<byte>(256);
        parentBefore[0] = 11;

        Span<byte> parentDuring;
        ArenaString parentString;

        using (var child = arena.Rent())
        {
            Span<byte> childData = child.AllocSpan<byte>(4096);
            childData.Fill(0x33);

            // With a mark-backed child this allocation would land above the child's mark and
            // be destroyed by the release. With independent chunks it is untouched.
            parentDuring = arena.AllocSpan<byte>(256);
            parentDuring[0] = 22;
            parentDuring[255] = 33;

            parentString = ArenaString.From(V("parent side"u8), arena);

            Ok("parent and child memory are disjoint",
                (nuint)Unsafe.AsPointer(ref childData[0]) != (nuint)Unsafe.AsPointer(ref parentDuring[0]));
        }

        // Churn the returned chunks hard.
        for (int i = 0; i < 8; i++)
        {
            using var churn = arena.RentScratch();
            churn.AllocSpan<byte>(4096).Fill(0xCC);
        }

        Ok("parent allocation before child survived", parentBefore[0] == 11);
        Ok("parent allocation during child survived", parentDuring[0] == 22 && parentDuring[255] == 33);
        Ok("parent string during child survived", parentString.AsView() == V("parent side"u8));
    }

    // Checkpoints keep the old mark/rewind behaviour, explicitly.
    static void CheckpointStillWorks()
    {
        using var arena = Arena.Create(8192);

        nuint before = arena.BytesUsed;

        using (var checkpoint = arena.Checkpoint())
        {
            Span<byte> temp = checkpoint.AllocSpan<byte>(4096);
            temp[0] = 1;
            Ok("checkpoint allocates", temp[0] == 1);
            Ok("checkpoint tracks usage", checkpoint.BytesUsed >= 4096);
        }

        Eq("checkpoint rewound the arena", (int)(arena.BytesUsed - before), 0);

        // Nested checkpoints.
        using (var outer = arena.Checkpoint())
        {
            outer.AllocSpan<byte>(128);
            using (var inner = outer.Checkpoint())
                inner.AllocSpan<byte>(128);

            Ok("outer checkpoint survives inner", outer.BytesUsed >= 128);
        }

        Eq("nested checkpoints fully rewound", (int)(arena.BytesUsed - before), 0);
    }

    // Rent/RentScratch/lease all give independent memory.
    static void ChildIndependenceAcrossTypes()
    {
        using var arena = Arena.Create(16384);

        using var rental = arena.Rent();
        using var scratch = arena.RentScratch();
        ArenaLease lease = arena.RentBlock(4096);

        Span<byte> r = rental.AllocSpan<byte>(1024);
        Span<byte> s = scratch.AllocSpan<byte>(1024);
        Span<byte> l = lease.AllocSpan<byte>(1024);
        Span<byte> p = arena.AllocSpan<byte>(1024);

        r[0] = 1; s[0] = 2; l[0] = 3; p[0] = 4;

        Ok("four scopes hold four distinct values",
            r[0] == 1 && s[0] == 2 && l[0] == 3 && p[0] == 4);

        Eq("arena counts its two children", (int)arena.Stats.ActiveChildren, 2);

        lease.Release();
        Ok("values survive lease release", r[0] == 1 && s[0] == 2 && p[0] == 4);
    }

    // Regressions for the two bugs the Kimi audit surfaced: reservation accounting drifting
    // when chunks migrate between scopes, and free chunks stranded in a scope that is done
    // with them.
    static void AccountingAndPoolSharing()
    {
        PageSource counting = Counting();
        int reservesBefore = s_reserveCount;

        using var arena = Arena.Create(counting, 8192, 262144);

        Eq("root counts its own first chunk", (int)arena.Stats.ReservationCount, 1);

        // A child grows well past its borrowed first chunk, so it reserves several itself.
        using (var child = arena.Rent(4096))
        {
            for (int i = 0; i < 20000; i++)
                *child.Alloc<long>() = i;

            Ok("child forced real reservations", s_reserveCount - reservesBefore > 1);
        }

        // Every reservation the child caused must be visible in the root's accounting: the
        // root is what actually still holds that memory once the child hands it back.
        Eq("root accounts for reservations made by its child",
            (int)arena.Stats.ReservationCount, s_reserveCount - reservesBefore);

        int reservesAfterChild = s_reserveCount;

        // A sibling scope gets the first child's chunks, not new ones. (Checked before the
        // parent allocates: chunks the parent takes into its own live chain are legitimately
        // no longer available to anyone else.)
        using (var sibling = arena.Rent(4096))
        {
            for (int i = 0; i < 10000; i++)
                *sibling.Alloc<long>() = i;
        }

        Eq("sibling reuses the pool the first child filled", s_reserveCount - reservesAfterChild, 0);

        // ...and so can the parent itself.
        for (int i = 0; i < 10000; i++)
            *arena.Alloc<long>() = i;

        Eq("parent reuses the chunks its children released", s_reserveCount - reservesAfterChild, 0);
    }
}
