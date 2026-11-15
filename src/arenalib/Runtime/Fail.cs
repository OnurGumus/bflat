using System;

namespace Arenas
{
    /// <summary>
    /// Fatal-error reporting. There are no exceptions under zerolib, so every
    /// unrecoverable condition ends here.
    /// </summary>
    public static class Fail
    {
        /// <summary>Print a message and abort the process.</summary>
        public static void Msg(string message)
        {
            // Environment.FailFast throws its message away on Linux (it just calls
            // SystemNative_Abort), so print it ourselves first.
            if (message != null)
                Console.WriteLine(message);

            Environment.FailFast(null);
        }

        /// <summary>Abort if <paramref name="condition"/> is false.</summary>
        public static void If(bool condition, string message)
        {
            if (condition)
                Msg(message);
        }
    }
}
