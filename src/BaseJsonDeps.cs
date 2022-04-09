namespace SingleFileAppDependencyContext;

public abstract class BaseJsonDeps : IJsonDeps
{
    public abstract Location GetJsonDepsLocation(string appHostPath);

    public Stream CreateJsonDepsStream(string appHostPath)
    {
        var location = GetJsonDepsLocation(appHostPath);
        using var appHostFile = MemoryMappedFile.CreateFromFile(appHostPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        return appHostFile.CreateViewStream(location.Offset, location.Size, MemoryMappedFileAccess.Read);
    }
}