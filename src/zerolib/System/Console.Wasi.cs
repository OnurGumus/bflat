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

#if WASI

using System.Runtime.InteropServices;

namespace System
{
    // WASI: stdout through libc write(); no terminal control or key input.
    public static unsafe partial class Console
    {
        public static unsafe string Title
        {
            set
            {
                _ = value;
            }
        }

        public static unsafe bool CursorVisible
        {
            set
            {
                _ = value;
            }
        }

        [DllImport("libc"), SuppressGCTransition]
        private static extern nint write(int fd, byte* buffer, nuint count);

        public static void Write(char c)
        {
            byte* utf8 = stackalloc byte[3];
            int length;
            if (c <= 0x7F)
            {
                utf8[0] = (byte)c;
                length = 1;
            }
            else if (c <= 0x7FF)
            {
                utf8[0] = (byte)(0xC0 | (c >> 6));
                utf8[1] = (byte)(0x80 | (c & 0x3F));
                length = 2;
            }
            else
            {
                utf8[0] = (byte)(0xE0 | (c >> 12));
                utf8[1] = (byte)(0x80 | ((c >> 6) & 0x3F));
                utf8[2] = (byte)(0x80 | (c & 0x3F));
                length = 3;
            }
            write(1, utf8, (nuint)length);
        }
    }
}

#endif
