// Expect: abort under ARENA_CHECKS. Disposing an arena that still owns a live child loses
// the child's memory and leaves its handle dangling.
using System; using Arenas;
unsafe static class P { static int Main() {
    var arena = Arena.Create();
    var child = arena.Rent();
    child.AllocSpan<byte>(256);
    arena.Dispose();
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
