// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S5 USER_ID. Payload members are declared in wire order.
/// </summary>
[OsuPacket(5)]
public class PacketUserId : IPacket
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PacketUserId"/> class.
    /// </summary>
    public PacketUserId()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PacketUserId"/> class for a failed login.
    /// </summary>
    /// <param name="reason">The login failure reason.</param>
    public PacketUserId(LoginFailureReason reason)
    {
        UserId = (int)reason;
    }

    /// <summary>
    /// Gets or sets UserId.
    /// </summary>
    public int UserId { get; set; }
}