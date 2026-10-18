using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OgConverter.Services
{
    public class SoundService
    {
        private const uint SND_ASYNC = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;
        private const uint SND_MEMORY = 0x0004;

        [DllImport("winmm.dll", EntryPoint = "PlaySound", SetLastError = true)]
        private static extern bool PlaySound(byte[] ptrToSound, IntPtr hmod, uint fdwSound);

        private static readonly Lazy<SoundService> _instance = new(() => new SoundService());
        public static SoundService Instance => _instance.Value;

        private readonly byte[] _tickWav;
        private readonly byte[] _switchWav;

        public bool IsEnabled { get; set; } = true;

        public SoundService()
        {
            _tickWav = GenerateTickWav(frequency: 2400, subFreq: 650, durationMs: 9, decayRate: 350.0);
            _switchWav = GenerateTickWav(frequency: 1800, subFreq: 3200, durationMs: 14, decayRate: 220.0);
        }

        public void PlayTick()
        {
            if (!IsEnabled) return;
            try
            {
                PlaySound(_tickWav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
            catch { }
        }

        public void PlaySwitch()
        {
            if (!IsEnabled) return;
            try
            {
                PlaySound(_switchWav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
            catch { }
        }

        private static byte[] GenerateTickWav(double frequency, double subFreq, int durationMs, double decayRate)
        {
            int sampleRate = 44100;
            int numSamples = (int)(sampleRate * (durationMs / 1000.0));
            short[] samples = new short[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                double t = (double)i / sampleRate;
                double envelope = Math.Exp(-decayRate * t);

                // High click tone + subtle body click
                double tone1 = Math.Sin(2.0 * Math.PI * frequency * t);
                double tone2 = Math.Sin(2.0 * Math.PI * subFreq * t);
                double val = (0.7 * tone1 + 0.3 * tone2) * envelope;

                // Soft volume: peak ~7500 out of 32767
                samples[i] = (short)Math.Clamp(val * 8500.0, short.MinValue, short.MaxValue);
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + (numSamples * 2));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt subchunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // Subchunk1Size
            bw.Write((short)1); // PCM
            bw.Write((short)1); // Mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // ByteRate = sampleRate * 1 * 16/8
            bw.Write((short)2); // BlockAlign
            bw.Write((short)16); // BitsPerSample

            // data subchunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(numSamples * 2);

            foreach (var s in samples)
            {
                bw.Write(s);
            }

            bw.Flush();
            return ms.ToArray();
        }
    }
}
