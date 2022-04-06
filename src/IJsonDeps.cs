namespace SingleFileAppDependencyContext;

public interface IJsonDeps
{
    Location GetJsonDepsLocation(string appHostPath);
    Stream CreateJsonDepsStream(string appHostPath);
}

public struct Location
{
    public Location(long offset, long size)
    {
        Offset = offset;
        Size = size;
    }

    public long Offset { get; }
    public long Size { get; }

    public override string ToString() => $"0x{Offset:x16} (Size={Size})";
}
