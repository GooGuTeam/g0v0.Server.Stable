// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C97 USER_PRESENCE_REQUEST. Payload members are declared in wire order.
/// </summary>
[OsuPacket(97)]
public class PacketUserPresenceRequest : IClientPacket
{
    /// <summary>
    /// Gets or sets the payload value. IdList16: ushort count followed by signed 32-bit user IDs.
    /// </summary>
    public int[] UserIds { get; set; } = [];

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        IPacket[] packets = UserIds.Distinct().Select(facade.PlayerManager.GetPlayer)
            .Where(target => target != null && (!target.IsRestricted || ReferenceEquals(target, player)))
            .Select(target => (IPacket)target!.GetPresence()).ToArray();
        return Task.FromResult(packets);
    }
}