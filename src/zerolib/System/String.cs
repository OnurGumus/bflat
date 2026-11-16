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

using System.Runtime;
using System.Runtime.CompilerServices;

namespace System
{
    public sealed class String
    {
        // The layout of the string type is a contract with the compiler, and so
        // are the field names: the compiler's static preinitializer looks up
        // _stringLength and _firstChar by name when it builds strings.
        private readonly int _stringLength;
        private char _firstChar;

        public int Length => _stringLength;

        [IndexerName("Chars")]
        public unsafe char this[int index]
        {
            [System.Runtime.CompilerServices.Intrinsic]
            get
            {
                return System.Runtime.CompilerServices.Unsafe.Add(ref _firstChar, index);
            }
        }

        // Equality is ordinal, like .NET. Concat allocates the result with malloc,
        // like every other allocation in zero mode, and it is never freed.

        public static bool operator ==(string a, string b) => Equals(a, b);

        public static bool operator !=(string a, string b) => !Equals(a, b);

        public bool Equals(string value) => Equals(this, value);

        public static bool Equals(string a, string b)
        {
            if ((object)a == (object)b)
                return true;
            if (a is null || b is null || a._stringLength != b._stringLength)
                return false;
            for (int i = 0; i < a._stringLength; i++)
            {
                if (Unsafe.Add(ref a._firstChar, i) != Unsafe.Add(ref b._firstChar, i))
                    return false;
            }
            return true;
        }

        public static bool IsNullOrEmpty(string value) => value is null || value._stringLength == 0;

        public static string Concat(string str0, string str1)
        {
            if (IsNullOrEmpty(str0))
                return str1 ?? "";
            if (IsNullOrEmpty(str1))
                return str0;
            return ConcatCore(str0, str1, null, null);
        }

        public static string Concat(string str0, string str1, string str2) => ConcatCore(str0, str1, str2, null);

        public static string Concat(string str0, string str1, string str2, string str3) => ConcatCore(str0, str1, str2, str3);

        public static string Concat(string[] values)
        {
            if (values is null)
                return "";

            long total = 0;
            for (int i = 0; i < values.Length; i++)
                total += LengthOf(values[i]);
            if (total == 0)
                return "";
            if (total > int.MaxValue)
                Environment.FailFast("string too long");

            string result = FastNewString((int)total);
            int position = 0;
            for (int i = 0; i < values.Length; i++)
                Append(result, ref position, values[i]);
            Terminate(result);
            return result;
        }

        static string ConcatCore(string str0, string str1, string str2, string str3)
        {
            long total = (long)LengthOf(str0) + LengthOf(str1) + LengthOf(str2) + LengthOf(str3);
            if (total == 0)
                return "";
            if (total > int.MaxValue)
                Environment.FailFast("string too long");

            string result = FastNewString((int)total);
            int position = 0;
            Append(result, ref position, str0);
            Append(result, ref position, str1);
            Append(result, ref position, str2);
            Append(result, ref position, str3);
            Terminate(result);
            return result;
        }

        static int LengthOf(string value) => value is null ? 0 : value._stringLength;

        static void Append(string destination, ref int position, string value)
        {
            if (value is null)
                return;
            for (int i = 0; i < value._stringLength; i++)
                Unsafe.Add(ref destination._firstChar, position + i) = Unsafe.Add(ref value._firstChar, i);
            position += value._stringLength;
        }

        // malloc doesn't zero memory; the string's base size has room for the terminator.
        static void Terminate(string value) => Unsafe.Add(ref value._firstChar, value._stringLength) = '\0';

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern unsafe String(char* value);

        private static unsafe string Ctor(char* ptr)
        {
            char* cur = ptr;
            while (*cur++ != 0) ;

            string result = FastNewString((int)(cur - ptr - 1));
            for (int i = 0; i < cur - ptr - 1; i++)
                Unsafe.Add(ref result._firstChar, i) = ptr[i];
            return result;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern unsafe String(sbyte* value);

        private static unsafe string Ctor(sbyte* ptr)
        {
            sbyte* cur = ptr;
            while (*cur++ != 0) ;

            string result = FastNewString((int)(cur - ptr - 1));
            for (int i = 0; i < cur - ptr - 1; i++)
            {
                if (ptr[i] > 0x7F)
                    Environment.FailFast(null);
                Unsafe.Add(ref result._firstChar, i) = (char)ptr[i];
            }
            return result;
        }

        static unsafe string FastNewString(int numChars)
        {
            return NewString("".m_pMethodTable, numChars);

            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "RhpNewArray")]
            static extern string NewString(MethodTable* pMT, int numElements);
        }
    }
}
