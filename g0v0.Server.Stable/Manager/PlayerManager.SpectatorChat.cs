// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;

namespace g0v0.Server.Stable.Manager;

public partial class PlayerManager
{
    private readonly Dictionary<IPlayer, StableChannel> _spectatorChannels = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IPlayer, StableChannel> _spectatorChatMembers = new(ReferenceEqualityComparer.Instance);

    /// <inheritdoc/>
    public StableChannel? GetSpectatorChannel(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            return SpectatorChannel(player);
        }
    }

    /// <inheritdoc/>
    public void JoinSpectatorChannel(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            UpdateSpectatorChannel(player);
        }
    }

    /// <inheritdoc/>
    public void LeaveSpectatorChannel(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_lock)
        {
            RemoveSpectatorChannelMember(player);
        }
    }

    private static void UpdateSpectatorChannelInfo(StableChannel channel)
    {
        PacketChannelInfo info = new()
        {
            Channel = new ChannelWire
            {
                Name = channel.Name,
                Topic = channel.Topic,
                PlayerCount = checked((ushort)Math.Min(channel.Count, ushort.MaxValue))
            }
        };
        foreach (IPlayer member in channel.GetMembers())
        {
            member.Enqueue(info);
        }
    }

    // The player registry lock protects the channel lifecycle together with spectator relationships.
    private StableChannel? SpectatorChannel(IPlayer player)
    {
        if (!IsRegistered(player) || player.IsRestricted)
        {
            return null;
        }

        IPlayer? host = _spectating.GetValueOrDefault(player) ?? (_spectators.ContainsKey(player) ? player : null);
        if (host is null || host.IsRestricted || !IsRegistered(host))
        {
            return null;
        }

        if (_spectatorChannels.TryGetValue(host, out StableChannel? channel))
        {
            return channel;
        }

        channel = new StableChannel("#spectator", $"Spectating {host.Name}", autoJoin: false);
        _spectatorChannels.Add(host, channel);
        return channel;
    }

    private void UpdateSpectatorChannel(IPlayer player)
    {
        StableChannel? channel = SpectatorChannel(player);
        if (_spectatorChatMembers.TryGetValue(player, out StableChannel? current) && ReferenceEquals(current, channel))
        {
            return;
        }

        RemoveSpectatorChannelMember(player);
        if (channel is null)
        {
            return;
        }

        _spectatorChatMembers[player] = channel;
        channel.Add(player);
        player.Enqueue(new PacketChannelJoinSuccess { Name = channel.Name });
        UpdateSpectatorChannelInfo(channel);
    }

    private void RemoveSpectatorChannelMember(IPlayer player)
    {
        if (!_spectatorChatMembers.Remove(player, out StableChannel? channel))
        {
            return;
        }

        channel.Remove(player);
        player.Enqueue(new PacketChannelKick { Name = channel.Name });
        UpdateSpectatorChannelInfo(channel);
    }
}