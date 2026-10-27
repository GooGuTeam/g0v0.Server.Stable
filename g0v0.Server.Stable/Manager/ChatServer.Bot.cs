// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Stable.Objects.Chats;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using g0v0.Server.Stable.Services;

namespace g0v0.Server.Stable.Manager;

public partial class ChatServer
{
    /// <inheritdoc/>
    public async Task SendBotMessageAsync(StableChannel channel, string text)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BanchoBot? bot = scope.ServiceProvider.GetService<BanchoBot>();
            string name = bot is null ? "BanchoBot" : await bot.GetNameAsync().ConfigureAwait(false);
            foreach (string chunk in BanchoBot.SplitReply(text, ResolveChatConfig().MessageLengthLimit))
            {
                await StoreChannelMessageAsync(channel.ChannelId, BanchoBot.UserId, chunk).ConfigureAwait(false);
                PacketSendMessage packet = BotPacket(name, channel.Name, chunk);
                foreach (IPlayer member in channel.GetMembers())
                {
                    try
                    {
                        member.Enqueue(packet);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception, "Could not deliver a bot message to session {UserId}.", member.Id);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not deliver a bot reply to channel {Channel}.", channel.Name);
        }
    }

    private static PacketSendMessage BotPacket(string name, string recipient, string text) => new()
    {
        Message = new Message { SenderId = BanchoBot.UserId, Sender = name, Recipient = recipient, Text = text }
    };

    private async Task<bool> IsBotRecipientAsync(string recipientName)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BanchoBot? bot = scope.ServiceProvider.GetService<BanchoBot>();
            return bot is not null && string.Equals(recipientName, await bot.GetNameAsync().ConfigureAwait(false), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not resolve the BanchoBot account.");
            return string.Equals(recipientName, "BanchoBot", StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task HandleBotCommandAsync(IPlayer player, StableChannel? channel, string message, List<IPacket> response)
    {
        if (!message.StartsWith('!'))
        {
            return;
        }

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BanchoBot? bot = scope.ServiceProvider.GetService<BanchoBot>();
            string? reply = bot is null ? null : await bot.ProcessAsync(player, channel, message).ConfigureAwait(false);
            if (string.IsNullOrEmpty(reply))
            {
                return;
            }

            if (channel is not null && channel.Contains(player) && channel.Name is "#multiplayer" or "#spectator")
            {
                await SendBotMessageAsync(channel, reply).ConfigureAwait(false);
                return;
            }

            string name = await bot!.GetNameAsync().ConfigureAwait(false);
            foreach (string chunk in BanchoBot.SplitReply(reply, ResolveChatConfig().MessageLengthLimit))
            {
                await StorePrivateMessageAsync(BanchoBot.UserId, player.Id, chunk).ConfigureAwait(false);
                response.Add(BotPacket(name, player.Name, chunk));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not handle a bot command from {UserId}.", player.Id);
        }
    }
}