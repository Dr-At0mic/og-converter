using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>CPU name and installed programs from the registry, memory from GlobalMemoryStatusEx.</summary>
    public sealed class WindowsSystemInfo : ISystemInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        private static readonly Lazy<string> Cpu = new(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (key?.GetValue("ProcessorNameString") as string)?.Trim() is { Length: > 0 } name ? name : "CPU";
            }
            catch
            {
                return "CPU";
            }
        });

        public string CpuName => Cpu.Value;

        public bool TryGetMemory(out long totalBytes, out long freeBytes)
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                totalBytes = (long)status.TotalPhys;
                freeBytes = (long)status.AvailPhys;
                return true;
            }
            totalBytes = freeBytes = 0;
            return false;
        }

        /// <summary>Installs in unusual places register themselves under App Paths (machine-wide first, then per user).</summary>
        public IReadOnlyList<string> RegisteredAppPaths(string exeName)
        {
            var paths = new List<string>();
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                    if (key?.GetValue(null) is string path) paths.Add(path.Trim('"'));
                }
                catch { }
            }
            return paths;
        }
    }
}
