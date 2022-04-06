namespace SingleFileAppDependencyContext.Strategies;

internal class CaptureAppHostLogs : IJsonDeps
{
    public Location GetJsonDepsLocation(string appHostPath)
    {
        var depsJsonRegex = new Regex(@"DepsJson Offset:\[([0-9a-fA-F]+)\] Size\[([0-9a-fA-F]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var startInfo = new ProcessStartInfo(appHostPath)
        {
            EnvironmentVariables =
            {
                ["COREHOST_TRACE"] = "1",
                ["COREHOST_TRACE_VERBOSITY"] = "3",
            },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = startInfo };
        long? depsJsonOffset = null;
        long? depsJsonSize = null;
        process.ErrorDataReceived += (sender, args) =>
        {
            var match = depsJsonRegex.Match(args.Data ?? "");
            if (match.Success)
            {
                var p = (Process)sender;
                p.CancelErrorRead();
                p.Kill();
                depsJsonOffset = Convert.ToInt64(match.Groups[1].Value, 16);
                depsJsonSize = Convert.ToInt64(match.Groups[2].Value, 16);
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(1000))
        {
            process.Kill();
        }

        if (depsJsonOffset.HasValue && depsJsonSize.HasValue)
        {
            return new Location(depsJsonOffset.Value, depsJsonSize.Value);
        }

        throw new Exception("The .deps.json location was not found in the AppHost logs");
    }

    public Stream CreateJsonDepsStream(string appHostPath)
    {
        var location = GetJsonDepsLocation(appHostPath);
        using var appHostFile = MemoryMappedFile.CreateFromFile(appHostPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        return appHostFile.CreateViewStream(location.Offset, location.Size, MemoryMappedFileAccess.Read);
    }
}