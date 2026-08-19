namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 D-57: <c>MW3.Protocol</c> is a separate project because a missing project reference is
/// the only version of "the client contains no rules" a compiler can check. These tests are the
/// belt to that braces - they fail on the day someone adds the reference that would make the rules
/// reachable from the wire contract, with a message saying why rather than a link error three
/// features later.
/// </summary>
public class ProtocolBoundaryTests
{
    private static readonly string[] _bannedTypeNames =
    {
        "Match",
        "MatchRunner",
        "AiBrain",
        "CombatResolver",
        "LevelTable",
        "MoraleTable",
        "ForgeTable",
        "PathCalculator",
        "TravelTimeCalculator",
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

    private static string ProtocolSourceDirectory => Path.Combine(RepositoryRoot, "src", "MW3.Protocol");

    [Fact]
    public void TheProtocolAssembly_ReferencesNeitherTheRulesNorAnythingElse()
    {
        var referenced = typeof(MatchSnapshot).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        // Nothing but the framework facade it targets: no MW3 assembly, and no third party either.
        Assert.All(
            referenced,
            name => Assert.True(
                name is "netstandard" || name!.StartsWith("System", StringComparison.Ordinal),
                $"MW3.Protocol references '{name}', which is neither the framework nor allowed."));
    }

    [Fact]
    public void TheProtocolProject_TargetsNetStandard21AndTakesNoDependencies()
    {
        var text = File.ReadAllText(Path.Combine(ProtocolSourceDirectory, "MW3.Protocol.csproj"));

        Assert.Contains("<TargetFramework>netstandard2.1</TargetFramework>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoProtocolSourceFile_NamesARuleOrATable()
    {
        // The assembly check above cannot see a name that only appears in prose, and a doc comment
        // that says "MatchOutcome is what Match.Advance sets" is fine. What is not fine is a
        // reference the compiler resolved, so this looks for one in code: a `using MW3.Core`, or a
        // banned name used as a type.
        foreach (var file in Directory.EnumerateFiles(ProtocolSourceDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var lines = File.ReadAllLines(file);
            var name = Path.GetFileName(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var code = line.TrimStart();
                if (code.StartsWith("///", StringComparison.Ordinal) || code.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(
                    code.StartsWith("using MW3.", StringComparison.Ordinal),
                    $"{name} line {i + 1} takes a dependency on the rules: {code}");

                foreach (var banned in _bannedTypeNames)
                {
                    Assert.False(
                        line.Contains(banned + ".", StringComparison.Ordinal) || line.Contains(banned + " ", StringComparison.Ordinal),
                        $"{name} line {i + 1} references '{banned}', which lives in MW3.Core: {code}");
                }
            }
        }
    }

    [Theory]
    [InlineData("MapPoint")]
    [InlineData("MapObstacle")]
    [InlineData("ArmyPath")]
    [InlineData("BaseType")]
    [InlineData("BaseActionKind")]
    [InlineData("BaseActionAvailability")]
    [InlineData("MatchOutcome")]
    [InlineData("SendStrength")]
    [InlineData("PlayerControllerKind")]
    public void EachMovedValueType_IsDeclaredInTheProtocolAssemblyAndNowhereElse(string typeName)
    {
        // D-67: moved, not copied. A duplicate declaration plus a mapping layer is the drift class
        // #68, phase 5's morale patch and D-45 each had to close once, and it is rejected here.
        var protocolType = typeof(MatchSnapshot).Assembly.GetType("MW3.Protocol." + typeName);
        Assert.NotNull(protocolType);

        Assert.Null(typeof(Match).Assembly.GetType("MW3.Core." + typeName));
        Assert.False(
            File.Exists(Path.Combine(RepositoryRoot, "src", "MW3.Core", typeName + ".cs")),
            $"{typeName} still has a source file in MW3.Core.");
    }
}
