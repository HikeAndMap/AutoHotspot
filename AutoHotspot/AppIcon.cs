namespace AutoHotspot;

/// <summary>Loads the embedded AutoHotspot.ico at a requested size.</summary>
internal static class AppIcon
{
    public static Icon Load(Size size)
    {
        using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AutoHotspot.ico")
            ?? throw new InvalidOperationException("Embedded AutoHotspot.ico is missing.");
        return new Icon(stream, size);
    }
}
