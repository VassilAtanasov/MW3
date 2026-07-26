using MW3.Game;

var smokeTest = args.Contains("--smoke");

string? screenshotPath = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--screenshot" && i + 1 < args.Length)
    {
        screenshotPath = args[i + 1];
        break;
    }
}

using var game = new WelcomeGame(exitAfterFirstDraw: smokeTest, screenshotPath: screenshotPath);
game.Run();
