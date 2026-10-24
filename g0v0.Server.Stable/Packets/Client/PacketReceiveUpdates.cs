// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C79 RECEIVE_UPDATES. Payload members are declared in wire order.
/// </summary>
[OsuPacket(79, allowRestrict: true)]
public class PacketReceiveUpdates : IClientPacket
{
    /// <summary>
    /// Gets or sets Value.
    /// </summary>
    public PresenceFilter Value { get; set; }

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        if (Enum.IsDefined(Value))
        {
            player.PresenceFilter = Value;
        }

        return Task.FromResult<IPacket[]>([]);
    }
}