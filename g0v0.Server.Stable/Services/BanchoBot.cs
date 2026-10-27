// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using osu.Game.Online.API;

namespace g0v0.Server.Stable.Services;

/// <summary>Handles Stable utility, score and tournament chat commands.</summary>
/// <param name="scopes">The factory for request-independent database scopes.</param>
/// <param name="rooms">The live multiplayer room manager.</param>
public sealed class BanchoBot(IServiceScopeFactory scopes, IRoomManager rooms)
{
    /// <summary>The shared BanchoBot account ID.</summary>
    public const int UserId = 3;

    private const string MultiplayerHelp = """
        !mp make <name> | close | settings | name <name> | password [password]
        !mp size <2-16> | set <0|2> | lock | unlock
        !mp host|invite|kick|ban|addref|removeref <username|#userid> | listrefs
        !mp move <username|#userid> <slot> | team <username|#userid> <red|blue>
        !mp map <beatmap_id> [ruleset_id] | mods [acronyms] [freemod]
        !mp playlist add <id> <beatmap_id> [ruleset_id] [mods]
        !mp playlist edit <id> beatmap=xxx ruleset_id=xxx mods=xxx
        !mp playlist remove <id> | list
        !mp start [seconds] | abort | timer [seconds] | aborttimer
        Room changes require the host or a referee. Only the host can change referees.
        """;

    /// <summary>Gets the configured name of the shared bot account.</summary>
    /// <returns>The account name, or BanchoBot when the account is unavailable.</returns>
    public async Task<string> GetNameAsync()
    {
        using IServiceScope scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetUsernameByIdAsync(UserId).ConfigureAwait(false) ?? "BanchoBot";
    }

    /// <summary>Runs a recognized command after the chat transport checks membership and write permissions.</summary>
    /// <param name="player">The exact sending session.</param>
    /// <param name="channel">The channel, or null for a private message to the bot.</param>
    /// <param name="message">The command text.</param>
    /// <returns>The response text, or null for an unrecognized command.</returns>
    public async Task<string?> ProcessAsync(IPlayer player, StableChannel? channel, string message)
    {
        if (!message.StartsWith('!') || !player.IsOnline || player.IsRestricted)
        {
            return null;
        }

        string[] tokens = message[1..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        string[] args = tokens[1..];
        return tokens[0].ToLowerInvariant() switch
        {
            "help" => Help(args),
            "roll" => Roll(player, args),
            "re" => await RecentAsync(player, includeFailed: true).ConfigureAwait(false),
            "pr" => await RecentAsync(player, includeFailed: false).ConfigureAwait(false),
            "mp" => await MultiplayerAsync(player, channel, args).ConfigureAwait(false),
            _ => null
        };
    }

    internal static bool IsMultiplayerCommand(string message)
        => message.StartsWith("!mp", StringComparison.OrdinalIgnoreCase) && (message.Length == 3 || char.IsWhiteSpace(message[3]));

    internal static IEnumerable<string> SplitReply(string text, int limit)
    {
        limit = limit > 0 ? limit : ChatConfig.DefaultMessageLengthLimit;
        foreach (string line in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Where(line => line.Length > 0))
        {
            string remainder = line;
            while (remainder.Length > limit)
            {
                yield return remainder[..limit];
                remainder = remainder[limit..];
            }

            if (remainder.Length > 0)
            {
                yield return remainder;
            }
        }
    }

    private static string Help(string[] args) => args.FirstOrDefault()?.TrimStart('!').ToLowerInvariant() switch
    {
        null => "Available commands:\n!help [command]\n!roll [positive maximum, default 100]\n!re\n!pr\n!mp",
        "help" => "Usage: !help [command]",
        "roll" => "Usage: !roll [positive maximum, default 100]",
        "re" => "!re shows your latest score in the current mode, including failed attempts.",
        "pr" => "!pr shows your latest passed score in the current mode.",
        "mp" => MultiplayerHelp,
        _ => $"No such command: {args[0]}"
    };

    private static string Roll(IPlayer player, string[] args)
    {
        int maximum = 100;
        return args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out maximum) || maximum <= 0))
            ? "Usage: !roll [positive maximum, default 100]"
            : FormattableString.Invariant($"{player.Name} rolls {Random.Shared.NextInt64(1, (long)maximum + 1)} point(s)");
    }

    private async Task<string> RecentAsync(IPlayer player, bool includeFailed)
    {
        using IServiceScope scope = scopes.CreateScope();
        IScoreRepository scores = scope.ServiceProvider.GetRequiredService<IScoreRepository>();
        GameConfig config = scope.ServiceProvider.GetService<ConfigManager>()?.TryGet<GameConfig>() ?? new GameConfig();
        int mode = ScoreModeUtils.GetScoreMode(player.Activity.Mode, player.Activity.Mods.Select(mod => new APIMod(mod)), config);
        int limit = 1;
        while (true)
        {
            IReadOnlyList<Score> recent = await scores.GetRecentByUserIdAndModeAsync(player.Id, mode, limit).ConfigureAwait(false);
            Score? score = recent.FirstOrDefault(candidate => includeFailed || candidate.Passed);
            if (score is not null)
            {
                return FormattableString.Invariant($"Played at {score.EndedAt:yyyy-MM-dd HH:mm}\n{score.Pp:N2}pp {score.Accuracy:P2} {score.TotalScore:N0}");
            }

            if (recent.Count < limit || includeFailed || limit == int.MaxValue)
            {
                return "You have no scores.";
            }

            limit = limit <= int.MaxValue / 4 ? limit * 4 : int.MaxValue;
        }
    }

    private async Task<string> MultiplayerAsync(IPlayer player, StableChannel? channel, string[] args)
    {
        string command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
        return command switch
        {
            "help" => MultiplayerHelp,
            "makeprivate" or "clearhost" => $"!mp {command} is not supported.",
            "make" => await MakeRoomAsync(player, args[1..]).ConfigureAwait(false),
            _ => channel is null || !string.Equals(channel.Name, "#multiplayer", StringComparison.Ordinal)
                ? "The !mp command can only be used in your multiplayer channel."
                : await rooms.ExecuteBotCommandAsync(player, channel, command, args[1..]).ConfigureAwait(false)
        };
    }

    private async Task<string> MakeRoomAsync(IPlayer player, string[] args)
    {
        string name = string.Join(' ', args).Trim();
        if (name.Length is < 1 or > 100)
        {
            return "Usage: !mp make <name> (1-100 characters).";
        }

        Objects.Rooms.IRoom? room = await rooms.CreateTournamentRoomAsync(player, name).ConfigureAwait(false);
        return room is null ? "Could not create a room. Leave your current room and check the active tournament room limit."
            : FormattableString.Invariant($"Created tournament room {room.RoomId}: {name} (match {room.MatchId}).");
    }
}