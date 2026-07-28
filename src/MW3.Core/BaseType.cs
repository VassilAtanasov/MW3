namespace MW3.Core;

/// <summary>
/// A base's type: <see cref="Producer"/>, which grows its own garrison, or <see cref="Tower"/>,
/// which never produces and instead shoots enemy armies passing within range (FR-4). Every base
/// starts a <see cref="Producer"/>, including neutral ones, and changes type only through
/// <see cref="Match.Execute(ConvertCommand)"/> (D-13).
/// </summary>
public enum BaseType
{
    Producer,
    Tower,
}
