namespace SingleFileAppDependencyContext;

/// <summary>
/// Identifies the location of a bundled file within the app host.
/// The <see cref="Offset"/> is the number of bytes since the beginning of the app host file.
/// The <see cref="Size"/> is the length in bytes of the bundled file.
/// </summary>
public struct Location
{
    public Location(long offset, long size)
    {
        Offset = offset;
        Size = size;
    }

    /// <summary>
    /// The number of bytes since the beginning of the app host file.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// The length in bytes of the bundled file.
    /// </summary>
    public long Size { get; }

    public override string ToString() => $"0x{Offset:x16} (Size={Size})";
}