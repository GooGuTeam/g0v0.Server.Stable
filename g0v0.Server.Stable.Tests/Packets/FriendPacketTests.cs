// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace g0v0.Server.Stable.Tests.Packets;

public class FriendPacketTests
{
    private Mock<IUserRepository> _users = null!;
    private Mock<IRelationshipRepository> _relationships = null!;
    private ServiceProvider _services = null!;
    private PacketFacade _facade = null!;
    private Player _player = null!;

    [SetUp]
    public void SetUp()
    {
        _users = new Mock<IUserRepository>();
        _users.Setup(repository => repository.GetByIdAsync(2)).ReturnsAsync(new User { Id = 2, Username = "bob" });
        _relationships = new Mock<IRelationshipRepository>();
        _services = new ServiceCollection().AddSingleton(_users.Object).AddSingleton(_relationships.Object)
            .BuildServiceProvider();
        _facade = new PacketFacade { ScopeFactory = _services.GetRequiredService<IServiceScopeFactory>() };
        _player = new Player(1);
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    [Test]
    public async Task AddingAnOfflineUserCreatesAnOutgoingFollowOnly()
    {
        await HandleAsync(new PacketFriendAdd { UserId = 2 });

        _relationships.Verify(
            repository => repository.CreateAsync(It.Is<Relationship>(relationship =>
                relationship.UserId == 1 && relationship.TargetId == 2 && relationship.Type == RelationshipType.Follow)),
            Times.Once);
        _relationships.Verify(repository => repository.CreateAsync(It.IsAny<Relationship>()), Times.Once);
        _relationships.Verify(repository => repository.UpdateAsync(It.IsAny<Relationship>()), Times.Never);
    }

    [Test]
    public async Task AddingAnExistingFriendDoesNotDuplicateTheRelationship()
    {
        _relationships.Setup(repository => repository.GetRelationshipAsync(1, 2))
            .ReturnsAsync(new Relationship { UserId = 1, TargetId = 2, Type = RelationshipType.Follow });

        await HandleAsync(new PacketFriendAdd { UserId = 2 });

        _relationships.Verify(repository => repository.CreateAsync(It.IsAny<Relationship>()), Times.Never);
        _relationships.Verify(repository => repository.UpdateAsync(It.IsAny<Relationship>()), Times.Never);
    }

    [Test]
    public async Task AddingABlockedUserReplacesTheBlockWithoutMutatingItsTrackedPrimaryKey()
    {
        Relationship block = new() { UserId = 1, TargetId = 2, Type = RelationshipType.Block };
        _relationships.Setup(repository => repository.GetRelationshipAsync(1, 2)).ReturnsAsync(block);

        await HandleAsync(new PacketFriendAdd { UserId = 2 });

        Assert.That(block.Type, Is.EqualTo(RelationshipType.Block));
        _relationships.Verify(
            repository => repository.UpdateAsync(It.Is<Relationship>(relationship =>
                !ReferenceEquals(relationship, block) && relationship.UserId == 1 && relationship.TargetId == 2
                && relationship.Type == RelationshipType.Follow)),
            Times.Once);
        _relationships.Verify(repository => repository.CreateAsync(It.IsAny<Relationship>()), Times.Never);
    }

    [TestCase(1)]
    [TestCase(999)]
    public async Task CannotAddSelfOrANonexistentUser(int userId)
    {
        await HandleAsync(new PacketFriendAdd { UserId = userId });

        _relationships.Verify(repository => repository.CreateAsync(It.IsAny<Relationship>()), Times.Never);
        _relationships.Verify(repository => repository.UpdateAsync(It.IsAny<Relationship>()), Times.Never);
    }

    [Test]
    public async Task RemovingAFriendDeletesOnlyTheOutgoingFollow()
    {
        Relationship follow = new() { UserId = 1, TargetId = 2, Type = RelationshipType.Follow };
        _relationships.Setup(repository => repository.GetRelationshipAsync(1, 2)).ReturnsAsync(follow);

        await HandleAsync(new PacketFriendRemove { UserId = 2 });

        _relationships.Verify(repository => repository.DeleteAsync(follow), Times.Once);
        _relationships.Verify(repository => repository.DeleteAsync(It.IsAny<Relationship>()), Times.Once);
    }

    [TestCase(null)]
    [TestCase(RelationshipType.Block)]
    public async Task RemovingANonFriendPreservesExistingBlocks(RelationshipType? type)
    {
        _relationships.Setup(repository => repository.GetRelationshipAsync(1, 2))
            .ReturnsAsync(type.HasValue ? new Relationship { UserId = 1, TargetId = 2, Type = type.Value } : null);

        await HandleAsync(new PacketFriendRemove { UserId = 2 });

        _relationships.Verify(repository => repository.DeleteAsync(It.IsAny<Relationship>()), Times.Never);
    }

    private async Task HandleAsync(IClientPacket packet)
    {
        using MemoryStream bytes = new(PacketConvert.Serialize(packet));
        IClientPacket decoded = (IClientPacket)PacketConvert.DeserializeAll(bytes, _facade).Single();
        Assert.That(await decoded.Handle(_player, _facade), Is.Empty);
    }
}