namespace OgConverter.Models
{
    /// <summary>A rectangle in image pixels (crop areas). UI toolkits convert their own rectangle types at the boundary.</summary>
    public readonly record struct PixelRect(int X, int Y, int Width, int Height);
}
