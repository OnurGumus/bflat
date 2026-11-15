// Expect: abort in BOTH builds - bounds checks are deliberately unconditional.
using System; using Arenas;
unsafe static class P { static int Main() {
    using var arena = Arena.Create();
    ArenaSlice<int> slice = arena.AllocSlice<int>(4);
    Console.WriteLine(slice[10]);
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
