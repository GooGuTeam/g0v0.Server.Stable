// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C16 START_SPECTATING. Payload members are declared in wire order.
/// </summary>
[OsuPacket(16)]
public class PacketStartSpectating : IClientPacket
{
    /// <summary>
    /// Gets or sets TargetId.
    /// </summary>
    public int TargetId { get; set; }

    /// <inheritdoc />
    public async Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        IPlayer? target = facade.PlayerManager.GetPlayer(TargetId);
        if (target is { IsRestricted: false } && !ReferenceEquals(target, player))
        {
            await player.WatchPlayer(target);
        }

        return [];
    }
}