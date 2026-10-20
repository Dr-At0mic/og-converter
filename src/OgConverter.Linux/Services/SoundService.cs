using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OgConverter.Models;

namespace OgConverter.Services
{
    public enum UiSound
    {
        /// <summary>Moving onto an option.</summary>
        Tick,
        /// <summary>Switching between the Convert, Tools and Actions wheels.</summary>
        Switch,
        /// <summary>The wheel appearing.</summary>
        Open,
        /// <summary>Dropping a file on an option.</summary>
        Select
    }

    /// <summary>
    /// Short UI sounds. Each template picks a synthesized pack (or its own .wav files). Sounds are generated once into
    /// small .wav files and played by the desktop's sound server tools (pw-play, else paplay, else aplay) in a
    /// separate process, so the UI never waits for audio. Without any of those tools the app is silent.
    /// </summary>
    public sealed class SoundService
    {
        private const int SampleRate = 44100;
        private const long MaxWavBytes = 2 * 1024 * 1024;

        private static readonly Lazy<SoundService> _instance = new(() => new SoundService());
        public static SoundService Instance => _instance.Value;

        public static readonly string[] Packs = { "click", "glass", "bubble", "synth", "soft", "none" };

        private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new();
        private long _lastTickMs = -1000;
        private (string Path, string[] Args)? _player;
        private bool _playerResolved;
        private string? _cacheDir;

        /// <summary>Settings › Sound feedback. The wheel keeps it in sync with the settings each time it opens.</summary>
        public bool IsEnabled { get; set; } = true;

        public void PlayTick() => Play(UiSound.Tick);
        public void PlaySwitch() => Play(UiSound.Switch);

        public void Play(UiSound sound) => Play(sound, TemplateManager.Instance.Current);

        /// <summary>Plays in the background; returns at once. <paramref name="force"/> plays even when sounds are off (previews).</summary>
        public void Play(UiSound sound, WheelTemplate template, bool force = false)
        {
            if (!IsEnabled && !force) return;

            // Sweeping across a wheel fires a hover per option; each one starts a player process, so keep ticks sparse
            if (sound == UiSound.Tick)
            {
                long now = _clock.ElapsedMilliseconds;
                if (now - _lastTickMs < 45) return;
                _lastTickMs = now;
            }

            try
            {
                string? file;
                lock (_gate) file = Resolve(sound, template);
                if (file == null) return;
                var player = Player();
                if (player == null) return;

                var psi = new ProcessStartInfo(player.Value.Path)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string a in player.Value.Args) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add(file);
                using var process = Process.Start(psi);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                // Sound is decoration: a missing or broken player must never get in the way
            }
        }

        /// <summary>pw-play (PipeWire), else paplay (PulseAudio), else aplay (ALSA); null when none is installed.</summary>
        private (string Path, string[] Args)? Player()
        {
            if (_playerResolved) return _player;
            _playerResolved = true;
            if (Platform.FindOnPath("pw-play") is { } pw) _player = (pw, Array.Empty<string>());
            else if (Platform.FindOnPath("paplay") is { } pa) _player = (pa, Array.Empty<string>());
            else if (Platform.FindOnPath("aplay") is { } al) _player = (al, new[] { "-q" });
            return _player;
        }

        private string? Resolve(UiSound sound, WheelTemplate template)
        {
            var s = template.Sounds ?? new WheelSounds();
            string? custom = sound switch
            {
                UiSound.Tick => s.Tick,
                UiSound.Switch => s.Switch,
                UiSound.Open => s.Open,
                _ => s.Select
            };

            string? file = TemplateManager.ResolveAsset(template, custom);
            if (file != null)
            {
                string key = "file:" + file;
                if (!_cache.TryGetValue(key, out var p))
                {
                    p = IsWav(file) ? file : null;
                    _cache[key] = p;
                }
                if (p != null) return p;
            }

            string pack = string.IsNullOrWhiteSpace(s.Pack) ? "click" : s.Pack.Trim().ToLowerInvariant();
            if (pack == "none") return null;
            double volume = Math.Clamp(s.Volume, 0, 1);
            if (volume <= 0) return null;

            string synthKey = $"{pack}-{sound}-{volume:0.00}".ToLowerInvariant();
            if (_cache.TryGetValue(synthKey, out var cached)) return cached;

            string? path = null;
            string? dir = CacheDirectory();
            if (dir != null)
            {
                path = Path.Combine(dir, synthKey + ".wav");
                if (!File.Exists(path))
                {
                    string tmp = path + "." + Environment.ProcessId + ".tmp";
                    File.WriteAllBytes(tmp, Synthesize(pack, sound, volume));
                    File.Move(tmp, path, overwrite: true);
                }
            }
            _cache[synthKey] = path;
            return path;
        }

        /// <summary>$XDG_RUNTIME_DIR/og-converter-sounds (private, in memory), else a folder in the temp directory.</summary>
        private string? CacheDirectory()
        {
            if (_cacheDir != null) return _cacheDir;
            string root = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run && Directory.Exists(run)
                ? run
                : Path.GetTempPath();
            try
            {
                string dir = Path.Combine(root, "og-converter-sounds");
                Directory.CreateDirectory(dir);
                _cacheDir = dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return _cacheDir;
        }

        private static bool IsWav(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length < 44 || info.Length > MaxWavBytes) return false;
                Span<byte> head = stackalloc byte[12];
                using var fs = File.OpenRead(path);
                if (fs.Read(head) < 12) return false;
                return head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                    && head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E';
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // ------------------------------------------------------------------ synthesis (same packs as the Windows app)

        /// <summary>One voice: a tone with an optional pitch glide and an exponential decay.</summary>
        private readonly record struct Voice(double StartMs, double DurationMs, double FreqFrom, double FreqTo, double Decay, double Gain, Wave Wave = Wave.Sine, double Partial = 0);

        private enum Wave { Sine, Soft, Square, Noise }

        private static byte[] Synthesize(string pack, UiSound sound, double volume)
        {
            Voice[] voices = (pack, sound) switch
            {
                // Dry mechanical click: the original OG tick
                ("click", UiSound.Tick) => new[] { new Voice(0, 9, 2400, 2400, 350, 0.70, Partial: 0.43) },
                ("click", UiSound.Switch) => new[] { new Voice(0, 14, 1800, 1800, 220, 0.70, Partial: 0.25) },
                ("click", UiSound.Open) => new[] { new Voice(0, 18, 900, 1300, 200, 0.40, Partial: 0.3) },
                ("click", UiSound.Select) => new[] { new Voice(0, 16, 1500, 1500, 240, 0.55), new Voice(28, 22, 2250, 2250, 200, 0.5) },

                // Glass: bright, slightly ringing taps in the spirit of macOS "Tink" and "Pop"
                ("glass", UiSound.Tick) => new[] { new Voice(0, 30, 3520, 3520, 140, 0.32, Partial: 0.35) },
                ("glass", UiSound.Switch) => new[] { new Voice(0, 45, 520, 1180, 70, 0.55, Wave.Soft) },
                ("glass", UiSound.Open) => new[] { new Voice(0, 150, 1760, 1760, 26, 0.26, Partial: 0.4), new Voice(40, 160, 2637, 2637, 24, 0.20, Partial: 0.3) },
                ("glass", UiSound.Select) => new[] { new Voice(0, 110, 2093, 2093, 38, 0.40, Partial: 0.45), new Voice(0, 110, 3136, 3136, 44, 0.18) },

                // Bubble: rising "bloops"
                ("bubble", UiSound.Tick) => new[] { new Voice(0, 26, 650, 1500, 90, 0.42) },
                ("bubble", UiSound.Switch) => new[] { new Voice(0, 30, 420, 900, 80, 0.45), new Voice(40, 34, 600, 1300, 80, 0.4) },
                ("bubble", UiSound.Open) => new[] { new Voice(0, 80, 260, 880, 34, 0.5) },
                ("bubble", UiSound.Select) => new[] { new Voice(0, 50, 900, 1900, 50, 0.45) },

                // Synth: soft-square chiptune blips and arpeggios
                ("synth", UiSound.Tick) => new[] { new Voice(0, 16, 1320, 1320, 180, 0.22, Wave.Square) },
                ("synth", UiSound.Switch) => new[] { new Voice(0, 32, 880, 880, 70, 0.22, Wave.Square), new Voice(34, 40, 1320, 1320, 60, 0.22, Wave.Square) },
                ("synth", UiSound.Open) => new[] { new Voice(0, 34, 660, 660, 60, 0.18, Wave.Square), new Voice(36, 34, 880, 880, 60, 0.18, Wave.Square), new Voice(72, 70, 1320, 1320, 36, 0.18, Wave.Square) },
                ("synth", UiSound.Select) => new[] { new Voice(0, 40, 1760, 1760, 60, 0.2, Wave.Square), new Voice(0, 40, 880, 880, 60, 0.16, Wave.Square) },

                // Soft: low, felt-like thocks
                ("soft", UiSound.Tick) => new[] { new Voice(0, 20, 430, 380, 230, 0.75, Wave.Soft), new Voice(0, 6, 0, 0, 900, 0.12, Wave.Noise) },
                ("soft", UiSound.Switch) => new[] { new Voice(0, 30, 320, 270, 150, 0.75, Wave.Soft) },
                ("soft", UiSound.Open) => new[] { new Voice(0, 60, 210, 260, 60, 0.6, Wave.Soft) },
                ("soft", UiSound.Select) => new[] { new Voice(0, 40, 520, 520, 110, 0.6, Wave.Soft), new Voice(30, 50, 780, 780, 100, 0.45, Wave.Soft) },

                // Unknown pack names sound like "click"
                _ => FallbackVoices(sound)
            };

            double totalMs = 0;
            foreach (var v in voices) totalMs = Math.Max(totalMs, v.StartMs + v.DurationMs);
            int n = (int)(SampleRate * totalMs / 1000.0) + 1;
            var mix = new double[n];
            var rng = new Random(7);

            foreach (var v in voices)
            {
                int start = (int)(SampleRate * v.StartMs / 1000.0);
                int len = (int)(SampleRate * v.DurationMs / 1000.0);
                double phase = 0, phase2 = 0;
                for (int i = 0; i < len && start + i < n; i++)
                {
                    double t = (double)i / SampleRate;
                    double k = (double)i / Math.Max(1, len - 1);
                    double freq = v.FreqFrom + (v.FreqTo - v.FreqFrom) * k;
                    phase += 2 * Math.PI * freq / SampleRate;
                    phase2 += 2 * Math.PI * freq * 1.5 / SampleRate;
                    double osc = v.Wave switch
                    {
                        Wave.Square => Math.Tanh(4 * Math.Sin(phase)) * 0.8,
                        Wave.Soft => Math.Sin(phase) + 0.15 * Math.Sin(2 * phase),
                        Wave.Noise => rng.NextDouble() * 2 - 1,
                        _ => Math.Sin(phase)
                    };
                    if (v.Partial > 0) osc = (1 - v.Partial) * osc + v.Partial * Math.Sin(phase2);

                    // 1.5 ms attack avoids a click at the start; the tail fades to zero so it never pops at the end
                    double attack = Math.Min(1, t / 0.0015);
                    double tail = Math.Min(1, (len - i) / (SampleRate * 0.003));
                    mix[start + i] += osc * Math.Exp(-v.Decay * t) * attack * tail * v.Gain;
                }
            }

            // A little silence at the end: some players cut the last few milliseconds when the stream drains
            var samples = new short[n + SampleRate / 50];
            for (int i = 0; i < n; i++)
            {
                samples[i] = (short)Math.Clamp(mix[i] * volume * 12000.0, short.MinValue, short.MaxValue);
            }
            return Wav(samples);
        }

        private static Voice[] FallbackVoices(UiSound sound) => sound switch
        {
            UiSound.Tick => new[] { new Voice(0, 9, 2400, 2400, 350, 0.70, Partial: 0.43) },
            UiSound.Open => new[] { new Voice(0, 18, 900, 1300, 200, 0.40, Partial: 0.3) },
            UiSound.Select => new[] { new Voice(0, 16, 1500, 1500, 240, 0.55), new Voice(28, 22, 2250, 2250, 200, 0.5) },
            _ => new[] { new Voice(0, 14, 1800, 1800, 220, 0.70, Partial: 0.25) }
        };

        private static byte[] Wav(short[] samples)
        {
            using var ms = new MemoryStream(44 + samples.Length * 2);
            using var bw = new BinaryWriter(ms);
            bw.Write("RIFF"u8);
            bw.Write(36 + samples.Length * 2);
            bw.Write("WAVE"u8);
            bw.Write("fmt "u8);
            bw.Write(16);
            bw.Write((short)1);              // PCM
            bw.Write((short)1);              // mono
            bw.Write(SampleRate);
            bw.Write(SampleRate * 2);        // byte rate
            bw.Write((short)2);              // block align
            bw.Write((short)16);             // bits per sample
            bw.Write("data"u8);
            bw.Write(samples.Length * 2);
            foreach (var s in samples) bw.Write(s);
            bw.Flush();
            return ms.ToArray();
        }
    }
}
