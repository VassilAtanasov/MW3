using MW3.Core;

// Prints the golden-hash test's fixed scripted match's snapshot hash to stdout, and nothing else.
// SnapshotHashTests launches this as a separate OS process and compares its output against the
// same hash computed in-process, which is the only way to actually prove the hash agrees across
// processes rather than assume it (D-71) - the whole reason this hash avoids
// object.GetHashCode/string.GetHashCode is that .NET randomizes string hashing per process, so an
// in-process-only assertion could not have told the difference.
var match = new Match(MapCatalog.Small);
var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
var neutral = match.Bases.First(b => b.Owner is null);

match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 5));
match.Advance(1000);

var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
Console.WriteLine(SnapshotHash.Compute(snapshot).ToString("x16", System.Globalization.CultureInfo.InvariantCulture));
