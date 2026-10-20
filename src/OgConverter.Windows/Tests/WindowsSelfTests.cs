using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter.Tests
{
    /// <summary>What the portable self-test needs from Windows: Office test files, the HEIF encoder, the clipboard, Mark of the Web, WPF.</summary>
    public static class WindowsSelfTests
    {
        public static PlatformSelfTests Suites { get; } = new()
        {
            DragDrop = DragDropVerification.Run,
            EditDialogs = CheckTrimTimes,
            CreateHeaderBackgroundDocx = OfficeFixtures.CreateHeaderBackgroundDocx,
            // LibreOffice can convert a deck, but only PowerPoint makes the test deck here
            CreatePresentation = DocumentEngine.HasPowerPoint ? OfficeFixtures.CreatePresentation : null,
            WriteHeicAsync = TryWriteHeicAsync,
            ReadClipboardText = () =>
            {
                string text = "";
                RunSta(() => text = Clipboard.GetText());
                return text;
            },
            ReadClipboardImageAndFile = () =>
            {
                bool image = false, file = false;
                RunSta(() => { image = Clipboard.ContainsImage(); file = Clipboard.ContainsFileDropList(); });
                return (image, file);
            },
            // Mark of the Web, as a browser writes it
            MarkAsDownloaded = path => File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n")
        };

        private static void CheckTrimTimes()
        {
            foreach (var (text, expected) in new[] { ("65.5", 65.5), ("1:05", 65.0), ("1:02:03", 3723.0) })
            {
                if (!TrimDialog.TryParseTime(text, out double t) || Math.Abs(t - expected) > 0.001) throw new Exception($"Time '{text}' parsed as {t}");
            }
            if (TrimDialog.TryParseTime("abc", out _)) throw new Exception("Invalid time accepted");
        }

        /// <summary>HEIC through Windows' own encoder (HEIF + HEVC extensions); false when they aren't installed.</summary>
        private static async Task<bool> TryWriteHeicAsync(string path)
        {
            try
            {
                byte[] px = new byte[64 * 48 * 4];
                for (int i = 0; i < px.Length; i += 4) { px[i] = 40; px[i + 1] = 120; px[i + 2] = 220; px[i + 3] = 255; }
                File.WriteAllBytes(path, Array.Empty<byte>());
                var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                using (var stream = await file.OpenAsync(global::Windows.Storage.FileAccessMode.ReadWrite))
                {
                    var encoder = await global::Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(global::Windows.Graphics.Imaging.BitmapEncoder.HeifEncoderId, stream);
                    encoder.SetPixelData(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, 64, 48, 96, 96, px);
                    await encoder.FlushAsync();
                }
                if (new FileInfo(path).Length > 0) return true;
                Console.WriteLine("     (HEIC encoder wrote nothing)");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"     (HEIC encoder: {ex.GetType().Name}: {ex.Message.Trim()})");
                return false;
            }
        }

        /// <summary>The clipboard needs an STA thread; the self-test runs on the thread pool.</summary>
        private static void RunSta(Action action)
        {
            Exception? error = null;
            var t = new System.Threading.Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw error;
        }
    }
}
