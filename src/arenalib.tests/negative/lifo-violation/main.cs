// Expect: abort under ARENA_CHECKS.
// Rentals and scratches are independent now, so they no longer constrain the parent. The one
// remaining mark-backed scope is ArenaCheckpoint, which carries the hard rule that nothing
// else may allocate into the arena while it is open - otherwise that allocation lands above
// the mark and is destroyed by the rewind.
using System; using Arenas;
unsafe static class P { static int Main() {
    using var arena = Arena.Create();
    using var checkpoint = arena.Checkpoint();
    long* p = arena.Alloc<long>();
    *p = 1;
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
