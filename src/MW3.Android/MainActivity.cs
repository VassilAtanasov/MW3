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

    // MonoGame's own view consumes the hardware back key before it ever reaches OnBackPressed
    // (confirmed on a physical MI Pad 4, Android 11 - OnBackPressed never fired). DispatchKeyEvent
    // is the activity's first look at the event, ahead of the view hierarchy, so intercepting Back
    // here and returning true - without calling base.DispatchKeyEvent for it - both guarantees our
    // handler runs and keeps Android's own back-stack handling from finishing the activity;
    // ScreenManager decides pop vs. exit instead.
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null && e.KeyCode == Keycode.Back && e.Action == KeyEventActions.Down)
        {
            _game?.NotifyBackButtonPressed();
            return true;
        }

        return base.DispatchKeyEvent(e);
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
