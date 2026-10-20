using System.Collections.Generic;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>One recognized line: its text and the top/bottom of its words, in pixels of the recognized picture.</summary>
    public sealed record OcrTextLine(string Text, double Top, double Bottom);

    /// <summary>Text recognition. <see cref="OcrService"/> decodes the pictures and lays out the lines.</summary>
    public interface IOcrEngine
    {
        /// <summary>False when no recognition language is installed.</summary>
        bool IsAvailable { get; }

        /// <summary>Why <see cref="IsAvailable"/> is false and what to install, for the user.</summary>
        string UnavailableHint { get; }

        /// <summary>Longest edge the engine accepts; pictures are shrunk to it before recognition.</summary>
        int MaxImageDimension { get; }

        /// <summary>Lines in reading order. Lines without words are left out.</summary>
        Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(RasterImage image);
    }
}
