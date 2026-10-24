// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Attributes;

/// <summary>
/// Describes one registered stable packet: its type and the flags the dispatcher needs.
/// </summary>
/// <param name="Type">The packet type.</param>
/// <param name="AllowRestrict">Whether a restricted account may send this packet.</param>
public readonly record struct PacketRegistration(Type Type, bool AllowRestrict)
{
    /// <summary>
    /// Creates a registration from a packet type's <see cref="OsuPacketAttribute"/>.
    /// </summary>
    /// <param name="type">The packet type.</param>
    /// <returns>The registration, with <see cref="AllowRestrict"/> taken from the attribute.</returns>
    public static PacketRegistration For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new PacketRegistration(type, OsuPacketAttribute.IsAllowedForRestricted(type));
    }
}