using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;

namespace Greenlight.BonsaiClient.UnitTests;

/// <summary>Builds the snapshots the tests feed the gardener.</summary>
internal static class Snapshots
{
    public static GreenlightBuild Passed(long id, GreenlightProvider provider = GreenlightProvider.GitHub) =>
        Build(id, GreenlightBuildStatus.Completed, GreenlightBuildResult.Succeeded, provider);

    public static GreenlightBuild Failed(long id) =>
        Build(id, GreenlightBuildStatus.Completed, GreenlightBuildResult.Failed);

    public static GreenlightBuild Running(long id) =>
        Build(id, GreenlightBuildStatus.InProgress, GreenlightBuildResult.Unknown);

    public static GreenlightBuild Build(long id, GreenlightBuildStatus status, GreenlightBuildResult result,
        GreenlightProvider provider = GreenlightProvider.GitHub) =>
        new(id, "ci", "proj", "main", status, result, DateTimeOffset.Now, DateTimeOffset.Now, "Jamie",
            "https://example.invalid", provider, false);

    public static GreenlightSnapshot With(GreenlightStatus status, params GreenlightBuild[] builds) =>
        new(status, false, "", [], builds, [], [], DateTimeOffset.Now, "1.0");

    /// <summary>A snapshot carrying <paramref name="count"/> passing builds numbered from <paramref name="from"/>.</summary>
    public static GreenlightSnapshot Passes(int from, int count, GreenlightStatus status = GreenlightStatus.Green) =>
        With(status, Enumerable.Range(from, count).Select(i => Passed(i)).ToArray());
}
