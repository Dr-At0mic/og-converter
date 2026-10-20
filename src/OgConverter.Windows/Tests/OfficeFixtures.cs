using System;
using System.Threading;

namespace OgConverter.Tests
{
    /// <summary>Test documents only Word and PowerPoint can make, created through COM automation.</summary>
    internal static class OfficeFixtures
    {
        /// <summary>White text over a full-page picture placed behind the text in the header (like the user's document).</summary>
        public static void CreateHeaderBackgroundDocx(string path, string imagePath)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                dynamic? word = null;
                try
                {
                    word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
                    word.Visible = false;
                    word.DisplayAlerts = 0;
                    dynamic doc = word.Documents.Add();
                    doc.Content.Text = "Neurons\rIf you are reading this article, neurons in your brain are working.";
                    doc.Content.Font.Color = 16777215; // white
                    dynamic shape = doc.Sections(1).Headers(1).Shapes.AddPicture(imagePath, false, true, 0, 0, 595, 842);
                    shape.WrapFormat.Type = 5;         // wdWrapBehind
                    shape.RelativeHorizontalPosition = 1;
                    shape.RelativeVerticalPosition = 1;
                    doc.SaveAs2(path, 16);
                    doc.Close(0);
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    try { word?.Quit(0); } catch { }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw new Exception("Couldn't create the watermark test document: " + error.Message, error);
        }

        /// <summary>Creates a two-slide deck with PowerPoint so the presentation path can be tested.</summary>
        public static void CreatePresentation(string path, string picture)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                bool wasRunning = System.Diagnostics.Process.GetProcessesByName("POWERPNT").Length > 0;
                dynamic? app = null;
                try
                {
                    app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                    dynamic pres = app.Presentations.Add(0); // msoFalse: no window
                    dynamic slide = pres.Slides.Add(1, 1);   // ppLayoutTitle
                    slide.Shapes[1].TextFrame.TextRange.Text = "OG Converter test deck";
                    slide.NotesPage.Shapes.Placeholders[2].TextFrame.TextRange.Text = "Remember the demo";
                    dynamic second = pres.Slides.Add(2, 2);  // ppLayoutText
                    second.Shapes.AddPicture(picture, 0, -1, 100, 100);
                    pres.SaveAs(path, 24);
                    pres.Close();
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    try { if (app is not null && !wasRunning) { dynamic a = app; if ((int)a.Presentations.Count == 0) a.Quit(); } } catch { }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw new Exception("Couldn't create a test presentation: " + error.Message, error);
        }
    }
}
