// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;

namespace g0v0.Server.Stable.Objects.Chats;

/// <summary>
/// Represents one stable chat channel and the connected sessions that joined it.
/// </summary>
/// <remarks>
/// Membership is connection-scoped: the stable protocol only needs to know which live
/// sessions are inside a channel, so nothing here is persisted. Message history is owned
/// by the shared chat storage used by the lazer endpoints.
/// </remarks>
public class StableChannel
{
    private readonly Lock _lock = new();
    private readonly List<IPlayer> _members = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="StableChannel"/> class.
    /// </summary>
    /// <param name="name">The wire name, including the leading <c>#</c>.</param>
    /// <param name="topic">The topic shown in the client's channel list.</param>
    /// <param name="writePrivileges">The privileges allowed to post, or <see langword="null"/> when everyone may post.</param>
    /// <param name="autoJoin">Whether the channel is part of the directory offered at login.</param>
    /// <param name="channelId">The identifier of the stored channel row, or <see langword="null"/> when it is not persisted.</param>
    public StableChannel(
        string name,
        string topic,
        ClientPrivileges? writePrivileges = null,
        bool autoJoin = true,
        int? channelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Topic = topic;
        WritePrivileges = writePrivileges;
        AutoJoin = autoJoin;
        ChannelId = channelId;
    }

    /// <summary>
    /// Gets the wire name, including the leading <c>#</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the identifier of the stored channel row, or <see langword="null"/> when the channel
    /// exists only in memory and messages cannot be persisted for it.
    /// </summary>
    public int? ChannelId { get; }

    /// <summary>
    /// Gets the topic shown in the client's channel list.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the privileges allowed to post, or <see langword="null"/> when everyone may post.
    /// </summary>
    public ClientPrivileges? WritePrivileges { get; }

    /// <summary>
    /// Gets a value indicating whether the channel is part of the directory offered at login.
    /// </summary>
    public bool AutoJoin { get; }

    /// <summary>
    /// Gets the number of joined sessions.
    /// </summary>
    /// <returns>The joined session count.</returns>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _members.Count;
            }
        }
    }

    /// <summary>
    /// Gets a snapshot of the joined sessions, in join order.
    /// </summary>
    /// <returns>The joined sessions.</returns>
    public IReadOnlyList<IPlayer> GetMembers()
    {
        lock (_lock)
        {
            return _members.ToArray();
        }
    }

    /// <summary>
    /// Determines whether a session has joined this channel.
    /// </summary>
    /// <param name="player">The session to look up.</param>
    /// <returns>Whether the session is a member.</returns>
    public bool Contains(IPlayer player)
    {
        lock (_lock)
        {
            return _members.Contains(player);
        }
    }

    /// <summary>
    /// Adds a session to the channel.
    /// </summary>
    /// <param name="player">The session to add.</param>
    /// <returns>Whether the session was newly added.</returns>
    public bool Add(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            if (_members.Contains(player))
            {
                return false;
            }

            _members.Add(player);
            return true;
        }
    }

    /// <summary>
    /// Removes a session from the channel.
    /// </summary>
    /// <param name="player">The session to remove.</param>
    /// <returns>Whether the session was a member.</returns>
    public bool Remove(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            return _members.Remove(player);
        }
    }

    /// <summary>
    /// Removes every session that is no longer online.
    /// </summary>
    /// <returns>The number of sessions that were dropped.</returns>
    public int RemoveOfflineMembers()
    {
        lock (_lock)
        {
            return _members.RemoveAll(member => !member.IsOnline);
        }
    }

    /// <summary>
    /// Determines whether a session may post to this channel.
    /// </summary>
    /// <param name="player">The session attempting to post.</param>
    /// <returns>Whether the session may post.</returns>
    public bool CanWrite(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return WritePrivileges is not { } required || player.Privileges.HasFlag(required);
    }
}