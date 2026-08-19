using System.Text.RegularExpressions;

namespace MW3.Core.Tests;

/// <summary>
/// D-71: half of determinism's two-part enforcement. Scans every source line under
/// <c>MW3.Core</c> and <c>MW3.Protocol</c> for a token that reads ambient state or performs a
/// floating-point operation IEEE-754 does not require be correctly rounded - either of which would
/// break the property <see cref="SnapshotDiffApplyPropertyTests"/> rests on: that a run is
/// reproducible. <c>MW3.Protocol</c> is in scope because D-68 moved the position and progress math
/// there. The other half is <see cref="SnapshotHashTests"/>'s golden hash, which catches whatever
/// this list does not know to look for.
/// </summary>
public class DeterminismSourceScanTests
{
    // Word-boundary tokens, matched as whole identifiers so e.g. `DateTimeOffset` doesn't also
    // trip on a bare `DateTime` scan of the same list, and unrelated identifiers containing these
    // as a substring (there are none today) never would either.
    private static readonly string[] _bannedTokens =
    {
        "DateTime",
        "DateTimeOffset",
        "Stopwatch",
        "Environment.TickCount",
        "Random",
        "Guid.NewGuid",
        "Math.Pow",
        "Math.Sin",
        "Math.Cos",
        "Math.Tan",
        "Math.Exp",
        "Math.Log",
        "Math.Atan2",
        "Math.Cbrt",
    };

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MW3.slnx")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                throw new InvalidOperationException("Could not locate the repository root (MW3.slnx) above the test output directory.");
            }

            return directory.FullName;
        }
    }

    public static IEnumerable<object[]> ScannedDirectories()
    {
        yield return new object[] { "MW3.Core" };
        yield return new object[] { "MW3.Protocol" };
    }

    [Theory]
    [MemberData(nameof(ScannedDirectories))]
    public void SourceFiles_ContainNoNonDeterministicApi_AndAFailureNamesTheFileAndLine(string projectName)
    {
        var directory = Path.Combine(RepositoryRoot, "src", projectName);
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            var relativePath = Path.GetRelativePath(RepositoryRoot, file);

            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("///", StringComparison.Ordinal) || code.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var token in _bannedTokens)
                {
                    if (ContainsAsIdentifierOrMember(lines[i], token))
                    {
                        violations.Add($"{relativePath}:{i + 1}: uses banned non-deterministic API '{token}': {code}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, "Non-deterministic API found:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Whether <paramref name="line"/> uses <paramref name="token"/> as a standalone identifier (or
    /// dotted member access, for tokens like <c>Math.Pow</c>) rather than as part of a longer
    /// identifier - so <c>RandomSeed</c> would not trip a scan for <c>Random</c> (it does not appear
    /// in this repo, but the guard is cheap and matches <c>ProtocolBoundaryTests</c>'s own
    /// whole-word approach).
    /// </summary>
    private static bool ContainsAsIdentifierOrMember(string line, string token)
    {
        var pattern = $@"(?<![A-Za-z0-9_.]){Regex.Escape(token)}(?![A-Za-z0-9_])";
        return Regex.IsMatch(line, pattern, RegexOptions.None);
    }
}
