using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>
    /// Microsoft Word, Excel and PowerPoint driven by automation. <see cref="DocumentEngine"/> decides when to use them,
    /// runs one job at a time and checks the result; every call here runs one job with its own time limit.
    /// </summary>
    public interface IOfficeAutomation
    {
        bool HasWord { get; }
        bool HasExcel { get; }
        bool HasPowerPoint { get; }

        /// <summary>
        /// Converts with Word (<see cref="DocumentKind.Text"/>, also PDF → editable document), Excel or PowerPoint.
        /// <paramref name="dstExt"/> is lower case without the dot.
        /// </summary>
        Task ConvertAsync(DocumentKind kind, string src, string dst, string dstExt);

        /// <summary>
        /// Opens with <paramref name="openPassword"/> (null: the file has none) and saves a copy with <paramref name="newPassword"/>
        /// and <paramref name="modifyPassword"/> ("" for none). Wrong passwords throw with a message for the user.
        /// </summary>
        Task ChangePasswordAsync(string src, string dst, string? openPassword, string newPassword, string modifyPassword);

        /// <summary>Records the presentation as an MP4 with PowerPoint (transitions, timings, narration kept).</summary>
        Task RecordVideoAsync(string src, string dst, int secondsPerSlide);
    }
}
