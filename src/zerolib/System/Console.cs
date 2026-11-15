// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace System
{
    public enum ConsoleColor
    {
        Black, DarkBlue, DarkGreen, DarkCyan, DarkRed, DarkMagenta, DarkYellow,
        Gray, DarkGray, Blue, Green, Cyan, Red, Magenta, Yellow, White
    }

    public enum ConsoleKey
    {
        Escape = 27,
        LeftArrow = 37,
        UpArrow = 38,
        RightArrow = 39,
        DownArrow = 40,
    }

    public readonly struct ConsoleKeyInfo
    {
        public ConsoleKeyInfo(char keyChar, ConsoleKey key, bool shift, bool alt, bool control)
        {
            Key = key;
        }

        public readonly ConsoleKey Key;
    }

    public static unsafe partial class Console
    {
        public static void WriteLine(string s)
        {
            for (int i = 0; i < s.Length; i++)
                Console.Write(s[i]);
#if WINDOWS || UEFI
            Console.Write('\r');
#endif
            Console.Write('\n');
        }

        public static void WriteLine(int i)
        {
            const int BufferSize = 16;
            char* pBuffer = stackalloc char[BufferSize];

            // Build the digits from the magnitude as a uint. Doing the modulo on a negative
            // int yields negative digits ('0' + -5 == '+'), and negating int.MinValue as an
            // int overflows back to itself.
            uint magnitude;
            if (i < 0)
            {
                Write('-');
                magnitude = 0u - (uint)i;
            }
            else
            {
                magnitude = (uint)i;
            }

            char* pEnd = &pBuffer[BufferSize - 1];
            char* pCurrent = pEnd;
            do
            {
                *(pCurrent--) = (char)((magnitude % 10) + '0');
                magnitude /= 10;
            } while (magnitude != 0);

            // The loop leaves pCurrent one slot below the most significant digit; without
            // this the first character written is whatever was on the stack.
            pCurrent++;

            while (pCurrent <= pEnd)
                Write(*(pCurrent++));

#if WINDOWS || UEFI
            Console.Write('\r');
#endif
            Console.Write('\n');
        }
    }
}
