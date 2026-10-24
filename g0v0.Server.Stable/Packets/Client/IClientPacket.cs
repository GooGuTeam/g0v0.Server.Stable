// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// A client packet that can be handled to produce response packets.
/// </summary>
public interface IClientPacket : IPacket
{
    /// <summary>
    /// Handles the client packet asynchronously.
    /// </summary>
    /// <param name="player">The session that sent the packet.</param>
    /// <param name="facade">Services available to the packet handler.</param>
    /// <returns>The response packets produced by the handler.</returns>
    Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade);
}