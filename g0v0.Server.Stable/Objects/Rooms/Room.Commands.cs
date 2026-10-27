// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using g0v0.Server.Stable.Services;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;

namespace g0v0.Server.Stable.Objects.Rooms;

public sealed partial class Room
{
    private const string PermissionDenied = "You don't have the required privilege to perform this action.";
    private readonly HashSet<int> _referees = [];
    private readonly HashSet<int> _bannedUsers = [];
    private int? _hostUserId;
    private int _size;
    private bool _joinsLocked;
    private bool _closing;

    internal bool IsTournament { get; }

    internal int CreatorId { get; }

    // RoomManager holds its membership gate and this room's lock for the entire command.
    internal async Task<string> ExecuteBotCommandAsync(IPlayer actor, string command, string[] args, RoomCountdownService countdowns)
    {
        bool host = actor.Id == _hostUserId;
        bool playlistList = string.Equals(command, "playlist", StringComparison.Ordinal) && string.Equals(args.FirstOrDefault(), "list", StringComparison.OrdinalIgnoreCase);
        if (command is not ("settings" or "listrefs") && !playlistList && !host && !_referees.Contains(actor.Id))
        {
            return PermissionDenied;
        }

        if (command is "addref" or "removeref" && !host)
        {
            return PermissionDenied;
        }

        if (InProgress && command is "name" or "size" or "set" or "lock" or "unlock" or "map" or "mods" or "move" or "team" or "start")
        {
            return "This command cannot be used during gameplay.";
        }

        switch (command)
        {
            case "settings":
                return await DescribeAsync().ConfigureAwait(false);
            case "playlist":
                return await PlaylistAsync(actor, args).ConfigureAwait(false);
            case "listrefs":
                return await ListRefereesAsync().ConfigureAwait(false);
            case "name":
                return await RenameAsync(args).ConfigureAwait(false);
            case "password":
                return await PasswordAsync(args).ConfigureAwait(false);
            case "size":
                return await ResizeAsync(args).ConfigureAwait(false);
            case "set":
                return await SetTeamModeAsync(args).ConfigureAwait(false);
            case "lock":
            case "unlock":
                _joinsLocked = string.Equals(command, "lock", StringComparison.Ordinal);
                return _joinsLocked ? "Locked the match." : "Unlocked the match.";
            case "host":
            case "invite":
            case "kick":
            case "ban":
            case "addref":
            case "removeref":
                return await UserCommandAsync(actor, command, args).ConfigureAwait(false);
            case "map":
                return await MapAsync(args).ConfigureAwait(false);
            case "mods":
                return await ModsAsync(args).ConfigureAwait(false);
            case "move":
            case "team":
                return await SlotCommandAsync(command, args).ConfigureAwait(false);
            case "start":
                return await StartCommandAsync(args, countdowns).ConfigureAwait(false);
            case "abort":
                countdowns.Cancel(this);
                if (Host is null || !InProgress)
                {
                    return "No match is in progress.";
                }

                await AbortAsync(Host).ConfigureAwait(false);
                return "Aborted the match.";
            case "timer":
                if (!TryDelay(args, 30, out int seconds) || seconds == 0)
                {
                    return "Usage: !mp timer [seconds] (1-3600).";
                }

                countdowns.Schedule(this, seconds, startMatch: false);
                return FormattableString.Invariant($"Countdown ends in {seconds} second(s).");
            case "aborttimer":
                countdowns.Cancel(this);
                return "All timers aborted.";
            case "close":
                countdowns.Cancel(this);
                await CloseAsync(actor).ConfigureAwait(false);
                return "Closed the room.";
            default:
                return "Unknown mp subcommand. Send !mp help for usage.";
        }
    }

    private static bool TryDelay(string[] args, int defaultSeconds, out int seconds)
    {
        seconds = defaultSeconds;
        return args.Length == 0 || (args.Length == 1 && int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds is >= 0 and <= 3600);
    }

    private static List<APIMod>? ParsePlaylistMods(Ruleset ruleset, string text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "nm", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        string normalized = text.Replace("+", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal);
        if (normalized.Length % 2 != 0)
        {
            error = "Mods must be supplied as acronyms, for example HDHR or HD,HR.";
            return null;
        }

        List<APIMod> mods = [];
        for (int index = 0; index < normalized.Length; index += 2)
        {
            string acronym = normalized.Substring(index, 2).ToUpperInvariant();
            Mod? mod = ruleset.CreateModFromAcronym(acronym);
            if (mod is null || ruleset.ConvertToLegacyMods([mod]) == LegacyMods.None)
            {
                error = $"Unsupported Stable mod: {acronym}.";
                return null;
            }

            if (!mods.Exists(existing => string.Equals(existing.Acronym, acronym, StringComparison.Ordinal)))
            {
                mods.Add(new APIMod(mod));
            }
        }

        bool incompatible = mods.Select(mod => ruleset.CreateModFromAcronym(mod.Acronym)).Where(mod => mod is not null).Cast<Mod>()
            .Any(mod => mods.Select(other => ruleset.CreateModFromAcronym(other.Acronym)).Where(other => other is not null).Cast<Mod>()
                .Any(other => !ReferenceEquals(mod, other) && mod.IncompatibleMods.Any(type => type.IsInstanceOfType(other))));
        if (!incompatible)
        {
            return mods;
        }

        error = "The requested mods are incompatible.";
        return null;
    }

    private async Task<string> DescribeAsync()
    {
        string players = string.Join('\n', _slots.Select((slot, index) => (Slot: slot, Index: index))
            .Where(item => item.Slot.Player is not null)
            .Select(item => item.Slot.Team == MatchTeam.Neutral
                ? FormattableString.Invariant($"Slot {item.Index + 1}: {item.Slot.Player!.Name} ({item.Slot.Status})")
                : FormattableString.Invariant($"Slot {item.Index + 1}: {item.Slot.Player!.Name} ({item.Slot.Status}, {item.Slot.Team})")));
        string ruleset = _rulesets.GetRuleset(_match.Mode).RulesetInfo.Name;
        string beatmap = await FormatBeatmapAsync(_match.MapId, _match.MapName).ConfigureAwait(false);
        return FormattableString.Invariant($"Room name: {_match.Name} (ID {RoomId}, match {MatchId})\n")
            + $"Host: {Host?.Name ?? _hostUserId?.ToString(CultureInfo.InvariantCulture) ?? "None"} | Team mode: {_match.TeamType}\n"
            + FormattableString.Invariant($"Size: {_size} | Locked: {_joinsLocked} | Password: {(string.IsNullOrEmpty(_match.Password) ? "none" : "set")}\n")
            + FormattableString.Invariant($"Beatmap: {beatmap} | Ruleset: {ruleset} | Mods: {_match.Mods} | Free mods: {_match.FreeMods}\n")
            + $"Win condition: {_match.WinCondition}\n"
            + (players.Length == 0 ? "Players: none" : players);
    }

    private async Task<string> FormatBeatmapAsync(int beatmapId, string fallbackName)
    {
        if (beatmapId <= 0)
        {
            return "none";
        }

        string artist = string.Empty;
        string title = string.Empty;
        using IServiceScope scope = _scopes.CreateScope();
        IBeatmapRepository maps = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        Beatmap? map = await maps.GetByIdAsync(beatmapId).ConfigureAwait(false);
        if (map is not null)
        {
            fallbackName = map.Version;
            if (scope.ServiceProvider.GetService<IBeatmapSetRepository>() is { } sets)
            {
                BeatmapSet? set = await sets.GetByIdAsync(map.BeatmapSetId).ConfigureAwait(false);
                artist = set?.Artist ?? string.Empty;
                title = set?.Title ?? string.Empty;
            }
        }

        string label = string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title)
            ? fallbackName
            : $"{artist} - {title} [{fallbackName}]";
        GeneralConfig config = scope.ServiceProvider.GetService<ConfigManager>()?.TryGet<GeneralConfig>() ?? new GeneralConfig();
        return FormattableString.Invariant($"[{config.WebUrl}b/{beatmapId} {label}]");
    }

    private async Task<string> PlaylistAsync(IPlayer actor, string[] args)
    {
        string action = args.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        return action switch
        {
            "list" when args.Length == 1 => await ListPlaylistAsync().ConfigureAwait(false),
            "add" => await AddPlaylistAsync(actor, args[1..]).ConfigureAwait(false),
            "edit" => await EditPlaylistAsync(actor, args[1..]).ConfigureAwait(false),
            "remove" => await RemovePlaylistAsync(actor, args[1..]).ConfigureAwait(false),
            _ => "Usage: !mp playlist add <id> <beatmap_id> [ruleset_id] [mods] | edit <id> beatmap=xxx ruleset_id=xxx mods=xxx | remove <id> | list."
        };
    }

    private async Task<string> ListPlaylistAsync()
    {
        MultiplayerPlaylistItem[] items = _playlist.Where(item => !item.Expired).OrderBy(item => item.PlaylistOrder).ToArray();
        if (items.Length == 0)
        {
            return "Playlist is empty.";
        }

        List<string> lines = [];
        foreach (MultiplayerPlaylistItem item in items)
        {
            string beatmap = await FormatBeatmapAsync(item.BeatmapID, $"#{item.BeatmapID}").ConfigureAwait(false);
            string mods = !item.RequiredMods.Any() ? "NM" : string.Join(string.Empty, item.RequiredMods.Select(mod => mod.Acronym));
            string current = item.ID == CurrentItem?.ID ? "* " : string.Empty;
            lines.Add(FormattableString.Invariant($"{current}{item.ID}: {beatmap} ({_rulesets.GetRuleset(item.RulesetID).RulesetInfo.Name}, {mods})"));
        }

        return string.Join('\n', lines);
    }

    private async Task<string> AddPlaylistAsync(IPlayer actor, string[] args)
    {
        if (args.Length is < 2 or > 4 || !long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out long anchorId) || anchorId < 0
            || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int beatmapId) || beatmapId <= 0)
        {
            return "Usage: !mp playlist add <id> <beatmap_id> [ruleset_id] [mods]. The first id is an existing item to insert after, or 0 to append.";
        }

        int? rulesetId = null;
        if (args.Length >= 3 && (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRuleset) || parsedRuleset is < 0 or > 3))
        {
            return "Unknown Stable ruleset ID; use 0-3.";
        }
        else if (args.Length >= 3)
        {
            rulesetId = int.Parse(args[2], CultureInfo.InvariantCulture);
        }

        using IServiceScope scope = _scopes.CreateScope();
        IBeatmapRepository maps = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        Beatmap? map = await maps.GetByIdAsync(beatmapId).ConfigureAwait(false);
        if (map is null && scope.ServiceProvider.GetService<IFetcher>() is { } fetcher)
        {
            map = await maps.GetOrFetchByIdAsync(beatmapId, fetcher).ConfigureAwait(false);
        }

        if (map?.Checksum is not { Length: 32 })
        {
            return "Beatmap not found.";
        }

        int mode = rulesetId ?? map.Mode;
        if (mode is < 0 or > 3 || (map.Mode != 0 && mode != map.Mode))
        {
            return "That beatmap cannot be played in the requested ruleset.";
        }

        string? error = null;
        List<APIMod>? mods = args.Length == 4 ? ParsePlaylistMods(_rulesets.GetRuleset(mode), args[3], out error) : [];
        if (mods is null)
        {
            return error ?? "Invalid mods.";
        }

        ushort order = GetPlaylistOrderAfter(anchorId);
        MultiplayerPlaylistItem item = new()
        {
            BeatmapID = map.Id,
            BeatmapChecksum = map.Checksum,
            RulesetID = mode,
            OwnerID = actor.Id,
            RequiredMods = mods,
            AllowedMods = [],
            PlaylistOrder = order,
            WinCondition = WinCondition.Score,
        };
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        if (anchorId != 0)
        {
            foreach (MultiplayerPlaylistItem existing in _playlist.Where(existing => !existing.Expired && existing.PlaylistOrder >= order).OrderByDescending(existing => existing.PlaylistOrder).ToArray())
            {
                existing.PlaylistOrder = checked((ushort)(existing.PlaylistOrder + 1));
                await repository.EditItemToPlaylist(existing, RoomId, existing.OwnerID).ConfigureAwait(false);
            }
        }

        Playlist stored = await repository.AddItemToPlaylist(item, RoomId, actor.Id).ConfigureAwait(false);
        item.ID = stored.Id;
        _playlist.Add(item);
        _playlist.Sort((left, right) => left.PlaylistOrder.CompareTo(right.PlaylistOrder));
        string message = FormattableString.Invariant($"Added playlist item {item.ID} for beatmap #{item.BeatmapID}.");
        if (CurrentItem is not null and { Expired: false })
        {
            return message;
        }

        await ApplyPlaylistItemAsync(item).ConfigureAwait(false);
        UnreadyPlayers();
        BroadcastState();
        return message;
    }

    private async Task<string> EditPlaylistAsync(IPlayer actor, string[] args)
    {
        if (args.Length < 2 || !long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0)
        {
            return "Usage: !mp playlist edit <id> beatmap=xxx ruleset_id=xxx mods=xxx.";
        }

        MultiplayerPlaylistItem? existing = _playlist.FirstOrDefault(item => item.ID == id);
        if (existing is null)
        {
            return "Playlist item not found.";
        }

        if (existing.Expired || (InProgress && existing.ID == CurrentItem?.ID))
        {
            return "That playlist item cannot be edited right now.";
        }

        Dictionary<string, string> changes = [];
        foreach (string argument in args[1..])
        {
            int separator = argument.IndexOf('=');
            if (separator <= 0)
            {
                return "Usage: !mp playlist edit <id> beatmap=xxx ruleset_id=xxx mods=xxx.";
            }

            changes[argument[..separator].ToLowerInvariant()] = argument[(separator + 1)..];
        }

        if (changes.Count == 0 || changes.Keys.Any(key => key is not ("beatmap" or "ruleset_id" or "mods")))
        {
            return "Edit at least one of beatmap=xxx, ruleset_id=xxx, or mods=xxx.";
        }

        int beatmapId = existing.BeatmapID;
        if (changes.TryGetValue("beatmap", out string? beatmapText) && (!int.TryParse(beatmapText, NumberStyles.None, CultureInfo.InvariantCulture, out beatmapId) || beatmapId <= 0))
        {
            return "Invalid beatmap ID.";
        }

        int mode = existing.RulesetID;
        if (changes.TryGetValue("ruleset_id", out string? rulesetText) && (!int.TryParse(rulesetText, NumberStyles.None, CultureInfo.InvariantCulture, out mode) || mode is < 0 or > 3))
        {
            return "Unknown Stable ruleset ID; use 0-3.";
        }

        using IServiceScope scope = _scopes.CreateScope();
        IBeatmapRepository maps = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        Beatmap? map = await maps.GetByIdAsync(beatmapId).ConfigureAwait(false);
        if (map is null && scope.ServiceProvider.GetService<IFetcher>() is { } fetcher)
        {
            map = await maps.GetOrFetchByIdAsync(beatmapId, fetcher).ConfigureAwait(false);
        }

        if (map?.Checksum is not { Length: 32 } || (map.Mode != 0 && mode != map.Mode))
        {
            return "Beatmap not found or incompatible with the requested ruleset.";
        }

        List<APIMod> mods = existing.RequiredMods.ToList();
        if (changes.TryGetValue("mods", out string? modsText))
        {
            List<APIMod>? parsedMods = ParsePlaylistMods(_rulesets.GetRuleset(mode), modsText, out string? error);
            if (parsedMods is null)
            {
                return error!;
            }

            mods = parsedMods;
        }

        MultiplayerPlaylistItem edited = new()
        {
            ID = existing.ID,
            PlaylistOrder = existing.PlaylistOrder,
            BeatmapID = map.Id,
            BeatmapChecksum = map.Checksum,
            RulesetID = mode,
            OwnerID = actor.Id,
            RequiredMods = mods,
            AllowedMods = existing.AllowedMods,
            Freestyle = existing.Freestyle,
            Expired = false,
            PlayedAt = existing.PlayedAt,
            WinCondition = existing.WinCondition,
        };
        await scope.ServiceProvider.GetRequiredService<IRoomRepository>()
            .EditItemToPlaylist(edited, RoomId, actor.Id).ConfigureAwait(false);
        ReplaceCurrentItem(edited);
        string message = FormattableString.Invariant($"Edited playlist item {id}.");
        if (CurrentItem?.ID != edited.ID)
        {
            return message;
        }

        await ApplyPlaylistItemAsync(edited).ConfigureAwait(false);
        UnreadyPlayers();
        BroadcastState();
        return message;
    }

    private async Task<string> RemovePlaylistAsync(IPlayer actor, string[] args)
    {
        if (args.Length != 1 || !long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0)
        {
            return "Usage: !mp playlist remove <id>.";
        }

        MultiplayerPlaylistItem? item = _playlist.FirstOrDefault(candidate => candidate.ID == id);
        if (item is null)
        {
            return "Playlist item not found.";
        }

        if (item.Expired || (item.ID == CurrentItem?.ID && InProgress))
        {
            return "That playlist item cannot be removed right now.";
        }

        if (_playlist.Count(candidate => !candidate.Expired) <= 1)
        {
            return "The current playlist item cannot be removed.";
        }

        using IServiceScope scope = _scopes.CreateScope();
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        if (await repository.AnyScoreTokenExistsFor(item.ID).ConfigureAwait(false))
        {
            return "That playlist item already has scores and cannot be removed.";
        }

        await repository.RemoveItemToPlaylist(item.ID, RoomId, actor.Id).ConfigureAwait(false);
        _playlist.Remove(item);
        await NormalizePlaylistOrderAsync(repository).ConfigureAwait(false);
        string message = FormattableString.Invariant($"Removed playlist item {id}.");
        if (item.ID != CurrentItem?.ID)
        {
            return message;
        }

        CurrentItem = null;
        await AdvanceToNextPlaylistItemAsync().ConfigureAwait(false);
        BroadcastState();
        return message;
    }

    private ushort GetPlaylistOrderAfter(long anchorId)
    {
        if (anchorId == 0)
        {
            return checked((ushort)(_playlist.Where(item => !item.Expired).Select(item => (int)item.PlaylistOrder).DefaultIfEmpty(-1).Max() + 1));
        }

        MultiplayerPlaylistItem? anchor = _playlist.FirstOrDefault(item => item.ID == anchorId && !item.Expired);
        return checked((ushort)(anchor is null
            ? _playlist.Where(item => !item.Expired).Select(item => (int)item.PlaylistOrder).DefaultIfEmpty(-1).Max() + 1
            : anchor.PlaylistOrder + 1));
    }

    private async Task NormalizePlaylistOrderAsync(IRoomRepository repository)
    {
        foreach ((MultiplayerPlaylistItem item, int index) in _playlist.Where(item => !item.Expired).OrderBy(item => item.PlaylistOrder).ThenBy(item => item.ID).Select((item, index) => (item, index)))
        {
            if (item.PlaylistOrder == index)
            {
                continue;
            }

            item.PlaylistOrder = checked((ushort)index);
            await repository.EditItemToPlaylist(item, RoomId, item.OwnerID).ConfigureAwait(false);
        }
    }

    private async Task<string> ListRefereesAsync()
    {
        if (_referees.Count == 0)
        {
            return "No referees in this room.";
        }

        using IServiceScope scope = _scopes.CreateScope();
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        List<string> names = [];
        foreach (int id in _referees.Order())
        {
            names.Add(await users.GetUsernameByIdAsync(id).ConfigureAwait(false) ?? "#" + id.ToString(CultureInfo.InvariantCulture));
        }

        return "Referees: " + string.Join(", ", names);
    }

    private async Task<string> RenameAsync(string[] args)
    {
        string name = string.Join(' ', args).Trim();
        if (name.Length is < 1 or > 100)
        {
            return "Usage: !mp name <title> (1-100 characters).";
        }

        if (Host is null)
        {
            return "The room host must join before changing settings.";
        }

        Match settings = CopySettings(_match);
        settings.Name = name;
        await ChangeSettingsAsync(Host, settings).ConfigureAwait(false);
        return $"Room name updated to \"{_match.Name}\".";
    }

    private async Task<string> PasswordAsync(string[] args)
    {
        string password = string.Join(' ', args);
        if (password.Length > 40 || Host is null)
        {
            return "Usage: !mp password [password] (up to 40 characters; the host must be in the room).";
        }

        await ChangePasswordAsync(Host, password).ConfigureAwait(false);
        return password.Length == 0 ? "Room password removed." : "Room password updated.";
    }

    private async Task<string> ResizeAsync(string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size is < 2 or > 16)
        {
            return "Usage: !mp size <2-16>.";
        }

        if (_slots.Skip(size).Any(slot => slot.Player is not null))
        {
            return "Cannot shrink the room across occupied slots.";
        }

        using IServiceScope scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRoomRepository>().UpdateRoomSettings(RoomId, ToRoomSettings(_match, size)).ConfigureAwait(false);
        foreach (RoomSlot slot in _slots.Skip(Math.Min(size, _size)).Take(Math.Abs(size - _size)))
        {
            slot.Status = size > _size ? SlotStatus.Open : SlotStatus.Locked;
        }

        _size = size;
        BroadcastState();
        return FormattableString.Invariant($"Room size changed to {size}.");
    }

    private async Task<string> SetTeamModeAsync(string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int mode) || mode is not (0 or 2))
        {
            return "Usage: !mp set <0|2> (head to head or team versus).";
        }

        if (Host is null)
        {
            return "The room host must join before changing settings.";
        }

        Match settings = CopySettings(_match);
        settings.TeamType = (MatchTeamType)mode;
        await ChangeSettingsAsync(Host, settings).ConfigureAwait(false);
        return $"Team mode changed to {_match.TeamType}.";
    }

    private async Task<User?> ResolveUserAsync(string[] args)
    {
        string name = string.Join(' ', args).Trim();
        if (name.Length == 0)
        {
            return null;
        }

        using IServiceScope scope = _scopes.CreateScope();
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        return name.StartsWith('#') && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            ? await users.GetByIdAsync(id).ConfigureAwait(false)
            : await users.GetByUsernameAsync(name).ConfigureAwait(false);
    }

    private async Task<string> UserCommandAsync(IPlayer actor, string command, string[] args)
    {
        User? user = await ResolveUserAsync(args).ConfigureAwait(false);
        if (user is null)
        {
            return $"User not found. Usage: !mp {command} <username|#userid>.";
        }

        IPlayer? target = Channel.GetMembers().FirstOrDefault(member => member.Id == user.Id);
        if (string.Equals(command, "invite", StringComparison.Ordinal))
        {
            target = _manager.GetOnlinePlayer(user.Id);
            if (target is null || target.Id == actor.Id || target.IsRestricted)
            {
                return "That player is not available for an invitation.";
            }

            if (await PrivateMessagePolicy.CheckAsync(actor, target, _scopes).ConfigureAwait(false) is { } rejection)
            {
                actor.Enqueue(rejection);
                return "That player cannot receive this invitation.";
            }

            string link = FormattableString.Invariant($"osump://{MatchId}/{Uri.EscapeDataString(_match.Password ?? string.Empty)}");
            target.Enqueue(new PacketMatchInvite
            {
                Message = new Message { Sender = actor.Name, SenderId = actor.Id, Recipient = target.Name, Text = $"Come join my multiplayer match: [{link} {_match.Name}]" }
            });
            return $"Invited {user.Username}.";
        }

        if (target is null && !string.Equals(command, "removeref", StringComparison.Ordinal))
        {
            return "That player is not in this room.";
        }

        switch (command)
        {
            case "addref":
                _referees.Add(user.Id);
                return $"Added {user.Username} as a referee.";
            case "removeref":
                _referees.Remove(user.Id);
                return $"Removed {user.Username} as a referee.";
            case "host":
                int index = FindSlotIndex(target!);
                if (index < 0 || Host is null)
                {
                    return "The new host must occupy a player slot.";
                }

                await TransferHostAsync(Host, index).ConfigureAwait(false);
                return $"Transferred host to {user.Username}.";
            case "kick":
            case "ban":
                if (user.Id == actor.Id || user.Id == _hostUserId || _referees.Contains(user.Id))
                {
                    return "The host, referees, and command sender cannot be kicked or banned.";
                }

                if (string.Equals(command, "ban", StringComparison.Ordinal))
                {
                    _bannedUsers.Add(user.Id);
                }

                foreach (IPlayer session in Channel.GetMembers().Where(member => member.Id == user.Id))
                {
                    await LeaveAsync(session).ConfigureAwait(false);
                    await ObserveAsync(session, join: false).ConfigureAwait(false);
                    session.Enqueue(new PacketUpdateMatch { Match = GetMatch() });
                }

                return $"{(string.Equals(command, "ban", StringComparison.Ordinal) ? "Banned" : "Kicked")} {user.Username}.";
            default:
                return "Unknown player command.";
        }
    }

    private async Task<string> MapAsync(string[] args)
    {
        if (args.Length is < 1 or > 2 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
        {
            return "Usage: !mp map <beatmap_id> [ruleset_id].";
        }

        int mode = -1;
        if (args.Length == 2 && (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out mode) || mode is < 0 or > 3))
        {
            return "Unknown Stable ruleset ID; use 0-3.";
        }

        if (Host is null)
        {
            return "The room host must join before changing the beatmap.";
        }

        using IServiceScope scope = _scopes.CreateScope();
        IBeatmapRepository maps = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        Beatmap? map = await maps.GetByIdAsync(id).ConfigureAwait(false);
        if (map is null && scope.ServiceProvider.GetService<IFetcher>() is { } fetcher)
        {
            map = await maps.GetOrFetchByIdAsync(id, fetcher).ConfigureAwait(false);
        }

        if (map?.Checksum is not { Length: 32 })
        {
            return "Beatmap not found.";
        }

        mode = mode < 0 ? map.Mode : mode;
        if (mode is < 0 or > 3 || (map.Mode != 0 && mode != map.Mode))
        {
            return "That beatmap cannot be played in the requested ruleset.";
        }

        Match settings = CopySettings(_match);
        settings.MapId = map.Id;
        settings.MapMd5 = map.Checksum;
        settings.MapName = map.Version;
        settings.Mode = (sbyte)mode;
        if (CurrentItem is { Expired: false } current)
        {
            MultiplayerPlaylistItem edited = CreatePlaylistItem(settings, current.OwnerID);
            edited.ID = current.ID;
            edited.PlaylistOrder = current.PlaylistOrder;
            using IServiceScope editScope = _scopes.CreateScope();
            await editScope.ServiceProvider.GetRequiredService<IRoomRepository>()
                .EditItemToPlaylist(edited, RoomId, edited.OwnerID).ConfigureAwait(false);
            ReplaceCurrentItem(edited);
            _match = settings;
            UnreadyPlayers();
            BroadcastState();
        }
        else
        {
            await ChangeSettingsAsync(Host, settings).ConfigureAwait(false);
        }

        return _match.MapId == map.Id && _match.Mode == mode
            ? FormattableString.Invariant($"Beatmap changed to #{map.Id} ({_rulesets.GetRuleset(mode).RulesetInfo.Name}).")
            : "The selected beatmap could not be resolved.";
    }

    private async Task<string> ModsAsync(string[] args)
    {
        if (Host is null)
        {
            return "The room host must join before changing mods.";
        }

        Ruleset ruleset = _rulesets.GetRuleset(_match.Mode);
        bool freeMods = args.Any(argument => string.Equals(argument, "freemod", StringComparison.OrdinalIgnoreCase) || string.Equals(argument, "freemods", StringComparison.OrdinalIgnoreCase) || string.Equals(argument, "fm", StringComparison.OrdinalIgnoreCase));
        List<Mod> mods = [];
        foreach (string argument in args)
        {
            string acronym = argument.TrimStart('+');
            if (string.Equals(acronym, "freemod", StringComparison.OrdinalIgnoreCase) || string.Equals(acronym, "freemods", StringComparison.OrdinalIgnoreCase) || string.Equals(acronym, "fm", StringComparison.OrdinalIgnoreCase) || string.Equals(acronym, "nm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Mod? mod = ruleset.CreateModFromAcronym(acronym);
            if (mod is null || ruleset.ConvertToLegacyMods([mod]) == LegacyMods.None)
            {
                return $"Unsupported Stable mod: {argument}.";
            }

            if (mods.TrueForAll(existing => !string.Equals(existing.Acronym, mod.Acronym, StringComparison.Ordinal)))
            {
                mods.Add(mod);
            }
        }

        if (mods.Exists(mod => mods.Exists(other => !ReferenceEquals(other, mod) && mod.IncompatibleMods.Any(type => type.IsInstanceOfType(other)))))
        {
            return "The requested mods are incompatible.";
        }

        Match settings = CopySettings(_match);
        settings.FreeMods = freeMods;
        await ChangeSettingsAsync(Host, settings).ConfigureAwait(false);
        LegacyMods legacy = ruleset.ConvertToLegacyMods(mods.ToArray());
        await ChangeModsAsync(Host, legacy).ConfigureAwait(false);
        return $"Mods updated to {_match.Mods}{(freeMods ? " with free mods" : string.Empty)}.";
    }

    private async Task<string> SlotCommandAsync(string command, string[] args)
    {
        if (args.Length < 2)
        {
            return $"Usage: !mp {command} <username|#userid> {(string.Equals(command, "move", StringComparison.Ordinal) ? "<slot>" : "<red|blue>")}.";
        }

        User? user = await ResolveUserAsync(args[..^1]).ConfigureAwait(false);
        RoomSlot? slot = _slots.FirstOrDefault(candidate => candidate.Player?.Id == user?.Id && candidate.Player is not null);
        if (slot?.Player is not { } target)
        {
            return "That player is not in a player slot.";
        }

        if (string.Equals(command, "move", StringComparison.Ordinal))
        {
            if (!int.TryParse(args[^1], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1 || index > _size || _slots[index - 1].Status != SlotStatus.Open)
            {
                return "The destination must be an open slot within the room size.";
            }

            await ChangeSlotAsync(target, index - 1).ConfigureAwait(false);
            return FormattableString.Invariant($"Moved {target.Name} to slot {index}.");
        }

        MatchTeam team = args[^1].ToLowerInvariant() switch { "red" => MatchTeam.Red, "blue" => MatchTeam.Blue, _ => MatchTeam.Neutral };
        if (!IsTeamMode(_match.TeamType) || team == MatchTeam.Neutral)
        {
            return "Use red or blue in a team-versus room.";
        }

        slot.Team = team;
        if (slot.Status == SlotStatus.Ready)
        {
            slot.Status = SlotStatus.NotReady;
        }

        BroadcastState();
        return $"Moved {target.Name} to team {args[^1].ToLowerInvariant()}.";
    }

    private async Task<string> StartCommandAsync(string[] args, RoomCountdownService countdowns)
    {
        if (!TryDelay(args, 0, out int seconds))
        {
            return "Usage: !mp start [seconds] (0-3600).";
        }

        if (Host is null || CurrentItem is null || !_slots.Any(slot => slot.Status == SlotStatus.Ready))
        {
            return "Can't start the match without a beatmap and at least one ready player.";
        }

        countdowns.Cancel(this);
        if (seconds > 0)
        {
            countdowns.Schedule(this, seconds, startMatch: true);
            return FormattableString.Invariant($"Match starts in {seconds} second(s).");
        }

        await StartAsync(Host).ConfigureAwait(false);
        return InProgress ? "Started the match." : "Could not start the match.";
    }

    private async Task CloseAsync(IPlayer actor)
    {
        _closing = true;
        foreach (IPlayer player in _slots.Where(slot => slot.Player is not null).Select(slot => slot.Player!).OrderBy(player => ReferenceEquals(player, actor)).ToArray())
        {
            await LeaveAsync(player).ConfigureAwait(false);
            player.Enqueue(new PacketDisposeMatch { Id = MatchId });
        }

        if (!IsDisposed)
        {
            using IServiceScope scope = _scopes.CreateScope();
            IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            await repository.UpdateRoomHost(RoomId, hostUserId: null).ConfigureAwait(false);
            await repository.SetRoomEndDate(RoomId, DateTimeOffset.UtcNow).ConfigureAwait(false);
            foreach (IPlayer observer in _observers.ToArray())
            {
                await ObserveAsync(observer, join: false).ConfigureAwait(false);
                observer.Enqueue(new PacketDisposeMatch { Id = MatchId });
            }

            Host = null;
            _match.InProgress = false;
            IsDisposed = true;
            _manager.RemoveRoom(this);
        }
    }
}