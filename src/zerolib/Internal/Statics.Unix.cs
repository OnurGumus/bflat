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

#if LINUX || WASI

using System.Runtime;

namespace Internal.Runtime.CompilerHelpers
{
    // Static fields of reference types (string, classes, arrays) live in one
    // "GC static base" object per type. The compiler lists the slots that point
    // to these objects in each module's GC static region and leaves them
    // uninitialized; the runtime has to allocate the objects at startup. Without
    // this, the first store to such a field writes through a bogus pointer.
    //
    // This mirrors InitializeStatics in NativeAOT's StartupCodeHelpers. It runs
    // from InitializeCommandLineArgs, which the compiler calls before Main.
    unsafe partial class StartupCodeHelpers
    {
#if WASM
        // WASM has no linker-defined section bounds, and C# can't take the address
        // of a data symbol there (a function "address" is a table index). The
        // native startup shim returns &__ReadyToRunHeader instead.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "ZeroGetModuleHeader")]
        static extern byte* GetModuleHeader();
#else
        // The linker defines these around the __modules section, which holds a
        // pointer to each module's ReadyToRunHeader. Referencing them also keeps
        // the section alive under --gc-sections.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "__start___modules")]
        static extern void ModulesStart();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "__stop___modules")]
        static extern void ModulesStop();
#endif

        const uint ReadyToRunSignature = 0x00525452; // "RTR"
        const int GCStaticRegionSection = 201;       // ReadyToRunSectionType.GCStaticRegion

        // Flags in the low bits of an uninitialized slot (GCStaticRegionConstants).
        const nint Uninitialized = 1;
        const nint HasPreInitializedData = 2;
        const nint FlagsMask = 3;

        static bool s_staticsInitialized;

        internal static void InitializeStatics()
        {
            if (s_staticsInitialized)
                return;
            s_staticsInitialized = true;

#if WASM
            InitializeModuleStatics(GetModuleHeader());
#else
            byte** module = (byte**)(delegate*<void>)&ModulesStart;
            byte** end = (byte**)(delegate*<void>)&ModulesStop;
            for (; module < end; module++)
                InitializeModuleStatics(*module);
#endif
        }

        // ReadyToRunHeader: uint Signature, ushort Major, ushort Minor, uint Flags,
        // ushort NumberOfSections, byte EntrySize, byte EntryType, then
        // NumberOfSections rows of { int SectionId, int Flags, void* Start, void* End }.
        static void InitializeModuleStatics(byte* header)
        {
            if (header == null || *(uint*)header != ReadyToRunSignature)
                return;

            ushort sectionCount = *(ushort*)(header + 12);
            byte entrySize = header[14];
            byte* row = header + 16;
            for (int i = 0; i < sectionCount; i++, row += entrySize)
            {
                if (*(int*)row != GCStaticRegionSection)
                    continue;

                byte* start = *(byte**)(row + 8);
                byte* stop = *(byte**)(row + 8 + sizeof(nint));
                // Each entry is a 32-bit pointer to a slot: relative on Linux,
                // absolute on WASM (whose object format has no relative relocations).
                for (byte* entry = start; entry < stop; entry += sizeof(int))
#if WASM
                    InitializeStaticBase(*(byte**)entry);
#else
                    InitializeStaticBase(entry + *(int*)entry);
#endif
            }
        }

        // An uninitialized slot holds a pointer to the base object's MethodTable
        // (32-bit relative on Linux, absolute on WASM), with flags in the low
        // bits, optionally followed by a pointer to preinitialized field data.
        static void InitializeStaticBase(byte* slot)
        {
#if WASM
            nint target = *(nint*)slot;
#else
            nint target = (nint)slot + *(int*)slot;
#endif
            if ((target & Uninitialized) == 0)
                return;

            MethodTable* mt = (MethodTable*)(target & ~FlagsMask);
            uint size = mt->_uBaseSize;
            byte* obj = (byte*)AllocObject(size); // zeroed (calloc)
            *(MethodTable**)obj = mt;

            if ((target & HasPreInitializedData) != 0)
            {
                // The blob holds every field of the object (BaseSize minus the
                // object header and MethodTable pointer).
#if WASM
                byte* data = *(byte**)(slot + sizeof(nint));
#else
                byte* data = slot + sizeof(int) + *(int*)(slot + sizeof(int));
#endif
                uint dataSize = size - 2 * (uint)sizeof(nint);
                for (uint i = 0; i < dataSize; i++)
                    obj[sizeof(nint) + i] = data[i];
            }

            *(byte**)slot = obj;
        }
    }
}

#endif
