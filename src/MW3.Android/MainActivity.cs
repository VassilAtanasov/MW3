using Android.Content.PM;
using Android.Views;
using Microsoft.Xna.Framework;
using MW3.Game;

namespace MW3.Android;

/// <summary>
/// The Android entry point: hosts the same <see cref="WelcomeGame"/> the desktop head runs.
/// </summary>
[Activity(
    Name = "com.vassilatanasov.mw3.MainActivity",
    Label = "MW3",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize)]
public sealed class MainActivity : AndroidGameActivity
{
    private WelcomeGame? _game;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _game = new WelcomeGame();
        var view = _game.Services.GetService(typeof(View)) as View;
        SetContentView(view);
        _game.Run();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _game?.Dispose();
        }

        base.Dispose(disposing);
    }
}
