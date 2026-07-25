using MW3.Game;

var smokeTest = args.Contains("--smoke");
using var game = new WelcomeGame(exitAfterFirstDraw: smokeTest);
game.Run();
