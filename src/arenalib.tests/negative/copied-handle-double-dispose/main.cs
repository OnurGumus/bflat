// Expect: abort under ARENA_CHECKS. C# has no move-only structs, so a scope handle can be
// copied; releasing both copies must be caught while the control block is still mapped.
using System; using Arenas;
unsafe static class P { static int Main() {
    using var arena = Arena.Create();
    var rental = arena.Rent();
    var copy = rental;
    rental.Dispose();
    copy.Dispose();
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
