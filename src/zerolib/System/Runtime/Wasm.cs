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

#if WASM

using System.Runtime.CompilerServices;

namespace System.Runtime
{
    // NativeAOT-LLVM's compiler looks this type up by name and substitutes the
    // method's body (AddInternalWasmSubstitutions); zerolib never calls it.
    internal static class PreciseVirtualUnwindInfo
    {
        private static void GetUnwindInfoViaAbsoluteValueLimit() => Environment.FailFast(null);
    }
}

namespace System
{
    // NativeAOT-LLVM resolves its whole helper table up front, so these must
    // exist even when a program never needs them.
    internal static unsafe class Buffer
    {
        // Copies memory that may hold object references. No GC, so no write
        // barrier: an overlapping-safe byte copy.
        internal static void BulkMoveWithWriteBarrier(ref byte destination, ref byte source, nuint byteCount)
        {
            byte* d = (byte*)Unsafe.AsPointer(ref destination);
            byte* s = (byte*)Unsafe.AsPointer(ref source);
            if (d < s)
            {
                for (nuint i = 0; i < byteCount; i++)
                    d[i] = s[i];
            }
            else
            {
                for (nuint i = byteCount; i > 0; i--)
                    d[i - 1] = s[i - 1];
            }
        }
    }
}

namespace System.Runtime
{
    // Casts zerolib doesn't support (to interface and array types, and exception
    // filters). On other targets the compiler only asks for these when a program
    // uses such a cast; NativeAOT-LLVM asks for all of them.
    internal static unsafe partial class TypeCast
    {
        private static object CheckCastAny(MethodTable* toType, object obj) => Unsupported();
        private static object CheckCastInterface(MethodTable* toType, object obj) => Unsupported();
        private static object CheckCastClassSpecial(MethodTable* toType, object obj) => Unsupported();
        private static object IsInstanceOfAny(MethodTable* toType, object obj) => Unsupported();
        private static object IsInstanceOfInterface(MethodTable* toType, object obj) => Unsupported();
        private static object IsInstanceOfException(MethodTable* toType, object obj) => Unsupported();

        private static object Unsupported()
        {
            Environment.FailFast("zerolib: casts to interface and array types aren't supported");
            return null;
        }
    }
}

namespace Internal.Runtime.CompilerHelpers
{
    partial class ThrowHelpers
    {
        // NativeAOT-LLVM checks for null explicitly instead of relying on a fault.
        static void ThrowNullReferenceException() => System.Environment.FailFast("NullReferenceException");
    }
}

#endif
