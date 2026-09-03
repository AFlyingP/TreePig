using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TreePig.Core
{
    enum SizeUnit
    {
        Auto,
        Bytes,
        KB,
        MB,
        GB,
        TB
    }

    static class Util
    {
        public static string FormatBytes(long bytes, SizeUnit unit = SizeUnit.Auto)
        {
            double abs = Math.Abs((double)bytes);
            switch (unit)
            {
                case SizeUnit.Bytes:
                    return bytes.ToString("N0") + " B";
                case SizeUnit.KB:
                    return (bytes / 1024.0).ToString("N1") + " KB";
                case SizeUnit.MB:
                    return (bytes / (1024.0 * 1024)).ToString("N1") + " MB";
                case SizeUnit.GB:
                    return (bytes / (1024.0 * 1024 * 1024)).ToString("N1") + " GB";
                case SizeUnit.TB:
                    return (bytes / (1024.0 * 1024 * 1024 * 1024)).ToString("N1") + " TB";
            }

            if (abs < 1024) return bytes.ToString("N0") + " B";
            if (abs < 1024 * 1024) return (bytes / 1024.0).ToString("N1") + " KB";
            if (abs < 1024.0 * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("N1") + " MB";
            if (abs < 1024.0 * 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("N2") + " GB";
            return (bytes / (1024.0 * 1024 * 1024 * 1024)).ToString("N2") + " TB";
        }

        // Explorer style comparison, "file10" sorts after "file9"
        public static int NaturalCompare(string a, string b)
            => StrCmpLogicalW(a ?? "", b ?? "");

        public static string FormatElapsed(TimeSpan t)
        {
            if (t.TotalMinutes >= 1)
                return string.Format("{0:0}m {1:0.0}s", t.TotalMinutes, t.Seconds);
            return string.Format("{0:0.0}s", t.TotalSeconds);
        }

        public static Color ParseColor(string text, Color fallback)
        {
            try
            {
                var parts = text.Split(',');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out int r) &&
                    int.TryParse(parts[1], out int g) &&
                    int.TryParse(parts[2], out int b))
                    return Color.FromArgb(r, g, b);
            }
            catch { }
            return fallback;
        }

        public static SizeUnit ParseUnit(string text)
        {
            return Enum.TryParse<SizeUnit>(text, out var unit) ? unit : SizeUnit.Auto;
        }

        public static uint GetClusterSize(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root)) return 0;
                if (GetDiskFreeSpaceW(root, out uint sectors, out uint bytesPerSector, out _, out _))
                    return sectors * bytesPerSector;
            }
            catch { }
            return 0;
        }

        public static long RoundUpToCluster(long size, uint cluster)
        {
            if (cluster <= 1 || size <= 0) return size;
            long rem = size % cluster;
            return rem == 0 ? size : size + (cluster - rem);
        }

        public static string GetOwner(string path)
        {
            try
            {
                var info = new FileInfo(path);
                var security = info.GetAccessControl();
                var id = security.GetOwner(typeof(System.Security.Principal.NTAccount));
                return id?.ToString();
            }
            catch { return null; }
        }

        public static void ShellOpen(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        public static void ShowInExplorer(string path)
        {
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        public static void OpenCmdHere(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/K cd /d \"" + path + "\"",
                WorkingDirectory = path,
                UseShellExecute = true
            });
        }

        // sends the file/folder to the recycle bin, returns false when the
        // user aborted or the shell refused
        public static bool DeleteToRecycleBin(string path)
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };
            int rc = SHFileOperationW(ref op);
            return rc == 0 && !op.fAnyOperationsAborted;
        }

        // --- win32 ---

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceW(string root,
            out uint sectorsPerCluster, out uint bytesPerSector,
            out uint freeClusters, out uint totalClusters);

        private const int FO_DELETE = 3;
        private const ushort FOF_ALLOWUNDO = 0x40;
        private const ushort FOF_NOCONFIRMATION = 0x10;
        private const ushort FOF_SILENT = 0x4;
        private const ushort FOF_NOERRORUI = 0x400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public int wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
    }
}
