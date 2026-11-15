// Expect: abort under ARENA_CHECKS. The slice was allocated before the rewind.
using System; using Arenas;
unsafe static class P { static int Main() {
    using var arena = Arena.Create();
    ArenaMark mark = arena.Mark();
    ArenaSlice<int> slice = arena.AllocSlice<int>(16);
    slice[0] = 5;
    arena.Rewind(mark);
    Console.WriteLine(slice[0]);
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
