using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OgConverter.Services
{
    /// <summary>What the machine has: CPU, memory, and where installed programs registered themselves.</summary>
    public interface ISystemInfo
    {
        /// <summary>"AMD Ryzen 9 9900X 12-Core Processor", or "CPU" when unknown.</summary>
        string CpuName { get; }

        /// <summary>Physical memory in bytes: installed, and free right now. False when it can't be read.</summary>
        bool TryGetMemory(out long totalBytes, out long freeBytes);

        /// <summary>Full paths a program registered for <paramref name="exeName"/> (Windows "App Paths"); empty elsewhere.</summary>
        IReadOnlyList<string> RegisteredAppPaths(string exeName);
    }

    /// <summary>Default: Linux's /proc files where they exist, otherwise "unknown".</summary>
    public sealed class PortableSystemInfo : ISystemInfo
    {
        private readonly Lazy<string> _cpu = new(() =>
        {
            try
            {
                string? line = File.Exists("/proc/cpuinfo")
                    ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))
                    : null;
                return line?.Split(':', 2) is { Length: 2 } parts && parts[1].Trim() is { Length: > 0 } name ? name : "CPU";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "CPU";
            }
        });

        public string CpuName => _cpu.Value;

        public bool TryGetMemory(out long totalBytes, out long freeBytes)
        {
            totalBytes = freeBytes = 0;
            try
            {
                if (!File.Exists("/proc/meminfo")) return false;
                foreach (string line in File.ReadLines("/proc/meminfo"))
                {
                    // "MemTotal:       32768000 kB"
                    var parts = line.Split(':', 2);
                    if (parts.Length != 2 || !long.TryParse(parts[1].Trim().Split(' ')[0], out long kb)) continue;
                    if (parts[0] == "MemTotal") totalBytes = kb * 1024;
                    else if (parts[0] == "MemAvailable") freeBytes = kb * 1024;
                }
                return totalBytes > 0 && freeBytes > 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public IReadOnlyList<string> RegisteredAppPaths(string exeName) => Array.Empty<string>();
    }
}
