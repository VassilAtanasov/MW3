namespace MW3.Core;

/// <summary>
/// Whether a <see cref="BaseAction"/> can be taken right now - four distinct states, never a bool
/// and never an exception (D-25), so the menu can grey a button for the right reason instead of
/// inferring one from a cost comparison it should not be making itself. <see cref="UnderConstruction"/>
/// added by FR-3c: a base already building rejects a second command (D-30).
/// </summary>
public enum BaseActionAvailability
{
    Affordable,
    GarrisonBelowCost,
    AlreadyAtMaxLevel,
    UnderConstruction,
}
