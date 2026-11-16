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

using System.Runtime.CompilerServices;
using Internal.Runtime.CompilerHelpers;

namespace System.Runtime
{
    // The compiler looks these up by name for arrays of reference types: storing
    // an element (stelem.ref, e.g. string[] items = { a, b }) and taking a ref to
    // one (ldelema). Without them, any such code fails to compile with
    // "Expected type 'System.Runtime.TypeCast' not found in module 'zerolib'".
    internal static unsafe partial class TypeCast
    {
        private static void StelemRef(object[] array, nint index, object obj)
            => StartupCodeHelpers.StelemRef(array, index, obj);

        private static ref object LdelemaRef(object[] array, nint index, void* elementType)
            => ref Unsafe.As<StartupCodeHelpers.ArrayElement[]>(array)[index].Value;

        // Casts to a class type: obj is an instance if toType is its type or one of
        // its base types (for a class, _relatedType is the base type). `x is T` and
        // `x as T` use IsInstanceOfClass; `(T)x` uses CheckCastClass, which fails
        // fast where .NET would throw InvalidCastException. Casts to interface and
        // array types aren't supported (they need other helpers).
        private static object IsInstanceOfClass(MethodTable* toType, object obj)
        {
            if (obj == null)
                return null;
            for (MethodTable* type = obj.m_pMethodTable; type != null; type = type->_relatedType)
            {
                if (type == toType)
                    return obj;
            }
            return null;
        }

        private static object CheckCastClass(MethodTable* toType, object obj)
        {
            if (obj != null && IsInstanceOfClass(toType, obj) == null)
                Environment.FailFast("InvalidCastException");
            return obj;
        }
    }
}
