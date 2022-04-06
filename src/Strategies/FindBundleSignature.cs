namespace SingleFileAppDependencyContext.Strategies;

internal class FindBundleSignature : IJsonDeps
{
    static byte[] bundleSignature =
    {
        // 32 bytes represent the bundle signature: SHA-256 for ".net core bundle"
        // The first byte is actually 0x8b but we don't want to accidentally have a second place where the bundle
        // signature can appear in the single file application so the first byte is set in the static constructor
        // See https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs#L216-L222
        0x00, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    };

    static FindBundleSignature()
    {
        bundleSignature[0] = 0x8b;
    }

    public Location GetJsonDepsLocation(string appHostPath)
    {
        var (appHostFile, location) = GetJsonDepsInfo(appHostPath);
        appHostFile.Dispose();
        return location;
    }

    public Stream CreateJsonDepsStream(string appHostPath)
    {
        var (appHostFile, location) = GetJsonDepsInfo(appHostPath);
        var stream = appHostFile.CreateViewStream(location.Offset, location.Size, MemoryMappedFileAccess.Read);
        appHostFile.Dispose();
        return stream;
    }

    private static (MemoryMappedFile, Location) GetJsonDepsInfo(string appHostPath)
    {
        var appHostFile = MemoryMappedFile.CreateFromFile(appHostPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var appHostStream = appHostFile.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
        var bundleSignatureIndex = SearchBundleSignature(appHostStream);
        if (bundleSignatureIndex == -1)
        {
            throw new Exception("The bundle signature was not found");
        }

        var location = appHostStream.GetDepsJsonLocation(bundleSignatureIndex - 8);
        return (appHostFile, location);
    }

    private static int SearchBundleSignature(Stream stream)
    {
        var m = 0;
        var i = 0;

        var length = stream.Length;
        while (m + i < length)
        {
            stream.Position = m + i;
            if (bundleSignature[i] == stream.ReadByte())
            {
                if (i == bundleSignature.Length - 1)
                {
                    return m;
                }
                i++;
            }
            else
            {
                m += i == 0 ? 1 : i;
                i = 0;
            }
        }

        return -1;
    }
}