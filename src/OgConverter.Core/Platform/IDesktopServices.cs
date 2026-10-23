namespace OgConverter.Services
{
    /// <summary>The desktop around the app: clipboard, file manager, terminal, wallpaper and links.</summary>
    public interface IDesktopServices
    {
        /// <summary>Puts text on the clipboard. Callable from any thread.</summary>
        void SetClipboardText(string text);

        /// <summary>
        /// Puts the picture at <paramref name="picturePath"/> (decoded upright with <see cref="ImageCodec.TryLoad"/>) on the
        /// clipboard for apps that paste images, together with <paramref name="filePath"/> for apps that paste attachments
        /// (chats, mail). Throws when the picture can't be read. Callable from any thread.
        /// </summary>
        void SetClipboardImageAndFile(string picturePath, string filePath);

        /// <summary>Opens the file manager with <paramref name="path"/> selected.</summary>
        void RevealInFileManager(string path);

        /// <summary>Opens a new terminal window in <paramref name="directory"/>.</summary>
        void OpenTerminal(string directory);

        /// <summary>Opens a web address in the default browser.</summary>
        void OpenUrl(string url);

        /// <summary>True when the file carries a "downloaded from the internet" mark that makes the system warn before opening it.</summary>
        bool IsBlocked(string path);

        /// <summary>Removes that mark. Does nothing for files that aren't blocked.</summary>
        void Unblock(string path);

        /// <summary>
        /// Sets the desktop background. The system may read the file whenever it redraws the desktop, so pass a copy that
        /// stays put (JPG or PNG).
        /// </summary>
        void SetWallpaper(string imagePath);

        /// <summary>
        /// Shows <paramref name="iconFile"/> (an .ico on Windows, a PNG elsewhere) as <paramref name="folder"/>'s icon in the
        /// file manager, or its default icon again when it's null.
        /// </summary>
        void SetFolderIcon(string folder, string? iconFile);

        /// <summary>The custom icon file the file manager shows for <paramref name="folder"/>, or null for the default icon.</summary>
        string? GetFolderIcon(string folder);
    }
}
