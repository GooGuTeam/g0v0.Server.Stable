// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;
using osu.Game.Beatmaps.Legacy;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C51 MATCH_CHANGE_MODS. Payload members are declared in wire order.
/// </summary>
[OsuPacket(51)]
public class PacketMatchChangeMods : IClientPacket
{
    /// <summary>
    /// Gets or sets the payload value. Signed i32 on wire; retain the same 32-bit Mods bit pattern.
    /// </summary>
    public LegacyMods Mods { get; set; }

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        // The handler is not implemented yet; the dispatcher logs and skips it, so an explicit
        // unsupported-operation error keeps the poll alive instead of failing the request.
        throw new NotSupportedException($"{GetType().Name} is not implemented by this server yet.");
    }
}