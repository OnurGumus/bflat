// Expect: survives in BOTH builds. Dispose is idempotent by design.
using System; using Arenas;
unsafe static class P { static int Main() {
    var arena = Arena.Create();
    arena.Dispose();
    arena.Dispose();
    Console.WriteLine("survived double dispose");
    return 0;
} }
