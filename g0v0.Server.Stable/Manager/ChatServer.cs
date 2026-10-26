// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Manager;

/// <summary>
/// Owns the stable chat channels: the login channel directory, channel membership and the chat
/// packets exchanged by connected sessions.
/// </summary>
/// <remarks>
/// <para>
/// Channel membership is held in memory, exactly like the player registry holds sessions; the stable
/// protocol has no persistent membership model. The channel list itself comes from the shared chat
/// storage, so stable and lazer clients see the same rooms. A deployment therefore has to create its
/// channels through the lazer endpoints before a stable client can join them.
/// </para>
/// <para>
/// Every handler returns the packets that belong to the calling session; packets aimed at other
/// sessions are queued on those sessions with <see cref="IPlayer.Enqueue"/> and leave on their next
/// poll.
/// </para>
/// <para>
/// Messages are written to the shared chat storage before they are delivered, so the moderation
/// history and the lazer clients stay in sync with what stable clients see.
/// </para>
/// </remarks>
/// <param name="logger">The logger.</param>
/// <param name="scopeFactory">The scope factory used to resolve the chat repository and configuration.</param>
/// <param name="players">The connected session registry.</param>
public class ChatServer(
    ILogger<ChatServer> logger,
    IServiceScopeFactory scopeFactory,
    IPlayerManager players) : IChatServer
{
    private const string TruncationNotice = "Your message was truncated\n(exceeded the configured message length limit).";

    private static readonly ClientPrivileges StaffPrivileges =
        ClientPrivileges.Moderator | ClientPrivileges.Owner | ClientPrivileges.Developer;

    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<IPlayer, StableChannel> _multiplayer = new(ReferenceEqualityComparer.Instance);
    private StableChannel[] _channels = [];
    private bool _loadedFromStorage;

    /// <inheritdoc />
    public async Task<IPacket[]> ConnectAsync(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        IReadOnlyList<StableChannel> channels = await EnsureChannelsAsync().ConfigureAwait(false);
        List<IPacket> packets = new(channels.Count + 1);
        foreach (StableChannel channel in channels)
        {
            channel.RemoveOfflineMembers();
            packets.Add(ChannelInfo(channel));
        }

        // S89 CHANNEL_INFO_END is what makes the client leave "Receiving data..." and show the
        // online channel list, so it is always sent - even without any channel.
        packets.Add(new PacketChannelInfoEnd());
        logger.LogDebug("Sent {ChannelCount} channel(s) to session {UserId}.", channels.Count, player.Id);
        return packets.ToArray();
    }

    /// <inheritdoc />
    public Task DisconnectAsync(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        players.LeaveSpectatorChannel(player);
        List<string> left = [];
        foreach (StableChannel channel in GetChannels().Where(channel => channel.Remove(player)))
        {
            left.Add(channel.Name);
            Broadcast(ChannelInfo(channel), except: player);
        }

        if (left.Count > 0)
        {
            logger.LogDebug("Session {UserId} left {Channels}.", player.Id, string.Join(", ", left));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IPacket[]> JoinChannelAsync(IPlayer player, string channelName)
    {
        ArgumentNullException.ThrowIfNull(player);
        StableChannel? channel = await ResolveChannelAsync(player, channelName).ConfigureAwait(false);
        if (channel is null)
        {
            logger.LogDebug("Session {UserId} tried to join unknown channel {Channel}.", player.Id, channelName);
            return [];
        }

        if (string.Equals(channelName, "#spectator", StringComparison.Ordinal))
        {
            players.JoinSpectatorChannel(player);
            return [];
        }

        if (string.Equals(channelName, "#multiplayer", StringComparison.Ordinal))
        {
            return [new PacketChannelJoinSuccess { Name = channel.Name }, ChannelInfo(channel)];
        }

        if (!channel.Add(player))
        {
            logger.LogDebug("Session {UserId} is already a member of {Channel}.", player.Id, channel.Name);
            return [];
        }

        PacketChannelInfo info = ChannelInfo(channel);
        Broadcast(info, except: player);
        logger.LogDebug("Session {UserId} joined {Channel}.", player.Id, channel.Name);
        return [new PacketChannelJoinSuccess { Name = channel.Name }, info];
    }

    /// <inheritdoc />
    public async Task<IPacket[]> LeaveChannelAsync(IPlayer player, string channelName)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (string.Equals(channelName, "#spectator", StringComparison.Ordinal))
        {
            players.LeaveSpectatorChannel(player);
            return [];
        }

        if (string.Equals(channelName, "#multiplayer", StringComparison.Ordinal))
        {
            return [];
        }

        StableChannel? channel = await ResolveChannelAsync(player, channelName).ConfigureAwait(false);
        if (channel is null || !channel.Remove(player))
        {
            return [];
        }

        PacketChannelInfo info = ChannelInfo(channel);
        Broadcast(info, except: player);
        logger.LogDebug("Session {UserId} left {Channel}.", player.Id, channel.Name);
        return [new PacketChannelKick { Name = channel.Name }, info];
    }

    /// <inheritdoc />
    public async Task<IPacket[]> SendChannelMessageAsync(IPlayer player, string channelName, string text)
    {
        ArgumentNullException.ThrowIfNull(player);
        StableChannel? channel = await ResolveChannelAsync(player, channelName).ConfigureAwait(false);
        if (channel is null || !channel.Contains(player))
        {
            logger.LogDebug("Session {UserId} wrote to {Channel} without being a member.", player.Id, channelName);
            return [];
        }

        if (!channel.CanWrite(player))
        {
            logger.LogDebug("Session {UserId} wrote to {Channel} without write privileges.", player.Id, channel.Name);
            return [];
        }

        List<IPacket> response = [];
        string message = PrepareMessage(text, ResolveChatConfig(), response);
        if (message.Length == 0)
        {
            return [];
        }

        await StoreChannelMessageAsync(channel.ChannelId, player.Id, message).ConfigureAwait(false);

        PacketSendMessage packet = new()
        {
            Message = new Message
            {
                Sender = player.Name,
                Text = message,
                Recipient = channel.Name,
                SenderId = player.Id
            }
        };

        // The stable client echoes the sender's own message locally, so it is skipped here.
        IPlayer[] recipients = channel.GetMembers().Where(member => !ReferenceEquals(member, player)).ToArray();
        foreach (IPlayer recipient in recipients)
        {
            recipient.Enqueue(packet);
        }

        logger.LogInformation(
            "Session {UserId} posted to {Channel} for {RecipientCount} recipient(s).",
            player.Id,
            channel.Name,
            recipients.Length);
        return response.ToArray();
    }

    /// <inheritdoc />
    public async Task<IPacket[]> SendPrivateMessageAsync(IPlayer player, string recipientName, string text)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (string.IsNullOrWhiteSpace(recipientName))
        {
            return [];
        }

        IPlayer? target = players.GetAllPlayers()
            .FirstOrDefault(other => string.Equals(other.Name, recipientName, StringComparison.OrdinalIgnoreCase));
        if (target is null || target.Id == player.Id)
        {
            logger.LogDebug("Session {UserId} tried to message offline user {Recipient}.", player.Id, recipientName);
            return [new PacketNotification { Message = $"{recipientName} is not online." }];
        }

        List<IPacket> response = [];
        string message = PrepareMessage(text, ResolveChatConfig(), response);
        if (message.Length == 0)
        {
            return response.ToArray();
        }

        if (await PrivateMessagePolicy.CheckAsync(player, target, scopeFactory).ConfigureAwait(false) is { } rejection)
        {
            return [rejection];
        }

        await StorePrivateMessageAsync(player.Id, target.Id, message).ConfigureAwait(false);

        target.Enqueue(new PacketSendMessage
        {
            Message = new Message
            {
                Sender = player.Name,
                Text = message,
                Recipient = target.Name,
                SenderId = player.Id
            }
        });

        if (target.Activity.Action == UserAction.Afk && target.AwayMessage is { Length: > 0 } awayMessage)
        {
            response.Add(new PacketSendMessage
            {
                Message = new Message
                {
                    Sender = target.Name,
                    Text = awayMessage,
                    Recipient = player.Name,
                    SenderId = target.Id
                }
            });
        }

        logger.LogInformation("Session {UserId} sent a private message to {RecipientId}.", player.Id, target.Id);
        return response.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<StableChannel> GetChannels()
    {
        lock (_lock)
        {
            return _channels.ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<StableChannel> GetPlayerChannels(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        IEnumerable<StableChannel> channels = GetChannels().Where(channel => channel.Contains(player));
        if (players.GetSpectatorChannel(player) is { } spectatorChannel && spectatorChannel.Contains(player))
        {
            channels = channels.Append(spectatorChannel);
        }

        return _multiplayer.TryGetValue(player, out StableChannel? roomChannel)
            ? channels.Append(roomChannel).ToArray()
            : channels.ToArray();
    }

    /// <inheritdoc/>
    public async Task JoinMultiplayerChannelAsync(IPlayer player, StableChannel channel)
    {
        if (_multiplayer.TryGetValue(player, out StableChannel? current) && ReferenceEquals(current, channel))
        {
            return;
        }

        if (channel.ChannelId is { } channelId)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IChatRepository>()
                .AddUserChannelAsync(channelId, player.Id).ConfigureAwait(false);
        }

        _multiplayer[player] = channel;
        channel.Add(player);
        player.Enqueue(new PacketChannelJoinSuccess { Name = channel.Name });
        foreach (IPlayer member in channel.GetMembers())
        {
            member.Enqueue(ChannelInfo(channel));
        }
    }

    /// <inheritdoc/>
    public async Task LeaveMultiplayerChannelAsync(IPlayer player, StableChannel channel)
    {
        if (!_multiplayer.TryGetValue(player, out StableChannel? current) || !ReferenceEquals(current, channel))
        {
            return;
        }

        if (channel.ChannelId is { } channelId && !channel.GetMembers().Any(member => member.Id == player.Id && !ReferenceEquals(member, player)))
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IChatRepository>()
                .RemoveUserChannelAsync(channelId, player.Id, hide: false).ConfigureAwait(false);
        }

        _multiplayer.TryRemove(new KeyValuePair<IPlayer, StableChannel>(player, channel));
        channel.Remove(player);
        player.Enqueue(new PacketChannelKick { Name = channel.Name });
        foreach (IPlayer member in channel.GetMembers())
        {
            member.Enqueue(ChannelInfo(channel));
        }
    }

    private static StableChannel? FindChannel(IReadOnlyList<StableChannel> channels, string name)
    {
        string? normalized = NormalizeName(name);
        return normalized is null
            ? null
            : channels.FirstOrDefault(channel => string.Equals(channel.Name, normalized, StringComparison.Ordinal));
    }

    private static PacketChannelInfo ChannelInfo(StableChannel channel) => new()
    {
        Channel = new ChannelWire
        {
            Name = channel.Name,
            Topic = channel.Topic,
            PlayerCount = checked((ushort)Math.Min(channel.Count, ushort.MaxValue))
        }
    };

    /// <summary>
    /// Normalizes a stored channel name to the stable wire form.
    /// </summary>
    /// <param name="name">The raw channel name.</param>
    /// <returns>The wire name, or <see langword="null"/> when nothing usable is left.</returns>
    /// <remarks>
    /// <c>osu!</c> and <c>#osu</c> both become <c>#osu</c>, so the stable client's default chat-join
    /// list matches the channels the lazer endpoints create.
    /// </remarks>
    private static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string normalized = name.Trim().TrimStart('#').TrimEnd('!').Trim();
        return normalized.Length == 0 ? null : "#" + normalized;
    }

    /// <summary>
    /// Trims a message and enforces the configured length limit.
    /// </summary>
    /// <param name="text">The raw text typed by the player.</param>
    /// <param name="chat">The configured chat limits.</param>
    /// <param name="response">Receives the truncation notice when the text had to be shortened.</param>
    /// <returns>The text to store and deliver, or an empty string when nothing is left.</returns>
    private static string PrepareMessage(string? text, ChatConfig chat, List<IPacket> response)
    {
        string message = (text ?? string.Empty).Trim();
        if (message.Length == 0)
        {
            return string.Empty;
        }

        int limit = Math.Max(1, chat.MessageLengthLimit);
        if (message.Length <= limit)
        {
            return message;
        }

        response.Add(new PacketNotification { Message = TruncationNotice });
        return message[..limit];
    }

    /// <summary>
    /// Returns the channel list, loading the public channels of the shared chat storage the first
    /// time a session needs them.
    /// </summary>
    /// <returns>The channels in display order.</returns>
    /// <remarks>
    /// Loading happens during login, before a client can join anything, so replacing the list cannot
    /// drop memberships. An empty list is a valid result: a deployment without configured channels
    /// simply has no chat.
    /// </remarks>
    private async Task<IReadOnlyList<StableChannel>> EnsureChannelsAsync()
    {
        lock (_lock)
        {
            if (_loadedFromStorage)
            {
                return _channels.ToArray();
            }
        }

        StableChannel[] loaded = await LoadChannelsAsync().ConfigureAwait(false);
        lock (_lock)
        {
            if (_loadedFromStorage)
            {
                // Another session won the race; its list is the one now in use.
                return _channels.ToArray();
            }

            _channels = loaded;
            _loadedFromStorage = true;
            return _channels.ToArray();
        }
    }

    private async Task<StableChannel[]> LoadChannelsAsync()
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IChatRepository chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
            IReadOnlyList<ChatChannel> stored = await chatRepository.GetPublicChannelsAsync().ConfigureAwait(false);
            List<StableChannel> channels = [];
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (ChatChannel channel in stored)
            {
                if (NormalizeName(channel.Name) is not { } name || name is "#spectator" or "#multiplayer" || !names.Add(name))
                {
                    continue;
                }

                channels.Add(new StableChannel(
                    name,
                    string.IsNullOrWhiteSpace(channel.Description) ? name : channel.Description,
                    channel.Moderated ? StaffPrivileges : null,
                    channelId: channel.ChannelId));
            }

            logger.LogInformation("Loaded {ChannelCount} stable chat channel(s) from storage.", channels.Count);
            return channels.ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not load the stable chat channels; the channel list stays empty.");
            return [];
        }
    }

    private ChatConfig ResolveChatConfig()
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<ConfigManager>()?.TryGet<ChatConfig>() ?? new ChatConfig();
    }

    private async Task StoreChannelMessageAsync(int? channelId, int senderId, string message)
    {
        if (channelId is null)
        {
            logger.LogDebug("Channel without a stored row; the message of {UserId} is not persisted.", senderId);
            return;
        }

        await StoreMessageAsync(channelId.Value, senderId, message).ConfigureAwait(false);
    }

    private async Task StorePrivateMessageAsync(int senderId, int recipientId, string message)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IChatRepository chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
            ChatChannel channel = await chatRepository.GetOrCreatePmChannelAsync(senderId, recipientId).ConfigureAwait(false);
            await StoreMessageAsync(channel.ChannelId, senderId, message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not resolve the private channel between {SenderId} and {RecipientId}.", senderId, recipientId);
        }
    }

    private async Task StoreMessageAsync(int channelId, int senderId, string message)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IChatRepository chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
            await chatRepository.CreateMessageAsync(channelId, senderId, message, MessageType.Plain).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not persist a message of {UserId} in channel {ChannelId}.", senderId, channelId);
        }
    }

    private void Broadcast(IPacket packet, IPlayer? except = null)
    {
        foreach (IPlayer other in players.GetAllPlayers().Where(other => !ReferenceEquals(other, except)))
        {
            other.Enqueue(packet);
        }
    }

    private async Task<StableChannel?> ResolveChannelAsync(IPlayer player, string channelName)
    {
        return channelName switch
        {
            "#multiplayer" => _multiplayer.GetValueOrDefault(player),
            "#spectator" => players.GetSpectatorChannel(player),
            _ => FindChannel(await EnsureChannelsAsync().ConfigureAwait(false), channelName)
        };
    }
}