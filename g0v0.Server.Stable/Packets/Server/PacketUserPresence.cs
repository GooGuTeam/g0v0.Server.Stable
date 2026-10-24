// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Attributes;

namespace g0v0.Server.Stable.Packets.Server;

/// <summary>
/// S83 USER_PRESENCE. Payload members are declared in wire order.
/// </summary>
[OsuPacket(83)]
public class PacketUserPresence : IPacket
{
    /// <summary>
    /// Gets or sets UserId.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Gets or sets Name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets UtcOffsetPlus24.
    /// </summary>
    public byte UtcOffsetPlus24 { get; set; }

    /// <summary>
    /// Gets or sets CountryCode.
    /// </summary>
    public byte CountryCode { get; set; }

    /// <summary>
    /// Gets or sets the payload value. Low five bits are ClientPrivileges; upper bits are the vanilla mode (0–3).
    /// </summary>
    public byte PackedPrivilegesMode { get; set; }

    /// <summary>
    /// Gets or sets Longitude.
    /// </summary>
    public float Longitude { get; set; }

    /// <summary>
    /// Gets or sets Latitude.
    /// </summary>
    public float Latitude { get; set; }

    /// <summary>
    /// Gets or sets Rank.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Extracts client privilege bits.
    /// </summary>
    /// <returns>The client privileges.</returns>
    public ClientPrivileges GetPrivileges() => (ClientPrivileges)(PackedPrivilegesMode & 31);

    /// <summary>
    /// Extracts the vanilla game mode.
    /// </summary>
    /// <returns>The vanilla game mode (0–3).</returns>
    public byte GetMode() => (byte)(PackedPrivilegesMode >> 5);

    /// <summary>
    /// Packs privileges and a vanilla mode into their shared wire byte.
    /// </summary>
    /// <param name="privileges">The client privilege bits.</param>
    /// <param name="mode">The vanilla game mode (0–3).</param>
    public void SetPrivilegesAndMode(ClientPrivileges privileges, byte mode)
    {
        if (mode > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        PackedPrivilegesMode = (byte)(((int)privileges & 31) | (mode << 5));
    }
}