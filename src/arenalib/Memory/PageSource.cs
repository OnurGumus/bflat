using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arenas
{
    public enum PageSourceFlags
    {
        None = 0,
        /// <summary>Reserved memory is guaranteed to arrive zero-filled.</summary>
        ZeroesOnReserve = 1,
        /// <summary>Release actually gives memory back to the OS.</summary>
        CanRelease = 2,
    }

    /// <summary>
    /// Where a root arena gets its backing memory. Everything funnels through this one
    /// seam, so a target whose default backend misbehaves can be given another one
    /// without touching a line of arena logic.
    /// </summary>
    public readonly unsafe struct PageSource
    {
        public readonly delegate*<nuint, void*> Reserve;
        public readonly delegate*<void*, nuint, void> Release;
        public readonly nuint Granularity;
        public readonly PageSourceFlags Flags;

        public PageSource(
            delegate*<nuint, void*> reserve,
            delegate*<void*, nuint, void> release,
            nuint granularity,
            PageSourceFlags flags)
        {
            Reserve = reserve;
            Release = release;
            Granularity = granularity;
            Flags = flags;
        }

        public bool ZeroesOnReserve => (Flags & PageSourceFlags.ZeroesOnReserve) != PageSourceFlags.None;

        public bool CanRelease => (Flags & PageSourceFlags.CanRelease) != PageSourceFlags.None;

        public bool IsValid => Reserve != null;

        /// <summary>The platform's native page source.</summary>
        public static PageSource Default
            => new PageSource(&OsReserve, &OsRelease, OsGranularity, OsFlags);

#if WINDOWS

        [DllImport("kernel32"), SuppressGCTransition]
        private static extern void* VirtualAlloc(void* address, nuint size, uint allocationType, uint protect);

        [DllImport("kernel32"), SuppressGCTransition]
        private static extern int VirtualFree(void* address, nuint size, uint freeType);

        private const uint MEM_COMMIT_RESERVE = 0x3000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private static void* OsReserve(nuint size)
            => VirtualAlloc(null, size, MEM_COMMIT_RESERVE, PAGE_READWRITE);

        private static void OsRelease(void* address, nuint size)
            => VirtualFree(address, 0, MEM_RELEASE);

        private static nuint OsGranularity => 65536;

        private static PageSourceFlags OsFlags
            => PageSourceFlags.ZeroesOnReserve | PageSourceFlags.CanRelease;

#elif LINUX

        [DllImport("*"), SuppressGCTransition]
        private static extern void* mmap(void* address, nuint length, int prot, int flags, int fd, long offset);

        [DllImport("*"), SuppressGCTransition]
        private static extern int munmap(void* address, nuint length);

        private const int PROT_READ_WRITE = 3;             // PROT_READ | PROT_WRITE

        // MAP_PRIVATE (0x02) | MAP_ANONYMOUS (0x20). MAP_ANONYMOUS is 0x20 on x86-64 and
        // aarch64 Linux and on bionic, which covers every bflat Linux target.
        private const int MAP_PRIVATE_ANONYMOUS = 0x22;

        private static void* OsReserve(nuint size)
        {
            void* result = mmap(null, size, PROT_READ_WRITE, MAP_PRIVATE_ANONYMOUS, -1, 0);

            // mmap reports failure as MAP_FAILED ((void*)-1), not null.
            return (nint)result == -1 ? null : result;
        }

        private static void OsRelease(void* address, nuint size)
            => munmap(address, size);

        private static nuint OsGranularity => 4096;

        private static PageSourceFlags OsFlags
            => PageSourceFlags.ZeroesOnReserve | PageSourceFlags.CanRelease;

#elif UEFI

        // arenalib cannot reach EFI boot services: EFI_SYSTEM_TABLE and EFI_BOOT_SERVICES
        // are internal to zerolib and the pointer captured by EfiMain is never surfaced.
        // So route through the managed allocator, which lands on AllocatePool. There is no
        // GC, so the array never moves and never dies; the managed reference is dropped
        // immediately and the memory is simply never reclaimed.
        private static void* OsReserve(nuint size)
        {
            byte[] block = new byte[(int)size];
            return Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(block));
        }

        private static void OsRelease(void* address, nuint size)
        {
            // Nothing to do: AllocatePool memory is reclaimed when the image exits.
        }

        private static nuint OsGranularity => 4096;

        // AllocatePool does not zero, and there is no free, so an arena on UEFI can
        // rewind but never actually returns memory.
        private static PageSourceFlags OsFlags => PageSourceFlags.None;

#else
#error Unsupported target OS
#endif
    }
}
