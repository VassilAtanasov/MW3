namespace MW3.Core;

/// <summary>
/// The fraction of a base's garrison a send commits, as a whole percentage (FR-1, parity G-3). The
/// human path (FR-2) picks one of these explicitly; the AI (this phase) always chooses
/// <see cref="Half"/>, unchanged from before this feature.
/// </summary>
public enum SendStrength
{
    Quarter = 25,
    Half = 50,
    ThreeQuarters = 75,
    Full = 100,
}
