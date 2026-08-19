using System.Diagnostics;

namespace MW3.Core.Tests;

/// <summary>
/// D-71's other half: a golden snapshot hash, pinned against a fixed scripted match, that catches
/// any divergence a <see cref="DeterminismSourceScanTests"/> token list did not know to look for.
/// </summary>
public class SnapshotHashTests
{
    /// <summary>The exact scenario <c>tools/SnapshotHashProbe/Program.cs</c> builds, kept in step by hand so both sides hash the same match.</summary>
    private static MatchSnapshot BuildFixedScenario()
    {
        var match = new Match(MapCatalog.Small);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 5));
        match.Advance(1000);

        return MatchSnapshotBuilder.Build(match, match.HumanPlayer);
    }

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

    [Fact]
    public void Compute_DoesNotUseObjectOrStringGetHashCode()
    {
        // Belt to the design's own braces: the implementation is inspectable, so this fails loudly
        // if a future edit reaches for the randomized-per-process hash this whole test exists to
        // avoid, instead of failing only in CI where two processes happen to disagree.
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, "src", "MW3.Protocol", "SnapshotHash.cs"))
            .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

        Assert.DoesNotContain(lines, line => line.Contains(".GetHashCode()", StringComparison.Ordinal));
    }

    [Fact]
    public void Compute_OfTheFixedScriptedMatch_MatchesTheCommittedGoldenValue()
    {
        var snapshot = BuildFixedScenario();

        var hash = SnapshotHash.Compute(snapshot);

        Assert.Equal(0x6a57c3b7b4217377UL, hash);
    }

    [Fact]
    public void Compute_AgreesWithASeparateProcessComputingTheSameFixedScenario()
    {
        var inProcess = SnapshotHash.Compute(BuildFixedScenario());

        var probeProject = Path.Combine(RepositoryRoot, "tools", "SnapshotHashProbe", "SnapshotHashProbe.csproj");
        var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{probeProject}\" --no-launch-profile -c Debug")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepositoryRoot,
        };

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "The hash probe process did not exit within 120 seconds.");
        Assert.Equal(0, process.ExitCode);

        var crossProcess = ulong.Parse(output.Trim(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(inProcess, crossProcess);
        Assert.True(string.IsNullOrEmpty(error), $"The hash probe wrote to stderr: {error}");
    }
}
