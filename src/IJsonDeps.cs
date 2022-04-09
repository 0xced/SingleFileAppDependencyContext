namespace SingleFileAppDependencyContext;

/// <summary>
/// Defines how to get the <see cref="Location"/> and a <see cref="Stream"/>
/// of the bundled deps.json file within a single file app host.
/// </summary>
public interface IJsonDeps
{
    /// <summary>
    /// Get the <see cref="Location"/> of the bundled deps.json file within a single file app host.
    /// </summary>
    /// <param name="appHostPath">The path to the single file app host.</param>
    /// <returns>The <see cref="Location"/> of the deps.json file within the single file app host at <paramref name="appHostPath"/>.</returns>
    Location GetJsonDepsLocation(string appHostPath);

    /// <summary>
    /// Get a <see cref="Stream"/> for reading the bundled deps.json file within a single file app host.
    /// </summary>
    /// <param name="appHostPath">The path to the single file app host.</param>
    /// <returns>A <see cref="Stream"/> for reading the bundled deps.json file within a single file app host at <paramref name="appHostPath"/>.</returns>
    Stream CreateJsonDepsStream(string appHostPath);
}