// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C98 USER_PRESENCE_REQUEST_ALL. Payload members are declared in wire order.
/// </summary>
[OsuPacket(98)]
public class PacketUserPresenceRequestAll : IClientPacket
{
    /// <summary>
    /// Gets or sets IngameTime.
    /// </summary>
    public int IngameTime { get; set; }

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        IPacket[] packets = facade.PlayerManager.GetAllPlayers().Select(target => target.Id).Distinct()
            .Select(facade.PlayerManager.GetPlayer)
            .Where(target => target is { IsRestricted: false })
            .Select(target => (IPacket)target!.GetPresence()).ToArray();
        return Task.FromResult(packets);
    }
}