using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    /// Short UI sounds. Each template picks a synthesized pack (or its own .wav files); sounds are generated once
    /// and kept in unmanaged memory, because PlaySound reads an SND_ASYNC buffer after the call returns and a
    /// managed array could be moved by the GC mid-playback.
    /// </summary>
    public sealed class SoundService
    {
        private const uint SND_ASYNC = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;
        private const uint SND_MEMORY = 0x0004;
        private const int SampleRate = 44100;
        private const long MaxWavBytes = 2 * 1024 * 1024;

        [DllImport("winmm.dll", EntryPoint = "PlaySound", SetLastError = true)]
        private static extern bool PlaySound(IntPtr sound, IntPtr hmod, uint flags);

        private static readonly Lazy<SoundService> _instance = new(() => new SoundService());
        public static SoundService Instance => _instance.Value;

        public static readonly string[] Packs = { "click", "glass", "bubble", "synth", "soft", "none" };

        private readonly Dictionary<string, IntPtr> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastTickMs = -1000;

        public bool IsEnabled { get; set; } = true;

        public void PlayTick() => Play(UiSound.Tick);
        public void PlaySwitch() => Play(UiSound.Switch);

        public void Play(UiSound sound) => Play(sound, TemplateManager.Instance.Current);

        public void Play(UiSound sound, WheelTemplate template, bool force = false)
        {
            if (!IsEnabled && !force) return;

            // Sweeping across a wheel fires a hover per option; past ~40 a second they blur into a buzz
            if (sound == UiSound.Tick)
            {
                long now = _clock.ElapsedMilliseconds;
                if (now - _lastTickMs < 24) return;
                _lastTickMs = now;
            }

            IntPtr buffer = Resolve(sound, template);
            if (buffer == IntPtr.Zero) return;
            try
            {
                PlaySound(buffer, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private IntPtr Resolve(UiSound sound, WheelTemplate template)
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
                if (_cache.TryGetValue(key, out var p)) return p;
                p = LoadWav(file);
                _cache[key] = p;
                if (p != IntPtr.Zero) return p;
            }

            string pack = string.IsNullOrWhiteSpace(s.Pack) ? "click" : s.Pack.Trim().ToLowerInvariant();
            if (pack == "none") return IntPtr.Zero;
            double volume = Math.Clamp(s.Volume, 0, 1);
            if (volume <= 0) return IntPtr.Zero;

            string synthKey = $"{pack}:{sound}:{volume:0.00}";
            if (_cache.TryGetValue(synthKey, out var buf)) return buf;
            buf = ToUnmanaged(Synthesize(pack, sound, volume));
            _cache[synthKey] = buf;
            return buf;
        }

        private static IntPtr LoadWav(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length < 44 || info.Length > MaxWavBytes) return IntPtr.Zero;
                byte[] bytes = File.ReadAllBytes(path);
                bool riff = bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                         && bytes[8] == 'W' && bytes[9] == 'A' && bytes[10] == 'V' && bytes[11] == 'E';
                return riff ? ToUnmanaged(bytes) : IntPtr.Zero;
            }
            catch (IOException) { return IntPtr.Zero; }
            catch (UnauthorizedAccessException) { return IntPtr.Zero; }
        }

        private static IntPtr ToUnmanaged(byte[] bytes)
        {
            IntPtr p = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            return p; // lives for the app's lifetime; the cache holds a handful of short clips
        }

        // ------------------------------------------------------------------ synthesis

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

            var samples = new short[n];
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
