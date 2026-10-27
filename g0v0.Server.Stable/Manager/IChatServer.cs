// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;

namespace g0v0.Server.Stable.Manager;

/// <summary>
/// Tracks the stable chat channels, their membership and the messages exchanged by
/// connected <see cref="IPlayer"/> sessions.
/// </summary>
/// <remarks>
/// This mirrors the division of labour of the realtime server: the player registry owns
/// sessions, while the chat server owns channels and talks to sessions through
/// <see cref="IPlayer.Enqueue"/>. Every method returns the packets that belong to the
/// calling session so the route handler can send them back in the same HTTP response;
/// packets for other sessions are queued directly.
/// </remarks>
public interface IChatServer
{
    /// <summary>
    /// Gets the channel directory a client receives while logging in.
    /// </summary>
    /// <param name="player">The connecting session.</param>
    /// <returns>The <c>S65 CHANNEL_INFO</c> packets of every joinable channel followed by <c>S89 CHANNEL_INFO_END</c>. The stable client leaves its "Receiving data..." state only after that terminator, so it must always be part of the login response.</returns>
    Task<IPacket[]> ConnectAsync(IPlayer player);

    /// <summary>
    /// Drops every channel membership of a session that went offline.
    /// </summary>
    /// <param name="player">The disconnecting session.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DisconnectAsync(IPlayer player);

    /// <summary>
    /// Joins a session to a channel by its wire name.
    /// </summary>
    /// <param name="player">The joining session.</param>
    /// <param name="channelName">The channel name, including the leading <c>#</c>.</param>
    /// <returns><c>S64 CHANNEL_JOIN_SUCCESS</c> for the joining session, or an empty array when the channel does not exist or the session is already a member. Other members receive the updated <c>S65 CHANNEL_INFO</c> directly.</returns>
    Task<IPacket[]> JoinChannelAsync(IPlayer player, string channelName);

    /// <summary>
    /// Removes a session from a channel.
    /// </summary>
    /// <param name="player">The leaving session.</param>
    /// <param name="channelName">The channel name, including the leading <c>#</c>.</param>
    /// <returns><c>S66 CHANNEL_KICK</c> for the leaving session, or an empty array when the session was not a member. Remaining members receive the updated <c>S65 CHANNEL_INFO</c>.</returns>
    Task<IPacket[]> LeaveChannelAsync(IPlayer player, string channelName);

    /// <summary>
    /// Sends a message to a channel the session joined.
    /// </summary>
    /// <param name="player">The sending session.</param>
    /// <param name="channelName">The channel name, including the leading <c>#</c>.</param>
    /// <param name="text">The message text as typed by the player.</param>
    /// <returns>Packets for the sending session only, such as a truncation notice.</returns>
    Task<IPacket[]> SendChannelMessageAsync(IPlayer player, string channelName, string text);

    /// <summary>
    /// Sends a private message to another user by name.
    /// </summary>
    /// <param name="player">The sending session.</param>
    /// <param name="recipientName">The recipient username.</param>
    /// <param name="text">The message text as typed by the player.</param>
    /// <returns>Packets for the sending session only, such as an "offline" notice.</returns>
    Task<IPacket[]> SendPrivateMessageAsync(IPlayer player, string recipientName, string text);

    /// <summary>Broadcasts a server-generated BanchoBot message to one exact channel.</summary>
    /// <param name="channel">The authorized destination channel.</param>
    /// <param name="text">The reply, split using the configured message limit.</param>
    /// <returns>A task representing delivery and persistence.</returns>
    Task SendBotMessageAsync(StableChannel channel, string text);

    /// <summary>
    /// Gets the channels offered in the login directory.
    /// </summary>
    /// <returns>The auto-join channels in display order.</returns>
    IReadOnlyList<StableChannel> GetChannels();

    /// <summary>
    /// Gets the channels a session is currently joined to.
    /// </summary>
    /// <param name="player">The session to inspect.</param>
    /// <returns>The joined channels in display order.</returns>
    IReadOnlyList<StableChannel> GetPlayerChannels(IPlayer player);

    /// <summary>
    /// Joins a room's private channel without advertising it in the public directory.
    /// </summary>
    /// <param name="player">The joining session.</param>
    /// <param name="channel">The server-authorized room channel.</param>
    /// <returns>A task that completes after membership is stored and announced.</returns>
    Task JoinMultiplayerChannelAsync(IPlayer player, StableChannel channel);

    /// <summary>
    /// Removes this exact session from a private room channel.
    /// </summary>
    /// <param name="player">The departing session.</param>
    /// <param name="channel">The server-authorized room channel.</param>
    /// <returns>A task that completes after membership is removed.</returns>
    Task LeaveMultiplayerChannelAsync(IPlayer player, StableChannel channel);
}