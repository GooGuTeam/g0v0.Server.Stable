// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Objects.Players;

namespace g0v0.Server.Stable.Packets.Client;

/// <summary>
/// C68 BEATMAP_INFO_REQUEST. Historical packet with no defined payload; Data preserves uninterpreted bytes.
/// </summary>
[OsuPacket(68, rawData: true)]
public class PacketBeatmapInfoRequest : IClientPacket
{
    /// <summary>
    /// Gets or sets the payload value. Raw payload bytes with no inner length prefix.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <inheritdoc />
    public Task<IPacket[]> Handle(IPlayer player, IPacketFacade facade)
    {
        // The handler is not implemented yet; the dispatcher logs and skips it, so an explicit
        // unsupported-operation error keeps the poll alive instead of failing the request.
        throw new NotSupportedException($"{GetType().Name} is not implemented by this server yet.");
    }
}