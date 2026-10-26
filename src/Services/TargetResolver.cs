using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.SteamAPI;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Resolves SourceMod-style target selectors against online players.
/// </summary>
internal static class TargetResolver
{
    public static IReadOnlyList<IPlayer> ResolveOnline(ISwiftlyCore core, IPlayer? actor, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        query = query.Trim();
        var players = core.PlayerManager.GetAllValidPlayers().ToList();

        if (query.StartsWith('@'))
        {
            return query.ToLowerInvariant() switch
            {
                "@me" => actor == null ? [] : [actor],
                "@all" => players,
                "@t" => players.Where(player => TeamOf(player) == Team.T).ToList(),
                "@ct" => players.Where(player => TeamOf(player) == Team.CT).ToList(),
                "@spec" => players.Where(player => TeamOf(player) == Team.Spectator).ToList(),
                "@alive" => players.Where(player => player.IsAlive).ToList(),
                "@dead" => players.Where(player => !player.IsAlive).ToList(),
                "@bots" => players.Where(player => player.IsFakeClient).ToList(),
                "@human" => players.Where(player => !player.IsFakeClient).ToList(),
                "@random" => PickRandom(players),
                _ => []
            };
        }

        if (query.StartsWith('#'))
        {
            return int.TryParse(query[1..], out var slot)
                ? players.Where(player => player.PlayerID == slot || player.UserID == slot).ToList()
                : [];
        }

        var steamId = SteamIdParser.ParseToSteamId64(query);
        if (steamId.HasValue)
        {
            return players.Where(player => player.SteamID == steamId.Value).ToList();
        }

        var exact = players
            .Where(player => player.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return exact.Count > 0
            ? exact
            : players.Where(player => player.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static IPlayer? ResolveSingle(ISwiftlyCore core, IPlayer? actor, string query)
    {
        return ResolveOnline(core, actor, query).FirstOrDefault();
    }

    public static bool TryParseSteamId(string? input, out ulong steamId)
    {
        steamId = 0;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var parsed = SteamIdParser.ParseToSteamId64(input.Trim().TrimStart('$'));
        if (!parsed.HasValue)
        {
            return false;
        }

        steamId = parsed.Value;
        return true;
    }

    public static Team TeamOf(IPlayer player)
    {
        return (Team)(player.Pawn?.TeamNum ?? 0);
    }

    public static string TeamName(IPlayer player)
    {
        return TeamOf(player) switch
        {
            Team.T => "T",
            Team.CT => "CT",
            Team.Spectator => "SPEC",
            _ => "-"
        };
    }

    public static string LifeStateName(IPlayer player)
    {
        return player.Pawn?.LifeState == (byte)LifeState_t.LIFE_ALIVE ? "alive" : "dead";
    }

    private static IReadOnlyList<IPlayer> PickRandom(List<IPlayer> players)
    {
        return players.Count == 0 ? [] : [players[Random.Shared.Next(players.Count)]];
    }
}
