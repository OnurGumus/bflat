// Expect: abort under ARENA_CHECKS. Rewinding arena A with arena B's mark.
using System; using Arenas;
unsafe static class P { static int Main() {
    using var a = Arena.Create();
    using var b = Arena.Create();
    ArenaMark markOfB = b.Mark();
    a.Rewind(markOfB);
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
