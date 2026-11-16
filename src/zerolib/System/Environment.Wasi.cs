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
    // WASI: libc directly (wasi-libc), no libSystem.Native. Calls are bound at
    // link time with --directpinvoke:libc.
    public static unsafe partial class Environment
    {
        [DllImport("libc"), SuppressGCTransition]
        private static extern void abort();

        [DllImport("libc"), SuppressGCTransition]
        private static extern nint write(int fd, byte* buffer, nuint count);

        // wasi-libc's clockid_t is a pointer, so clock_gettime can't be called
        // with a number; this WASI preview 1 function takes the raw id
        // (1 = monotonic) and returns nanoseconds.
        [DllImport("libc", EntryPoint = "__wasi_clock_time_get"), SuppressGCTransition]
        private static extern ushort WasiClockTimeGet(uint clockId, ulong precision, ulong* time);

        public static long TickCount64
        {
            get
            {
                ulong nanoseconds = 0;
                WasiClockTimeGet(1, 1, &nanoseconds);
                return (long)(nanoseconds / 1_000_000);
            }
        }

        public static void FailFast(string message)
        {
            if (message is not null)
            {
                for (int i = 0; i < message.Length; i++)
                {
                    byte b = message[i] <= 0x7F ? (byte)message[i] : (byte)'?';
                    write(2, &b, 1);
                }
                byte newline = (byte)'\n';
                write(2, &newline, 1);
            }
            abort();
        }
    }
}

#endif
