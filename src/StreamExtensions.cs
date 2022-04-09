namespace SingleFileAppDependencyContext;

internal static class StreamExtensions
{
    /// <summary>
    /// Get the <see cref="Location"/> of the bundled deps.json file within a single file app host readable through the <paramref name="appHostStream"/>.
    /// </summary>
    /// <param name="appHostStream">A <see cref="Stream"/> for reading the single file app host.</param>
    /// <param name="bundleHeaderFileOffset">The offset in bytes of the bundle header. See https://github.com/dotnet/designs/blob/main/accepted/2020/single-file/bundler.md</param>
    /// <returns>The <see cref="Location"/> of the bundled deps.json file within a single file app host readable through the <paramref name="appHostStream"/>.</returns>
    /// <remarks>The <see cref="appHostStream"/> must be seekable.</remarks>
    public static Location GetDepsJsonLocation(this Stream appHostStream, long bundleHeaderFileOffset)
    {
        using var appHostReader = new BinaryReader(appHostStream);
        appHostReader.BaseStream.Position = bundleHeaderFileOffset;
        var bundleHeaderOffset = appHostReader.ReadInt64();
        if (bundleHeaderOffset == 0)
        {
            throw new Exception("Not a single-file app");
        }
        if (bundleHeaderOffset < 0 || bundleHeaderOffset >= appHostStream.Length)
        {
            throw new Exception($"The bundle header offset ({bundleHeaderOffset}) is out of bound [0, {appHostStream.Length}]");
        }

        // See https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs#L32-L39
        // and https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs#L144-L155
        appHostReader.BaseStream.Position = bundleHeaderOffset;
        var majorVersion = appHostReader.ReadUInt32();
        _ = appHostReader.ReadUInt32(); // minorVersion
        var numEmbeddedFiles = appHostReader.ReadInt32();
        _ = appHostReader.ReadString(); // bundleId
        if (majorVersion >= 2)
        {
            var depsJsonOffset = appHostReader.ReadInt64();
            var depsJsonSize = appHostReader.ReadInt64();
            return new Location(depsJsonOffset, depsJsonSize);
        }

        // For version < 2 all the file entries must be enumerated until the `DepsJson` type is found
        // See https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileEntry.cs#L43-L54
        for (var i = 0; i < numEmbeddedFiles; i++)
        {
            var offset = appHostReader.ReadInt64();
            var size = appHostReader.ReadInt64();
            var type = appHostReader.ReadByte();
            _ = appHostReader.ReadString(); // relativePath
            if (type == 3)
            {
                // type 3 is the .deps.json configuration file
                // See https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileType.cs#L17
                return new Location(offset, size);
            }
        }

        throw new Exception("The .deps.json location was not found in the manifest");
    }
}