// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// The channel entry a server packet carries: its name, the topic shown in the client's channel list
/// and how many sessions are inside it.
/// </summary>
public class ChannelWire
{
    /// <summary>
    /// Gets or sets the channel name, including the leading <c>#</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the topic shown next to the channel name.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of sessions currently inside the channel.
    /// </summary>
    public ushort PlayerCount { get; set; }
}