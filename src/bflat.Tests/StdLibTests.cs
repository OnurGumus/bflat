using Xunit;

namespace bflat.Tests
{
    public class StdLibTests
    {
        private const string HelloSource =
            "class Program { static void Main() { System.Console.Write(\"Hello\"); } }";

        [Theory]
        [InlineData("")]                 // --stdlib:dotnet, the default
        [InlineData("--stdlib:zero")]
        [InlineData("--stdlib:arena")]
        public void HelloWorld(string stdlib)
        {
            new BflatCompilation().Build(HelloSource, stdlib).Run("Hello");
        }

        /// <summary>
        /// Proves ref/arenalib.dll and lib/&lt;os&gt;/&lt;arch&gt;/arenalib.dll both resolve, by using a
        /// type that only exists in arenalib.
        /// </summary>
        [Fact]
        public void ArenaLibIsReferenceable()
        {
            const string source = @"
using System;
using Arenas;
unsafe class Program
{
    static void Main()
    {
        using var arena = Arena.Create();
        Span<int> values = arena.AllocSpan<int>(4);
        values[0] = 40;
        values[3] = 2;
        Console.WriteLine(values[0] + values[3]);
    }
}";
            new BflatCompilation().Build(source, "--stdlib:arena").Run("42" + System.Environment.NewLine);
        }

        /// <summary>
        /// The release build must not pay for the lifetime checks: allocating from a parent
        /// while a rental is live is undetected, and the program runs to completion.
        /// </summary>
        [Fact]
        public void ArenaChecksAreOffByDefault()
        {
            new BflatCompilation().Build(LifoViolationSource, "--stdlib:arena").Run("done" + System.Environment.NewLine);
        }

        /// <summary>...and --arena-checks turns exactly that case into a deterministic abort.</summary>
        [Fact]
        public void ArenaChecksDetectLifoViolation()
        {
            string output = new BflatCompilation()
                .Build(LifoViolationSource, "--stdlib:arena --arena-checks")
                .RunExpectingFailure();

            Assert.Contains("LIFO violation", output);
        }

        private const string LifoViolationSource = @"
using System;
using Arenas;
unsafe class Program
{
    static void Main()
    {
        using var arena = Arena.Create();
        var rental = arena.Rent();
        *arena.Alloc<long>() = 1;   // allocating from the parent while a rental is live
        Console.WriteLine(""done"");
    }
}";

        /// <summary>
        /// arenalib is full of per-OS #if blocks, so make sure every target still compiles.
        /// Compile-only, so this needs no emulator.
        /// </summary>
        [Theory]
        [InlineData("linux", "x64")]
        [InlineData("linux", "arm64")]
        [InlineData("windows", "x64")]
        [InlineData("windows", "arm64")]
        [InlineData("windows", "x86")]
        [InlineData("uefi", "x64")]
        [InlineData("uefi", "arm64")]
        public void ArenaLibCompilesForEveryTarget(string os, string arch)
        {
            const string source = @"
using System;
using Arenas;
unsafe class Program
{
    static void Main()
    {
        using var arena = Arena.Create();
        using var request = arena.Rent();
        using var scratch = request.RentScratch();
        ArenaSlice<byte> buffer = scratch.AllocSlice<byte>(4096);
        Console.WriteLine(buffer.Length);
    }
}";
            new BflatCompilation().Build(source, $"--stdlib:arena --os:{os} --arch:{arch} -c");
        }
    }
}
