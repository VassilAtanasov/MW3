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
    /// A level-4 village (130% defence, the highest an <see cref="UpgradeCommand"/> can reach)
    /// holding 7 survives an 8-unit wave - the largest that stays a single wave (FR-3): under phase
    /// 2's plain 1:1 arithmetic 8 &gt; 7 would have captured it and left the attacker holding 1, but
    /// under <c>Bu = (a/d) × Wu</c> (Wu*a = 800 &lt; Du*d = 910) the defender holds instead.
    /// </summary>
    [Fact]
    public void DefendedBase_SurvivesAnAttackThatWouldHaveCapturedItUnder1To1()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);

        for (var l = LevelTable.MinLevel; l < 4; l++)
        {
            SetGarrison(ai, LevelTable.UpgradeCost(BaseType.Producer, l));
            Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, ai.Id)));
            match.Advance(LevelTable.UpgradeBuildDurationTicks(l));
        }

        Assert.Equal(4, ai.Level); // completes: level 4, defence 130%
        SetGarrison(ai, 7);
        SetGarrison(human, 30);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));

        // The defender keeps producing throughout the flight, so what it holds on arrival - not
        // what it held at launch - is what CombatResolver's ratio formula subtracts from (the same
        // style CaptureDemotionTests uses). Growth only strengthens the hold here: at Wu=8, a=100,
        // d=130, the defender needs only Du >= 7 to survive (8*100 <= Du*130), and it started
        // there, so production during the flight cannot flip the outcome to a capture.
        var army = match.ArmiesInFlight.Single(); // 8 units or fewer never splits into waves (FR-3)
        match.Advance(army.ArrivalTick - match.ElapsedTicks - 1);
        var defendersOnArrival = ai.GarrisonCount;
        match.Advance(1);

        Assert.Equal(match.AiPlayer, ai.Owner); // held - would have been captured under 1:1
        Assert.Equal(4, ai.Level); // no demotion: it was never captured
        Assert.Equal(defendersOnArrival - (8 * 100 / 130), ai.GarrisonCount);
    }
}
