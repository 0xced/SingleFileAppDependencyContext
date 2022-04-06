namespace SingleFileAppDependencyContext;

public static class DependencyContextExtensions
{
    public static void Dump(this DependencyContext context, TextWriter writer)
    {
        writer.WriteLine("Target");
        writer.WriteLine($"  Framework: {context.Target.Framework}");
        writer.WriteLine($"  Runtime: {context.Target.Runtime}");
        writer.WriteLine($"  RuntimeSignature: {context.Target.RuntimeSignature}");
        writer.WriteLine($"  IsPortable: {context.Target.IsPortable}");

        writer.WriteLine("RuntimeLibraries");
        foreach (var runtimeLibrary in context.RuntimeLibraries.OrderBy(e => e.Name))
        {
            writer.WriteLine($"  {runtimeLibrary.Name} {runtimeLibrary.Version} ({runtimeLibrary.Type})");

            if (runtimeLibrary.ResourceAssemblies.Count > 0)
            {
                writer.WriteLine("    ResourceAssemblies");
                foreach (var resourceAssembly in runtimeLibrary.ResourceAssemblies)
                {
                    writer.WriteLine($"      [{resourceAssembly.Locale}] {runtimeLibrary.Path}");
                }
            }

            if (runtimeLibrary.RuntimeAssemblyGroups.Count > 0)
            {
                writer.WriteLine("    RuntimeAssemblyGroups");
                PrintAssetGroups(runtimeLibrary.RuntimeAssemblyGroups, writer);
            }

            if (runtimeLibrary.NativeLibraryGroups.Count > 0)
            {
                writer.WriteLine("    NativeLibraryGroups");
                PrintAssetGroups(runtimeLibrary.NativeLibraryGroups, writer);
            }
        }
    }

    private static void PrintAssetGroups(IEnumerable<RuntimeAssetGroup> assetGroups, TextWriter writer)
    {
        foreach (var assetGroup in assetGroups)
        {
            writer.WriteLine($"      {(string.IsNullOrEmpty(assetGroup.Runtime) ? "any" : assetGroup.Runtime)}");

            if (assetGroup.AssetPaths.Count > 0)
            {
                writer.WriteLine("        AssetPaths");
                foreach (var assetPath in assetGroup.AssetPaths)
                {
                    writer.WriteLine($"          {assetPath}");
                }
            }

            if (assetGroup.RuntimeFiles.Count > 0)
            {
                writer.WriteLine("        RuntimeFiles");
                foreach (var runtimeFile in assetGroup.RuntimeFiles)
                {
                    writer.WriteLine($"          {runtimeFile.Path} {runtimeFile.AssemblyVersion} ({runtimeFile.FileVersion})");
                }
            }
        }
    }
}