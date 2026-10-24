// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C4 PING. Empty payload.
/// </summary>
[OsuPacket(4, allowRestrict: true)]
public class PacketPing : IClientPacket
{
    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        return Task.FromResult<IPacket[]>([]);
    }
}