namespace MW3.Core.Tests;

public class CoreConstraintsTests
{
    private static readonly string[] _bannedTokens =
    {
        "DateTime",
        "Stopwatch",
        "Environment.TickCount",
        "Random",
        "Microsoft.Xna",
        "MonoGame",
    };

    [Fact]
    public void SourceFiles_ContainNoWallClockRandomnessOrEngineTypeReferences()
    {
        foreach (var file in Directory.EnumerateFiles(CoreSourceDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (var token in _bannedTokens)
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} references banned token '{token}'.");
            }
        }
    }

    [Fact]
    public void CoreProject_StillTargetsNetStandard21()
    {
        var csproj = Path.Combine(CoreSourceDirectory, "MW3.Core.csproj");

        var text = File.ReadAllText(csproj);
        Assert.Contains("<TargetFramework>netstandard2.1</TargetFramework>", text);
    }

    private static string CoreSourceDirectory
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

            return Path.Combine(directory.FullName, "src", "MW3.Core");
        }
    }
}
