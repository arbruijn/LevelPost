using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using rdbundle;

namespace LevelPost
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WIN32_FILE_ATTRIBUTE_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
    }
    
    enum GET_FILEEX_INFO_LEVELS
    {
        GetFileExInfoStandard,
        GetFileExMaxInfoLevel
    }
    static class FileTimeExt
    {
        public static UInt64 ToUInt64(this System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            return (uint)ft.dwLowDateTime | ((ulong)(uint)ft.dwHighDateTime << 32);
        }
    }

    static class DirScan
    {
        public const int ERROR_NO_MORE_FILES = 18;
        public const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool FindClose(IntPtr hFindFile);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool GetFileAttributesEx(string lpFileName,
          GET_FILEEX_INFO_LEVELS fInfoLevelId, out WIN32_FILE_ATTRIBUTE_DATA fileData);

        public static IEnumerable<WIN32_FIND_DATA> DirScanner(string path)
        {
            WIN32_FIND_DATA data;
            var hnd = FindFirstFile(path + @"\*", out data);
            if (hnd == (IntPtr)(-1))
                throw new Exception(path + ": " + new Win32Exception().Message);
            for (;;)
            {
                yield return data;
                if (!FindNextFile(hnd, out data)) {
                    if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_FILES)
                        yield break;
                    throw new Win32Exception(path);
                }
            }
        }
    }

    class BundleInfo
    {
        public string path;
        public UInt64 lastWriteTime;
        public Dictionary<string, string> materials;
        public HashSet<string> gameObjects;
    }

    class BundleFiles
    {

        public Dictionary<string, BundleInfo> Bundles = new Dictionary<string, BundleInfo>();
        public Action<string> Logger;

        public void ScanBundles(string baseDir)
        {
            var dirs = new Stack<string>();
            dirs.Push(baseDir);
            while (dirs.Any())
            {
                string dir = dirs.Pop();
                foreach (var f in DirScan.DirScanner(dir))
                {
                    string filename = f.cFileName;
                    string path = Path.Combine(dir, filename);
                    if ((f.dwFileAttributes & DirScan.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        if (filename != "." && filename != "..")
                            dirs.Push(path);
                        continue;
                    }
                    if (dir.EndsWith(@"\linux") || dir.EndsWith(@"\osx"))
                        continue;
                    if (filename.Contains('.') || (f.nFileSizeHigh == 0 && f.nFileSizeLow < 1024))
                        continue;
                    CachedBundleInfo(path, f.ftLastWriteTime.ToUInt64());
                }
            }
        }

        public BundleInfo CachedBundleInfo(string path, UInt64 lastWriteTime = 0)
        {
            if (lastWriteTime == 0)
            {

                if (!DirScan.GetFileAttributesEx(path, GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, out WIN32_FILE_ATTRIBUTE_DATA fa))
                    throw new Exception(path + ": " + new Win32Exception().Message);
                lastWriteTime = fa.ftLastWriteTime.ToUInt64();
            }
            if (!Bundles.TryGetValue(path.ToUpperInvariant(), out BundleInfo info))
                Bundles.Add(path.ToUpperInvariant(), info = new BundleInfo() { path = path });
            else if (info.lastWriteTime == lastWriteTime)
                return info;
            info.lastWriteTime = lastWriteTime;
            BundleFile.ReadBundleFile(path, out List<string> materials, out List<string> gameObjects);
            info.materials = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (var material in materials)
                if (info.materials.ContainsKey(material.ToLowerInvariant()))
                    Logger("WARNING: Bundle " + path + " contains multiple versions of " + material);
                else
                    info.materials.Add(material.ToLowerInvariant(), material);
            info.gameObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var gameObject in gameObjects)
                if (gameObject.StartsWith("entity_", StringComparison.OrdinalIgnoreCase))
                    info.gameObjects.Add(gameObject);
            return info;
        }
    }
}
