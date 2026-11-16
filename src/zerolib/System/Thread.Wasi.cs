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

namespace System.Threading
{
    public static class Thread
    {
        [DllImport("libc"), SuppressGCTransition]
        private static extern unsafe int nanosleep(long* request, long* remaining);

        public static unsafe void Sleep(int delayMs)
        {
            // struct timespec on wasm32: int64 tv_sec, int32 tv_nsec, 4 bytes padding.
            long* request = stackalloc long[2];
            request[0] = delayMs / 1000;
            request[1] = (delayMs % 1000) * 1_000_000L;
            nanosleep(request, null);
        }
    }
}

#endif
