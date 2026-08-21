using Android.Util;
using MW3.Core;
using MW3.Transport;

namespace MW3.Android;

/// <summary>
/// FR-5: resolves which gateway factory this launch plays through - the Intent extra, else a
/// persisted address, else local play (D-83) - and reports the chosen mode to logcat, never on
/// screen (D-85). Kept out of <see cref="MainActivity"/> so the composition root stays a composition
/// root: it resolves an address, calls this, and constructs <c>MW3Game</c>. Holds no probe logic
/// itself - <see cref="ServerPreflightResolver"/> in <c>MW3.Transport</c> is the one resolver both
/// heads share (D-81).
/// </summary>
internal static class ServerAddressResolution
{
    private const string _logTag = "MW3";
    private const string _intentExtraName = "server";
    private const string _localKeyword = "local";
    private const string _storedAddressFileName = "server_address.txt";

    // docs/game-server/REQUIREMENTS.md §4 "Tuning values": bounds D-82's blocking OnCreate probe far
    // below Android's ANR threshold, while surviving a server that is still cold-starting.
    private const int _probeTimeoutMilliseconds = 2000;

    /// <summary>
    /// Resolves the gateway factory for this launch. Blocking is intentional (D-82): the probe runs
    /// on a thread-pool thread, off the main looper, bounded by <see cref="_probeTimeoutMilliseconds"/>,
    /// and this call blocks on that thread-pool task - the naive
    /// <c>ConnectAsync().GetAwaiter().GetResult()</c> directly on the calling (main looper) thread
    /// would deadlock on its own continuation instead.
    /// </summary>
    public static IMatchGatewayFactory Resolve(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var extra = activity.Intent?.GetStringExtra(_intentExtraName);

        if (string.Equals(extra, _localKeyword, StringComparison.OrdinalIgnoreCase))
        {
            ClearStoredAddress(activity);
            Log.Info(_logTag, "MW3: mode=local (cleared by -e server local)");
            return new LoopbackMatchGatewayFactory();
        }

        var candidate = extra ?? ReadStoredAddress(activity);
        if (candidate is null)
        {
            Log.Info(_logTag, "MW3: mode=local (no address configured)");
            return new LoopbackMatchGatewayFactory();
        }

        var result = Task.Run(() => ServerPreflightResolver.Resolve(
                candidate, timeScale: 1, TimeSpan.FromMilliseconds(_probeTimeoutMilliseconds)))
            .GetAwaiter().GetResult();

        if (!result.Succeeded)
        {
            // A malformed extra never touches a stored address (D-83); an unreachable one leaves it
            // alone too - only a successful handshake ever writes or clears the file.
            Log.Info(_logTag, FormattableString.Invariant(
                $"MW3: mode=local (fallback) address={candidate} reason={result.FailureKind} detail={result.FailureDetail}"));
            return new LoopbackMatchGatewayFactory();
        }

        if (extra is not null)
        {
            WriteStoredAddress(activity, extra);
        }

        Log.Info(_logTag, FormattableString.Invariant($"MW3: mode=remote address={candidate}"));
        return result.Factory!;
    }

    private static string? ReadStoredAddress(Activity activity)
    {
        try
        {
            var path = StoredAddressPath(activity);
            if (!File.Exists(path))
            {
                return null;
            }

            var content = File.ReadAllText(path).Trim();
            return content.Length == 0 ? null : content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Boundary validation (docs/CONVENTIONS.md): anything read off disk that is unreadable or
            // malformed is treated as absent rather than thrown into OnCreate.
            Log.Info(_logTag, FormattableString.Invariant($"MW3: stored address unreadable, treating as absent: {ex.Message}"));
            return null;
        }
    }

    private static void WriteStoredAddress(Activity activity, string address)
    {
        try
        {
            File.WriteAllText(StoredAddressPath(activity), address);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info(_logTag, FormattableString.Invariant($"MW3: could not persist address: {ex.Message}"));
        }
    }

    private static void ClearStoredAddress(Activity activity)
    {
        try
        {
            var path = StoredAddressPath(activity);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info(_logTag, FormattableString.Invariant($"MW3: could not clear stored address: {ex.Message}"));
        }
    }

    private static string StoredAddressPath(Activity activity) => Path.Combine(activity.FilesDir!.AbsolutePath, _storedAddressFileName);
}
