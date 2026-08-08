using System.Reflection;

namespace MW3.Core.Tests;

/// <summary>
/// FR-3: an army carries the path <see cref="PathCalculator"/> computed once at submission
/// (<c>docs/maps/REQUIREMENTS.md</c> FR-3, <c>docs/maps/ARCHITECTURE.md</c> D-51, D-53). Covers the
/// path being shared across a send's waves, surviving a mid-flight capture unchanged, Medium's
/// off-the-straight-line position at mid-flight, and the AI's arrival predictions agreeing with what
/// <see cref="Match.Execute(SendArmyCommand)"/> actually produces.
/// </summary>
public class ObstacleDetourTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static MapPoint PositionAtTick(Match match, Army army, long tick)
    {
        var method = typeof(Match).GetMethod("PositionAtTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (MapPoint)method.Invoke(match, new object[] { army, tick })!;
    }

    [Fact]
    public void SmallMap_ArmyPath_IsTheStraightTwoPointPath_BitIdenticalToPreFR3Behavior()
    {
        var match = new Match(MapCatalog.Small);
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 8)));

        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(new[] { human.Position, neutral.Position }, army.Path.Waypoints);
        Assert.Equal(34, army.ArrivalTick - army.LaunchTick); // the fixed pre-FR-3 travel time, unchanged
    }

    [Fact]
    public void MediumMap_SendSplitIntoWaves_EveryWaveSharesTheIdenticalPathInstance()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(human, 20); // splits into three waves (8, 8, 4)

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 20)));
        match.Advance(SendWaveCalculator.LaunchTickOffset(SendWaveCalculator.WaveCount(20)));

        var waves = match.ArmiesInFlight.OrderBy(a => a.WaveIndex).ToList();
        Assert.Equal(3, waves.Count);
        Assert.All(waves, w => Assert.Same(waves[0].Path, w.Path));
        Assert.All(waves, w => Assert.Equal(waves[0].ArrivalTick - waves[0].LaunchTick, w.ArrivalTick - w.LaunchTick));
    }

    [Fact]
    public void CapturingTheTargetMidFlight_DoesNotReRouteRelengthOrChangeArrivalTick()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 8);

        // A send toward the neutral, whose ownership then flips mid-flight (D-15: a launched army is
        // untouchable by capture) - rigged directly by reflection, the same style other tests in this
        // suite use, so the flip happens deterministically between submission and arrival.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 8)));
        var army = match.ArmiesInFlight.Single();
        var pathBefore = army.Path;
        var waypointsBefore = pathBefore.Waypoints.ToList();
        var lengthBefore = pathBefore.Length;
        var arrivalBefore = army.ArrivalTick;

        SetOwner(neutral, match.AiPlayer);
        match.Advance((army.ArrivalTick - match.ElapsedTicks) / 2);

        Assert.Same(pathBefore, army.Path);
        Assert.Equal(waypointsBefore, army.Path.Waypoints);
        Assert.Equal(lengthBefore, army.Path.Length);
        Assert.Equal(arrivalBefore, army.ArrivalTick);
    }

    [Fact]
    public void MediumMap_PositionAtMidFlight_IsMeasurablyOffTheStraightLine()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));
        var army = match.ArmiesInFlight.Single();

        var midTick = (army.LaunchTick + army.ArrivalTick) / 2;
        var position = PositionAtTick(match, army, midTick);

        var straightLineMidpoint = new MapPoint(0.50, 0.50); // inside Medium's obstacle
        var dx = position.X - straightLineMidpoint.X;
        var dy = position.Y - straightLineMidpoint.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        Assert.True(distance >= 0.15, $"expected the mid-flight position to sit at least 0.15 from (0.50, 0.50), was {distance}");
    }

    [Fact]
    public void MediumMap_PositionAtFractionZeroAndOne_IsExactlyTheFirstAndLastWaypoint()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));
        var army = match.ArmiesInFlight.Single();

        Assert.Equal(army.Path.Waypoints[0], PositionAtTick(match, army, army.LaunchTick));
        Assert.Equal(army.Path.Waypoints[^1], PositionAtTick(match, army, army.ArrivalTick));
    }

    [Fact]
    public void MediumMap_SendCosts92Ticks_WhereSmallsIdenticalSendCosts76()
    {
        var small = new Match(MapCatalog.Small);
        var smallHuman = HumanBase(small);
        var smallAi = AiBase(small);
        SetGarrison(smallHuman, 8);
        Assert.Equal(SendArmyOutcome.Accepted, small.Execute(new SendArmyCommand(small.HumanPlayer, smallHuman.Id, smallAi.Id, 8)));
        var smallArmy = small.ArmiesInFlight.Single();
        Assert.Equal(76, smallArmy.ArrivalTick - smallArmy.LaunchTick);

        var medium = new Match(MapCatalog.Medium);
        var mediumHuman = HumanBase(medium);
        var mediumAi = AiBase(medium);
        SetGarrison(mediumHuman, 8);
        Assert.Equal(SendArmyOutcome.Accepted, medium.Execute(new SendArmyCommand(medium.HumanPlayer, mediumHuman.Id, mediumAi.Id, 8)));
        var mediumArmy = medium.ArmiesInFlight.Single();
        Assert.Equal(92, mediumArmy.ArrivalTick - mediumArmy.LaunchTick);
    }

    // --- The AI's arrival predictions agree with what Match.Execute would actually produce (D-53) ---

    [Fact]
    public void AiBrainAttackPrediction_AgreesWithTheArrivalTickMatchActuallyAssigns_OnMedium()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(ai, 30); // enough to clear SendStrength.Half over the human's defence
        SetGarrison(human, 1);

        var brain = new AiBrain(match.AiPlayer);
        var ownBases = match.Bases.Where(b => b.Owner == match.AiPlayer).OrderBy(b => b.Id).ToList();
        var tryAttack = typeof(AiBrain).GetMethod("TryAttack", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var decision = (BrainDecision)tryAttack.Invoke(brain, new object[] { match, ownBases })!;

        Assert.True(decision.HasCommand);
        var command = decision.Command;
        Assert.Equal(ai.Id, command.SourceBaseId);
        Assert.Equal(human.Id, command.TargetBaseId);

        // Independently reproduce the arrival tick the AI's own prediction site computed - through
        // the same PathCalculator/TravelTimeCalculator pair, not the AI's internal state - then
        // confirm Match.Execute assigns that same span to the send it actually decided on.
        var speed = Match.EffectiveArmySpeedUnitsPerTick(match.MoraleFor(match.AiPlayer).Level);
        var path = PathCalculator.ComputePath(ai.Position, human.Position, match.Obstacles);
        var travelTimeCalculatorType = typeof(Match).Assembly.GetType("MW3.Core.TravelTimeCalculator")!;
        var computeTicksMethod = travelTimeCalculatorType.GetMethod("ComputeTicks", BindingFlags.NonPublic | BindingFlags.Static)!;
        var predictedTravelTicks = (long)computeTicksMethod.Invoke(null, new object[] { path.Length, speed })!;

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(command));
        var army = match.ArmiesInFlight.Single();
        Assert.Equal(predictedTravelTicks, army.ArrivalTick - army.LaunchTick);
        Assert.Equal(92, predictedTravelTicks); // sanity: matches PathCalculatorTests' pinned figure for this pair
    }
}
