// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using Moq;
using osu.Game.Online.Chat;

namespace g0v0.Server.Stable.Tests.Routes;

/// <summary>
/// Provides the chat repository the stable channel directory is loaded from.
/// </summary>
internal static class TestChatRepository
{
    /// <summary>
    /// Creates a repository whose public channels are <c>#osu</c> and <c>#announce</c>.
    /// </summary>
    /// <returns>The configured mock.</returns>
    public static Mock<IChatRepository> Create()
    {
        Mock<IChatRepository> repository = new();
        repository.Setup(repo => repo.GetPublicChannelsAsync())
            .ReturnsAsync([
                new ChatChannel { ChannelId = 1, Name = "osu!", Description = "General discussion.", Type = ChannelType.Public },
                new ChatChannel { ChannelId = 2, Name = "announce", Description = "News.", Type = ChannelType.Public, Moderated = true }
            ]);
        repository.Setup(repo => repo.CreateMessageAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<MessageType>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new ChatMessage());
        return repository;
    }
}