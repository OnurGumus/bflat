// Expect: abort under ARENA_CHECKS (clean message). Release: undefined (null deref).
using System; using Arenas;
unsafe static class P { static int Main() {
    var arena = Arena.Create();
    arena.Dispose();
    long* p = arena.Alloc<long>();
    *p = 1;
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
