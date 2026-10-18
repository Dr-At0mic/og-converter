using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>What FFmpeg reports about a media file (parsed from `ffmpeg -i`, so ffprobe isn't needed).</summary>
    public class MediaInfo
    {
        public double DurationSeconds { get; set; }
        public bool HasVideo { get; set; }
        public bool HasAudio { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; }
        public string VideoCodec { get; set; } = "";
        public string PixelFormat { get; set; } = "";
        public string AudioCodec { get; set; } = "";
        public int AudioKbps { get; set; }
        public int AudioChannels { get; set; }
        public bool Is10Bit => PixelFormat.Contains("10") || PixelFormat.Contains("12");
        public bool HasLosslessAudio => AudioCodec.StartsWith("pcm", StringComparison.Ordinal) || AudioCodec is "flac" or "alac" or "wavpack" or "ape" or "tta" or "truehd" or "mlp";
    }

    /// <summary>Runs FFmpeg with progress reporting, probes media, and detects which encoders actually work.</summary>
    public static class MediaToolkit
    {
        public static async Task<MediaInfo> ProbeAsync(string ffmpeg, string path)
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var a in new[] { "-hide_banner", "-nostdin", "-i", path }) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync();
            string err = await p.StandardError.ReadToEndAsync();
            await outTask;
            await p.WaitForExitAsync();
            return ParseProbe(err);
        }

        public static MediaInfo ParseProbe(string text)
        {
            var info = new MediaInfo();
            var d = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
            if (d.Success)
            {
                info.DurationSeconds = int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60
                                     + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
            }

            foreach (string line in text.Split('\n'))
            {
                if (!line.TrimStart().StartsWith("Stream #", StringComparison.Ordinal)) continue;

                var v = Regex.Match(line, @"Video:\s*([\w-]+)");
                if (v.Success && !info.HasVideo && !line.Contains("attached pic"))
                {
                    info.HasVideo = true;
                    info.VideoCodec = v.Groups[1].Value;
                    var pf = Regex.Match(line, @"Video:[^,]*,\s*([a-z0-9_]+)");
                    if (pf.Success) info.PixelFormat = pf.Groups[1].Value;
                    var wh = Regex.Match(line, @",\s*(\d{2,5})x(\d{2,5})");
                    if (wh.Success)
                    {
                        info.Width = int.Parse(wh.Groups[1].Value);
                        info.Height = int.Parse(wh.Groups[2].Value);
                    }
                    var fps = Regex.Match(line, @"([\d.]+)\s*fps");
                    if (fps.Success) info.Fps = double.Parse(fps.Groups[1].Value, CultureInfo.InvariantCulture);
                    continue;
                }

                var a = Regex.Match(line, @"Audio:\s*([\w-]+)");
                if (a.Success && !info.HasAudio)
                {
                    info.HasAudio = true;
                    info.AudioCodec = a.Groups[1].Value;
                    var kb = Regex.Match(line, @"(\d+)\s*kb/s");
                    if (kb.Success) info.AudioKbps = int.Parse(kb.Groups[1].Value);
                    info.AudioChannels = line.Contains("mono") ? 1 : line.Contains("stereo") ? 2 : line.Contains("5.1") ? 6 : 2;
                }
            }

            // A rotated phone video is stored landscape; FFmpeg auto-rotates when encoding, so report what the viewer sees
            var rot = Regex.Match(text, @"rotation of (-?[\d.]+) degrees");
            if (rot.Success && Math.Abs(Math.Abs(double.Parse(rot.Groups[1].Value, CultureInfo.InvariantCulture)) - 90) < 1)
            {
                (info.Width, info.Height) = (info.Height, info.Width);
            }
            return info;
        }

        /// <summary>
        /// Runs FFmpeg. <paramref name="durationSeconds"/> &gt; 0 enables progress (0–1) through <paramref name="progress"/>.
        /// Throws with the tail of FFmpeg's log on failure.
        /// </summary>
        public static async Task RunAsync(string ffmpeg, IEnumerable<string> args, double durationSeconds = 0,
            IProgress<double>? progress = null, string? workingDirectory = null)
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = workingDirectory ?? ""
            };
            foreach (var a in new[] { "-hide_banner", "-nostdin", "-y", "-progress", "pipe:1", "-nostats" }) psi.ArgumentList.Add(a);
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = new Process { StartInfo = psi };
            p.Start();

            // Keep only the end of the log; that's where FFmpeg explains a failure
            var log = new StringBuilder();
            var errTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync()) != null)
                {
                    log.AppendLine(line);
                    if (log.Length > 16_000) log.Remove(0, log.Length - 8_000);
                }
            });

            string? outLine;
            while ((outLine = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (progress == null || durationSeconds <= 0) continue;
                if (outLine.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(outLine.AsSpan(12), out long us) && us > 0)
                {
                    progress.Report(Math.Clamp(us / 1_000_000.0 / durationSeconds, 0, 1));
                }
            }

            await errTask;
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
            {
                throw new InvalidOperationException($"FFmpeg failed (exit code {p.ExitCode}): {SummarizeError(log.ToString())}");
            }
        }

        /// <summary>The last few meaningful lines of an FFmpeg log.</summary>
        private static string SummarizeError(string log)
        {
            var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Where(l => !l.StartsWith("frame=", StringComparison.Ordinal) && !l.StartsWith("Stream mapping", StringComparison.Ordinal))
                           .TakeLast(4);
            return string.Join(" ", lines);
        }

        // ---------- Encoder capabilities ----------

        private static Task<HashSet<string>>? _encoders;
        private static Task<HashSet<string>>? _workingHardware;
        private static readonly object Sync = new();

        public static readonly string[] HardwareEncoders =
        {
            "h264_nvenc", "hevc_nvenc", "av1_nvenc",
            "h264_qsv", "hevc_qsv", "av1_qsv",
            "h264_amf", "hevc_amf", "av1_amf"
        };

        /// <summary>Names of the encoders compiled into this FFmpeg build (cached).</summary>
        public static Task<HashSet<string>> GetEncodersAsync(string ffmpeg)
        {
            lock (Sync)
            {
                return _encoders ??= Task.Run(async () =>
                {
                    var set = new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        var psi = new ProcessStartInfo(ffmpeg, "-hide_banner -encoders")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var p = Process.Start(psi)!;
                        var errTask = p.StandardError.ReadToEndAsync();
                        string output = await p.StandardOutput.ReadToEndAsync();
                        await errTask;
                        await p.WaitForExitAsync();
                        foreach (Match m in Regex.Matches(output, @"^\s*[VAS][.\w]{5}\s+(\S+)", RegexOptions.Multiline))
                        {
                            set.Add(m.Groups[1].Value);
                        }
                    }
                    catch
                    {
                        // Unknown build: callers fall back to the safest encoders
                    }
                    return set;
                });
            }
        }

        /// <summary>
        /// Hardware encoders that actually start on this PC. A build can list NVENC without an NVIDIA GPU, so each
        /// candidate encodes a few blank frames; the ones that succeed are kept (cached for the session).
        /// </summary>
        public static Task<HashSet<string>> GetWorkingHardwareEncodersAsync(string ffmpeg)
        {
            lock (Sync)
            {
                return _workingHardware ??= Task.Run(async () =>
                {
                    var listed = await GetEncodersAsync(ffmpeg);
                    var candidates = HardwareEncoders.Where(listed.Contains).ToList();
                    var results = await Task.WhenAll(candidates.Select(async enc =>
                    {
                        try
                        {
                            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                            var psi = new ProcessStartInfo(ffmpeg)
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true
                            };
                            foreach (var a in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=320x240:r=30:d=0.2",
                                                      "-pix_fmt", "nv12", "-c:v", enc, "-f", "null", "-" })
                            {
                                psi.ArgumentList.Add(a);
                            }
                            using var p = Process.Start(psi)!;
                            var errTask = p.StandardError.ReadToEndAsync();
                            await p.WaitForExitAsync(cts.Token);
                            await errTask;
                            return p.ExitCode == 0 ? enc : null;
                        }
                        catch
                        {
                            return null;
                        }
                    }));
                    return results.Where(r => r != null).Select(r => r!).ToHashSet(StringComparer.Ordinal);
                });
            }
        }
    }
}
