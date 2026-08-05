namespace MW3.Core;

/// <summary>
/// A base's type: <see cref="Producer"/>, which grows its own garrison, <see cref="Tower"/>, which
/// never produces and instead shoots enemy armies passing within range (FR-4), or <see cref="Forge"/>,
/// which neither produces nor fires and has exactly one tier - its effect is a global multiplier read
/// by count, not anything local (phase 6 FR-1, D-42). Every base starts a <see cref="Producer"/>,
/// including neutral ones, and changes type only through <see cref="Match.Execute(ConvertCommand)"/>
/// (D-13). Declaration order matters: <see cref="Match.AvailableActions"/> offers one convert action
/// per type other than the base's own, in this order (D-48).
/// </summary>
public enum BaseType
{
    Producer,
    Tower,
    Forge,
}
