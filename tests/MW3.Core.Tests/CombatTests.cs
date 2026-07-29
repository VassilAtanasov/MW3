namespace MW3.Core.Tests;

/// <summary>
/// Full-match coverage of <see cref="CombatResolver"/>'s ratio formula (D-29) through
/// <see cref="Match"/> itself, not just the resolver in isolation (see
/// <see cref="CombatResolverTests"/> for that) - proving the wiring in <c>ResolveArrival</c> reads
/// the right defence percentage and applies it correctly.
/// </summary>
public class CombatTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    /// <summary>
    /// A level-2 village (110% defence) holding 20 survives a 21-unit wave: under phase 2's plain
    /// 1:1 arithmetic 21 &gt; 20 would have captured it and left the attacker holding 1, but under
    /// <c>Bu = (a/d) × Wu</c> (Wu*a = 2100 &lt; Du*d = 2200) the defender holds instead.
    /// </summary>
    [Fact]
    public void DefendedBase_SurvivesAnAttackThatWouldHaveCapturedItUnder1To1()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, ai.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel)); // completes: level 2, defence 110%
        Assert.Equal(2, ai.Level);
        SetGarrison(ai, 20);
        SetGarrison(human, 30);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 21)));

        // The defender keeps producing throughout the flight, so what it holds on arrival - not
        // what it held at launch - is what CombatResolver's ratio formula subtracts from (the same
        // style CaptureDemotionTests uses). Growth only strengthens the hold here: at Wu=21, a=100,
        // d=110, the defender needs only Du >= 20 to survive (21*100 <= Du*110), and it started
        // there, so production during the flight cannot flip the outcome to a capture.
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks - 1);
        var defendersOnArrival = ai.GarrisonCount;
        match.Advance(1);

        Assert.Equal(match.AiPlayer, ai.Owner); // held - would have been captured under 1:1
        Assert.Equal(2, ai.Level); // no demotion: it was never captured
        Assert.Equal(defendersOnArrival - (21 * 100 / 110), ai.GarrisonCount);
    }
}
