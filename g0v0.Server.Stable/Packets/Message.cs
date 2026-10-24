// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// One chat message as both directions of the protocol carry it.
/// </summary>
/// <remarks>
/// The same shape is used for public channel messages and private messages; the recipient decides
/// which one it is: a channel name starting with <c>#</c> is public, a username is private.
/// </remarks>
public class Message
{
    /// <summary>
    /// Gets or sets the display name of the sender.
    /// </summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the message body.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel name or username the message is addressed to.
    /// </summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user ID of the sender.
    /// </summary>
    public int SenderId { get; set; }
}