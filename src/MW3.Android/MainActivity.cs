using Android.Content.PM;
using Android.Views;
using Microsoft.Xna.Framework;
using MW3.Game;

namespace MW3.Android;

/// <summary>
/// The Android entry point: hosts the same <see cref="MW3Game"/> the desktop head runs.
/// </summary>
[Activity(
    Name = "com.vassilatanasov.mw3.MainActivity",
    Label = "MW3",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize)]
public sealed class MainActivity : AndroidGameActivity
{
    private MW3Game? _game;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _game = new MW3Game();
        var view = _game.Services.GetService(typeof(View)) as View;
        SetContentView(view);
        _game.Run();
    }

    // MonoGame does not surface the hardware back button through its own Keyboard state on every
    // device (confirmed on a physical MI Pad 4, Android 11), so the activity intercepts it here
    // and relays it into the game's input seam instead. Not calling base.OnBackPressed() means
    // Android's own back-stack handling never fires - ScreenManager decides pop vs. exit instead.
    public override void OnBackPressed()
    {
        _game?.NotifyBackButtonPressed();
    }

    protected override void OnDestroy()
    {
        _game?.Dispose();
        base.OnDestroy();
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
