// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.States;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO.Legacy;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// Reads and applies the activity supplied by a stable client.
/// </summary>
[OsuPacket(0, allowRestrict: true, hasCustomReader: true)]
public class PacketChangeAction : IClientPacket
{
    private LegacyMods? _legacyMods;

    /// <summary>
    /// Gets or sets the activity reported by the client.
    /// </summary>
    public Activity Activity { get; set; } = new();

    /// <summary>
    /// Reads activity while preserving the original wire mod flags.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <param name="facade">The available rulesets.</param>
    /// <returns>The parsed activity packet.</returns>
    public static PacketChangeAction Read(SerializationReader reader, IPacketFacade facade)
    {
        byte action = reader.ReadByte();
        string infoText = reader.ReadString();
        string mapHash = reader.ReadString();
        uint modId = reader.ReadUInt32();
        byte mode = reader.ReadByte();
        int mapId = reader.ReadInt32();

        Mod[] mods = [];
        try
        {
            Ruleset ruleset = facade.RulesetManager.GetRuleset(mode);
            mods = ruleset.ConvertFromLegacyMods((LegacyMods)modId).ToArray();
        }
        catch (ArgumentException)
        {
        }

        return new PacketChangeAction
        {
            _legacyMods = (LegacyMods)modId,
            Activity = new Activity
            {
                Action = (UserAction)action,
                InfoText = infoText,
                MapHash = mapHash,
                Mods = mods,
                Mode = mode,
                MapId = mapId
            }
        };
    }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        LegacyMods mods = _legacyMods ?? (Activity.Mods.Length == 0
            ? LegacyMods.None
            : facade.RulesetManager.GetRuleset(Activity.Mode).ConvertToLegacyMods(Activity.Mods));
        await PacketRequestStatusUpdate.RefreshStatistics(player, Activity.Mode, facade, Activity.Mods.Select(m => new APIMod(m)));
        await player.ChangePlayerActivityAsync(Activity, mods);
        return [];
    }
}