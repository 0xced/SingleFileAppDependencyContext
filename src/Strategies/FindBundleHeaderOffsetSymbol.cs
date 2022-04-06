using ELFSharp.MachO;

namespace SingleFileAppDependencyContext.Strategies;

internal class FindBundleHeaderOffsetSymbol : IJsonDeps
{
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

        var machOFile = MachOReader.Load(appHostStream, shouldOwnStream: true);
        var symbolTables = machOFile.GetCommandsOfType<SymbolTable>();
        var segments = machOFile.GetCommandsOfType<Segment>();
        var dataSegment = segments.Single(e => e.Name == "__DATA");
        var dataSection = dataSegment.Sections.Single(e => e.Name == "__data");

        const string symbolName = "__ZZN15bundle_marker_t13header_offsetEvE11placeholder";
        var bundleHeaderOffsetSymbol = symbolTables.SelectMany(e => e.Symbols).SingleOrDefault(e => e.Name == symbolName);
        if (bundleHeaderOffsetSymbol.Name == default && bundleHeaderOffsetSymbol.Value == default)
        {
            throw new Exception($"The bundle header offset symbol was not found ({symbolName})");
        }

        var bundleHeaderFileOffset = bundleHeaderOffsetSymbol.Value - (long)(dataSection.Address - dataSection.Offset);
        return (appHostFile, appHostStream.GetDepsJsonLocation(bundleHeaderFileOffset));
    }
}