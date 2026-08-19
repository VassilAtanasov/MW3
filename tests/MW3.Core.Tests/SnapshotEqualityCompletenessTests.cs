using System.Reflection;

namespace MW3.Core.Tests;

/// <summary>
/// The snapshot records that hold lists write their own <c>Equals</c>, because a record's generated
/// one compares a list by reference and a round-tripped snapshot would never equal the one it was
/// built from. The cost of writing it by hand is that the compiler stops helping: a user-defined
/// <c>Equals(T)</c> suppresses the generated one entirely, so a field added to the positional list
/// later is silently left out of equality - and equality is what FR-2's diff is built on, so the
/// symptom would be an event that never fires for a field that really changed.
///
/// This walks each record's primary constructor, rebuilds it with exactly one parameter perturbed,
/// and asserts the result compares unequal. It fails on the day someone adds a field and forgets,
/// naming the field.
/// </summary>
public class SnapshotEqualityCompletenessTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static MatchSnapshot BuildSnapshot()
    {
        var match = new Match(MapCatalog.Medium);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetGarrison(human, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 30)));
        match.Advance(20);
        SetGarrison(human, 60);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));

        return MatchSnapshotBuilder.Build(match, match.HumanPlayer);
    }

    /// <summary>
    /// A value of <paramref name="type"/> that differs from <paramref name="current"/>. Only the
    /// shapes the snapshot records actually use are handled - anything else fails loudly, so a new
    /// kind of field cannot slip past this test by being quietly unperturbable.
    /// </summary>
    private static object? Perturb(Type type, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsEnum)
        {
            foreach (var candidate in Enum.GetValues(underlying))
            {
                if (!candidate.Equals(current))
                {
                    return candidate;
                }
            }
        }

        if (underlying == typeof(int))
        {
            return (current as int?).GetValueOrDefault() + 7;
        }

        if (underlying == typeof(long))
        {
            return (current as long?).GetValueOrDefault() + 7L;
        }

        if (underlying == typeof(double))
        {
            return (current as double?).GetValueOrDefault() + 7.0;
        }

        if (underlying == typeof(string))
        {
            return current as string == "perturbed" ? "perturbed-again" : "perturbed";
        }

        if (underlying == typeof(MapPoint))
        {
            var point = current is MapPoint p ? p : default;
            return new MapPoint(point.X + 0.1, point.Y + 0.1);
        }

        if (underlying == typeof(PendingConstructionSnapshot))
        {
            return current is null
                ? new PendingConstructionSnapshot(BaseActionKind.Upgrade, 42, 3, null)
                : null;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying) && underlying != typeof(string))
        {
            // A list member is perturbed by dropping its last element, which the element-wise
            // comparison must notice. Handled by the caller, which knows the element type.
            return null;
        }

        throw new InvalidOperationException(
            FormattableString.Invariant($"This test has no way to perturb a {type}. Add one rather than skipping the field."));
    }

    private static void AssertEveryConstructorParameterAffectsEquality<T>(T original)
        where T : class
    {
        var constructor = typeof(T).GetConstructors().Single();
        var parameters = constructor.GetParameters();
        var values = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            values[i] = typeof(T).GetProperty(parameters[i].Name!)!.GetValue(original);
        }

        Assert.Equal(original, constructor.Invoke((object?[])values.Clone()));

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var mutated = (object?[])values.Clone();

            if (values[i] is System.Collections.IList list && list.Count > 0)
            {
                // Drop the last element: the element-wise comparison has to notice a shorter list.
                var shortened = (System.Collections.IList)Activator.CreateInstance(values[i]!.GetType())!;
                for (var j = 0; j < list.Count - 1; j++)
                {
                    shortened.Add(list[j]);
                }

                mutated[i] = shortened;
            }
            else if (values[i] is System.Collections.IList)
            {
                // An empty list cannot be shortened, so nothing about it is provable here. Every
                // list field this test exercises today is non-empty in BuildSnapshot()'s fixture;
                // a future field that is legitimately empty by construction would silently reach
                // this branch unexercised, which is a gap worth a fixture change, not a bigger test.
                continue;
            }
            else
            {
                mutated[i] = Perturb(parameter.ParameterType, values[i]);
            }

            var perturbed = constructor.Invoke(mutated);
            Assert.False(
                original.Equals(perturbed),
                $"{typeof(T).Name}.{parameter.Name} is not compared by Equals - a change to it would be invisible to a diff.");
        }
    }

    [Fact]
    public void EveryMatchSnapshotField_TakesPartInEquality() =>
        AssertEveryConstructorParameterAffectsEquality(BuildSnapshot());

    [Fact]
    public void EveryBaseSnapshotField_TakesPartInEquality()
    {
        var snapshot = BuildSnapshot();

        // One base that is building something and owns a populated action list, and one plain
        // neutral - between them every nullable field is exercised in both states.
        AssertEveryConstructorParameterAffectsEquality(snapshot.Bases.First(b => b.Construction is not null));
        AssertEveryConstructorParameterAffectsEquality(snapshot.Bases.First(b => b.OwnerPlayerId is null));
    }

    [Fact]
    public void EveryArmySnapshotField_TakesPartInEquality() =>
        AssertEveryConstructorParameterAffectsEquality(BuildSnapshot().Armies[0]);

    [Fact]
    public void EveryPlayerAndActionField_TakesPartInEquality()
    {
        // These two have no list members and so keep the record-generated Equals. They are covered
        // anyway: if a later feature gives either one a list, this test starts asserting against a
        // hand-written Equals the moment one appears, rather than after someone notices.
        var snapshot = BuildSnapshot();

        AssertEveryConstructorParameterAffectsEquality(snapshot.Players[0]);
        AssertEveryConstructorParameterAffectsEquality(snapshot.Bases.First(b => b.AvailableActions.Count > 0).AvailableActions[0]);
    }

    [Fact]
    public void EveryEventBatchField_TakesPartInEquality()
    {
        // EventBatch holds a list (Events) and so hand-writes Equals exactly as MatchSnapshot does -
        // the same trap applies.
        var match = new Match(MapCatalog.Medium);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        SetGarrison(human, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 30)));
        match.Advance(20);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        AssertEveryConstructorParameterAffectsEquality(SnapshotDiffer.Diff(a, b));
    }
}
