namespace OgConverter.Tests
{
    /// <summary>The self-test sections that need Office, WPF or Windows itself, in the order <see cref="VerificationRunner"/> runs them.</summary>
    public static class WindowsSelfTests
    {
        public static PlatformSelfTests Suites { get; } = new()
        {
            Documents = DocumentVerification.RunAsync,
            Compression = CompressionVerification.RunAsync,
            Edit = EditVerification.RunAsync,
            DragDrop = DragDropVerification.Run,
            Everyday = EverydayVerification.RunAsync
        };
    }
}
