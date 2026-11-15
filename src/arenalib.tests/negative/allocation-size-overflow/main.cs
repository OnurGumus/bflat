// Expect: clean abort in BOTH builds ("arena: out of memory"), not silent corruption.
//
// Rounding a near-nuint.MaxValue request up to the page granularity wraps, which used to turn
// an absurd request into a tiny mapping carrying a chunk header that claimed ~2^64 usable
// bytes - after which every bump allocation "succeeded" and wrote off the end of the mapping.
using System; using Arenas;
unsafe static class P { static int Main() {
    using var arena = Arena.Create();
    void* p = arena.AllocRaw((nuint)0xFFFFFFFFFFFFF000UL, 16);
    ((byte*)p)[0] = 1;
    Console.WriteLine("NOT DETECTED");
    return 0;
} }
