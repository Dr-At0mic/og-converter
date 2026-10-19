using System;
using System.IO;
using System.Text;
using System.Windows;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>Paths are read from every kind of drag source: Explorer, IDE file trees (path text or file:// URLs).</summary>
    public static class DragDropVerification
    {
        public static void Run(string tempDir)
        {
            Console.WriteLine("[21] Drag sources");
            string folder = Path.Combine(tempDir, "My Folder #1");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "photo 100%.png");
            File.WriteAllText(file, "x");
            string uri = new Uri(file).AbsoluteUri;

            Check("Explorer file list", Data(DataFormats.FileDrop, new[] { file }), file);
            // VS Code: plain path text (several selected files are separated by CRLF)
            Check("Path text", Data(DataFormats.UnicodeText, file + "\r\n" + Path.Combine(folder, "missing.png")), file);
            Check("Quoted path text", Data(DataFormats.UnicodeText, $"\"{file}\""), file);
            Check("Folder path text", Data(DataFormats.UnicodeText, folder + "\\"), folder);
            Check("uri-list", Data("text/uri-list", "# comment\r\n" + uri), file);
            Check("VS Code style URI (e%3A)", Data("text/uri-list", uri.Replace(":/", "%3A/").Replace("file%3A///", "file:///")), file);
            Check("URL as UTF-16 stream", Data("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes(uri + "\0"))), file);

            if (DroppedFiles.GetPaths(Data(DataFormats.UnicodeText, "hello world")).Count != 0) throw new Exception("Plain text treated as a file");
            if (DroppedFiles.GetPaths(Data("text/uri-list", "https://example.com/a.png")).Count != 0) throw new Exception("Web URL treated as a file");
        }

        private static DataObject Data(string format, object value)
        {
            var d = new DataObject();
            d.SetData(format, value);
            return d;
        }

        private static void Check(string label, IDataObject data, string expected)
        {
            var paths = DroppedFiles.GetPaths(data);
            bool ok = paths.Count == 1 && string.Equals(paths[0], expected, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"     {label,-26} {(ok ? "OK" : "FAIL")} {(paths.Count > 0 ? paths[0] : "(none)")}");
            if (!ok) throw new Exception($"Drag source '{label}' not understood");
        }
    }
}
