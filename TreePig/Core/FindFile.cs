using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TreePig.Core
{
    struct FindEntry
    {
        public string Name;
        public FileAttributes Attributes;
        public DateTime LastWriteUtc;
        public long Length;
    }

    // raw FindFirstFile/FindNextFile listing. DirectoryInfo.Enumerate... ends
    // up stat-ing every entry again for attributes and timestamps, this gets
    // everything in the single directory read. The struct is blitted straight
    // over with fixed buffers so the marshaler allocates nothing per entry.
    static class FindFile
    {
        public static List<FindEntry> ListDirectory(string path, out int win32Error)
        {
            win32Error = 0;
            var results = new List<FindEntry>();

            string search = path;
            if (search.Length > 240) search = "\\\\?\\" + search;
            if (!search.EndsWith("\\")) search += "\\";
            search += "*";

            unsafe
            {
                // locals are stack allocated, the address is fixed already
                var data = new WIN32_FIND_DATAW();
                WIN32_FIND_DATAW* pData = &data;
                IntPtr handle = FindFirstFileExW(search, FindExInfoBasic, pData,
                    FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
                if (handle == InvalidHandle)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    return results;
                }
                try
                {
                    while (true)
                    {
                        if (!FindNextFileW(handle, pData))
                            break;
                        string name = new string(data.cFileName);
                        if (name.Length == 0) break;
                        if (name == "." || name == "..") continue;
                        results.Add(new FindEntry
                        {
                            Name = name,
                            Attributes = (FileAttributes)data.dwFileAttributes,
                            LastWriteUtc = FromFileTime(data.ftLastWriteTime),
                            Length = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow
                        });
                    }
                }
                finally { FindClose(handle); }
            }
            return results;
        }

        static DateTime FromFileTime(System.Runtime.InteropServices.ComTypes.FILETIME ft)
            => DateTime.FromFileTimeUtc(((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime);

        static readonly IntPtr InvalidHandle = new IntPtr(-1);
        const int FindExInfoBasic = 1;           // skip the 8.3 short name, we never use it
        const int FindExSearchNameMatch = 0;
        const int FIND_FIRST_EX_LARGE_FETCH = 2; // bigger directory buffer

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        unsafe struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            public fixed char cFileName[260];
            public fixed char cAlternateFileName[14];
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern unsafe IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId,
            WIN32_FIND_DATAW* lpFindFileData, int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern unsafe bool FindNextFileW(IntPtr hFindFile, WIN32_FIND_DATAW* lpFindFileData);

        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr hFindFile);
    }
}
