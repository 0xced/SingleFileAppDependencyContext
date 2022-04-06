using System.Runtime.InteropServices;
using ELFSharp.ELF;
using ELFSharp.MachO;
using PeNet;

namespace SingleFileAppDependencyContext.Strategies;

internal class ParseExecutableFileFormat : IJsonDeps
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

        long bundleHeaderFileOffset;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            using var elfFile = ELFReader.Load<ulong>(appHostStream, shouldOwnStream: false);
            var dataSection = elfFile.Sections.FirstOrDefault(e => e.Name == ".data") ?? throw new Exception(".data section not found");
            // 16 was found by inspecting ~/.nuget/packages/microsoft.netcore.app.host.osx-x64/6.0.3/runtimes/osx-x64/native/singlefilehost
            // It could be different for other app host versions
            bundleHeaderFileOffset = (long)(dataSection.Offset + 16);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // shouldOwnStream seems to be inverted for Mach-O files :-/
            var machOFile = MachOReader.Load(appHostStream, shouldOwnStream: true);
            bundleHeaderFileOffset = GetMachOBundleHeaderFileOffset(machOFile);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var peFile = new PeFile(appHostStream);
            var dataSection = peFile.ImageSectionHeaders?.FirstOrDefault(e => e.Name == ".data") ?? throw new Exception(".data section not found");
            // 0x5C60 was found by inspecting ~/.nuget/packages/microsoft.netcore.app.host.osx-x64/6.0.3/runtimes/osx-x64/native/singlefilehost
            // It could be different for other app host versions
            bundleHeaderFileOffset = dataSection.PointerToRawData + 0x5C60;
        }
        else
        {
            throw new PlatformNotSupportedException("Only Linux, macOS and Windows platforms are supported.");
        }

        return (appHostFile, appHostStream.GetDepsJsonLocation(bundleHeaderFileOffset));
    }

    private static long GetMachOBundleHeaderFileOffset(MachO machOFile)
    {
        var symbolTables = machOFile.GetCommandsOfType<SymbolTable>();
        var segments = machOFile.GetCommandsOfType<Segment>();
        var dataSegment = segments.FirstOrDefault(e => e.Name == "__DATA") ?? throw new Exception("__DATA segment not found");
        var dataSection = dataSegment.Sections.FirstOrDefault(e => e.Name == "__data") ?? throw new Exception("__data section not found");;

        const string symbolName = "__ZZN15bundle_marker_t13header_offsetEvE11placeholder";
        var bundleHeaderOffsetSymbol = symbolTables.SelectMany(e => e.Symbols).Cast<Symbol?>().FirstOrDefault(e => e?.Name == symbolName);
        if (!bundleHeaderOffsetSymbol.HasValue)
        {
            // 96 was found by inspecting ~/.nuget/packages/microsoft.netcore.app.host.osx-x64/6.0.3/runtimes/osx-x64/native/singlefilehost
            // It could be different for other app host versions
            return dataSection.Offset + 96;
        }
        return bundleHeaderOffsetSymbol.Value.Value - (long)(dataSection.Address - dataSection.Offset);
    }
}