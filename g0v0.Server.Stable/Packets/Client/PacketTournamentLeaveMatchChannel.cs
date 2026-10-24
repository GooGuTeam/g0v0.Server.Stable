// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C109 TOURNAMENT_LEAVE_MATCH_CHANNEL. Payload members are declared in wire order.
/// </summary>
[OsuPacket(109)]
public class PacketTournamentLeaveMatchChannel : IClientPacket
{
    /// <summary>
    /// Gets or sets MatchId.
    /// </summary>
    public int MatchId { get; set; }

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        // The handler is not implemented yet; the dispatcher logs and skips it, so an explicit
        // unsupported-operation error keeps the poll alive instead of failing the request.
        throw new NotSupportedException($"{GetType().Name} is not implemented by this server yet.");
    }
}