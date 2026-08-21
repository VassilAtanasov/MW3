namespace MW3.Server;

/// <summary>
/// The resolved, absolute directory every new <see cref="MatchSession"/> logs to (FR-6, D-86) -
/// registered once in DI from <c>Program.cs</c>'s <c>--log-dir</c> flag (default <c>logs/</c> under
/// the content root) so <see cref="ConnectionHandler"/> does not have to re-resolve it per connection.
/// </summary>
internal sealed record MatchLogDirectory(string Path);
