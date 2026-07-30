namespace MW3.Core;

/// <summary>
/// The send-size arithmetic shared by every send path (FR-1, parity G-3) - <see cref="AiBrain"/>
/// this phase, the human path from FR-2 on - so a <see cref="SendStrength"/> is computed once
/// rather than each caller keeping its own <c>garrison / 2</c> copy.
/// </summary>
public static class SendStrengthCalculator
{
    public static int Compute(int garrison, SendStrength strength) =>
        Math.Max(1, garrison * (int)strength / 100);
}
