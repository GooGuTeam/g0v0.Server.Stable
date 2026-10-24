// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;
using osu.Game.Online.API;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C3 REQUEST_STATUS_UPDATE. Empty payload.
/// </summary>
[OsuPacket(3, allowRestrict: true)]
public class PacketRequestStatusUpdate : IClientPacket
{
    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        await RefreshStatistics(player, player.Activity.Mode, facade);
        if (player.Activity.Mods.Length != 0)
        {
            player.Mods = facade.RulesetManager.GetRuleset(player.Activity.Mode).ConvertToLegacyMods(player.Activity.Mods);
        }

        return [player.GetStats()];
    }

    internal static async Task RefreshStatistics(IPlayer player, byte mode, IPacketFacade facade, IEnumerable<APIMod>? mods = null)
    {
        using IServiceScope scope = facade.ScopeFactory.CreateScope();
        IUserStatisticsRepository statisticsRepository = scope.ServiceProvider.GetRequiredService<IUserStatisticsRepository>();
        GameConfig game = scope.ServiceProvider.GetService<ConfigManager>()?.TryGet<GameConfig>() ?? new GameConfig();
        int statisticsMode = ScoreModeUtils.GetScoreMode(mode, mods ?? player.Activity.Mods.Select(m => new APIMod(m)), game);
        UserStatistics? statistics = await statisticsRepository.GetByUserIdAndModeAsync(player.Id, statisticsMode);
        int? globalRank = await statisticsRepository.GetGlobalRankAsync(player.Id, statisticsMode);
        player.Statistics.RankedScore = statistics?.RankedScore ?? 0;
        player.Statistics.Accuracy = (float)Math.Clamp((statistics?.HitAccuracy ?? 0) / 100, 0, 1);
        player.Statistics.PlayCount = statistics?.PlayCount ?? 0;
        player.Statistics.TotalScore = statistics?.TotalScore ?? 0;
        player.Statistics.Rank = globalRank ?? 0;
        player.Statistics.Pp = (ushort)Math.Clamp(statistics?.Pp ?? 0, 0, ushort.MaxValue);
    }
}