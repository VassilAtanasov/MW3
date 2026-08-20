namespace MW3.Protocol;

/// <summary>
/// What a gateway made of a submitted <see cref="GatewayCommand"/>: accepted, or rejected with a
/// reason. A result rather than a bare bool or an exception, mirroring how the rules' own outcome
/// enums distinguish acceptance from each rejection - an ordinary rejection is not exceptional.
///
/// The reason is a string rather than an enum on purpose: the vocabulary of rejections belongs to
/// whatever is on the far side of the seam (the rules today, a server from FR-4, which can also
/// reject for reasons the rules have no member for - an unknown match, a closed session), and a
/// protocol enum would have to grow every time that side learned a new one.
/// </summary>
/// <param name="Accepted">Whether the command was applied.</param>
/// <param name="RejectionReason">Why it was not, or null when it was.</param>
public sealed record GatewayCommandResult(bool Accepted, string? RejectionReason)
{
    /// <summary>The command was applied.</summary>
    public static GatewayCommandResult Ok() => new(Accepted: true, RejectionReason: null);

    /// <summary>The command was not applied, for <paramref name="reason"/>.</summary>
    public static GatewayCommandResult Rejected(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejection must carry a reason.", nameof(reason));
        }

        return new GatewayCommandResult(Accepted: false, reason);
    }
}
