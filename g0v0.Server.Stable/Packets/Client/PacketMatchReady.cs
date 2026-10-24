// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C39 MATCH_READY. Empty payload.
/// </summary>
[OsuPacket(39)]
public class PacketMatchReady : IClientPacket
{
    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        // The handler is not implemented yet; the dispatcher logs and skips it, so an explicit
        // unsupported-operation error keeps the poll alive instead of failing the request.
        throw new NotSupportedException($"{GetType().Name} is not implemented by this server yet.");
    }
}