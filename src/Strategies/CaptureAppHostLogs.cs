namespace SingleFileAppDependencyContext.Strategies;

/// <summary>
/// A strategy that runs the app host with logs enabled by setting
/// <c>COREHOST_TRACE=1</c> and <c>COREHOST_TRACE_VERBOSITY=3</c> then reads stderr, looking for
/// the <c>DepsJson Offset</c> line containing the deps.json offset and size (in hexadecimal).
/// See https://github.com/dotnet/runtime/blob/v6.0.3/src/native/corehost/bundle/info.cpp#L51
/// <para />
/// The <c>DOTNET_STARTUP_HOOKS</c> environment variable is set to an invalid assembly (a single space)
/// to force aborting running the app before reaching the managed Main entry point. We don't want the app
/// to run, we just need to catch the app host logs which are written before reaching the managed code.
/// </summary>
internal class CaptureAppHostLogs : BaseJsonDeps
{
    public override Location GetJsonDepsLocation(string appHostPath)
    {
        var depsJsonRegex = new Regex(@"DepsJson Offset:\[([0-9a-fA-F]+)\] Size\[([0-9a-fA-F]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var startInfo = new ProcessStartInfo(appHostPath)
        {
            EnvironmentVariables =
            {
                ["COREHOST_TRACE"] = "1",
                ["COREHOST_TRACE_VERBOSITY"] = "3",
                // Force the app to exit with an exception before reaching the managed Main entrypoint
                // > Unhandled exception. System.ArgumentException: The startup hook simple assembly name ' ' is invalid. It must be a valid assembly name and it may not contain directory separator, space or comma characters and must not end with '.dll'.
                // >   at System.StartupHookProvider.ProcessStartupHooks()
                // See https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md
                ["DOTNET_STARTUP_HOOKS"] = " ",
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
        if (!process.WaitForExit(2000))
        {
            process.Kill();
        }

        if (depsJsonOffset.HasValue && depsJsonSize.HasValue)
        {
            return new Location(depsJsonOffset.Value, depsJsonSize.Value);
        }

        throw new Exception("The .deps.json location was not found in the AppHost logs");
    }
}