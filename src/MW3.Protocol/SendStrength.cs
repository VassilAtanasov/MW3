namespace MW3.Protocol;

/// <summary>
/// The fraction of a base's garrison a send commits, as a whole percentage (phase 4 FR-1, parity
/// G-3). The human path picks one of these explicitly; the AI always chooses <see cref="Half"/>.
/// </summary>
public enum SendStrength
{
    Quarter = 25,
    Half = 50,
    ThreeQuarters = 75,
    Full = 100,
}
