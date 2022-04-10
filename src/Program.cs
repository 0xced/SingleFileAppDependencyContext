using SingleFileAppDependencyContext;
using SingleFileAppDependencyContext.Strategies;

try
{
    var implementationType = args.Length == 0 ? "FindBundleSignature" : args[0];
    IJsonDeps implementation = implementationType switch
    {
        nameof(FindBundleSignature) => new FindBundleSignature(),
        nameof(CaptureAppHostLogs) => new CaptureAppHostLogs(),
        nameof(ParseExecutableFileFormat) => new ParseExecutableFileFormat(),
        _ => throw new ArgumentException($"Usage: {Path.GetFileName(Environment.GetCommandLineArgs()[0])} [{nameof(FindBundleSignature)}|{nameof(CaptureAppHostLogs)}|{nameof(ParseExecutableFileFormat)}]")
    };

    DependencyContext context;
    if (string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location))
    {
        Console.Out.WriteLine($"🏷 Using {implementation.GetType().Name}");
        var stopwatch = Stopwatch.StartNew();
        using var depsJsonStream = implementation.CreateJsonDepsStream(GetAppHostPath());
        Console.Out.WriteLine($"⏱ {stopwatch.Elapsed.TotalMilliseconds} ms");
        using var reader = new DependencyContextJsonReader();
        context = reader.Read(depsJsonStream);
    }
    else
    {
        Console.Out.WriteLine("🗂 Detected non single-file app => using DependencyContext.Default");
        context = DependencyContext.Default;
    }

    context.Dump(Console.Out);

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("💥 An unexpected error has occurred");
    Console.Error.WriteLine(exception);
    return 1;
}

static string GetAppHostPath()
{
#if NET6_0_OR_GREATER
    return Environment.ProcessPath!;
#else
    return Process.GetCurrentProcess().MainModule!.FileName!;
#endif
}
