using System;
using System.Diagnostics;
using Xunit;

namespace bflat.Tests
{
    internal record BflatCompilationResult(string BinaryName, string StdErr, string StdOut)
    {
        public void Run(string expectedOutput = null)
        {
            var psi = new ProcessStartInfo(BinaryName)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            var p = Process.Start(psi);
            if (!p.WaitForExit(30000))
            {
                p.Kill(entireProcessTree: true);
                throw new Exception("Timed out");
            }

            string stdOut = p.StandardOutput.ReadToEnd();
            
            if (expectedOutput != null)
            {
                Assert.Equal(expectedOutput, stdOut);
            }

            Assert.Equal(0, p.ExitCode);
        }

        /// <summary>Run and return stdout, asserting the process exited successfully.</summary>
        public string RunCapturingOutput()
        {
            (string stdOut, int exitCode) = RunCore();
            Assert.Equal(0, exitCode);
            return stdOut;
        }

        /// <summary>
        /// Run expecting the process to abort. Used by the arena negative tests, which prove
        /// the checked build actually detects lifetime misuse.
        /// </summary>
        public string RunExpectingFailure()
        {
            (string stdOut, int exitCode) = RunCore();
            Assert.NotEqual(0, exitCode);
            return stdOut;
        }

        private (string StdOut, int ExitCode) RunCore()
        {
            var psi = new ProcessStartInfo(BinaryName)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            var p = Process.Start(psi);
            if (!p.WaitForExit(30000))
            {
                p.Kill(entireProcessTree: true);
                throw new Exception("Timed out");
            }

            return (p.StandardOutput.ReadToEnd(), p.ExitCode);
        }
    }
}
