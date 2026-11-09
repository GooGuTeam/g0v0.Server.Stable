// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Objects.Rooms;
using g0v0.Server.Stable.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace g0v0.Server.Stable.Tests.Routes;

public class HealthRouteHandlerTests
{
    [Test]
    public async Task Health_ReportsStableIdentifyAndLiveCounters()
    {
        PlayerManager players = new();
        players.AddPlayer(new Player(1, "tourney-a", isTourneyClient: true));
        players.AddPlayer(new Player(1, "tourney-b", isTourneyClient: true));
        players.AddPlayer(new Player(1, "ordinary"));

        Mock<IRoomManager> rooms = new();
        rooms.Setup(manager => manager.GetAllRooms()).Returns([Mock.Of<IRoom>(), Mock.Of<IRoom>()]);

        Mock<IScoreRepository> scores = new();
        scores.Setup(repository => repository.CountUnprocessedScoresAsync("stable")).ReturnsAsync(7);

        HealthRouteHandler handler = new(players, rooms.Object, scores.Object);
        using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        DefaultHttpContext context = new() { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await (await handler.HandleAsync(context)).ExecuteAsync(context);

        using JsonDocument payload = JsonDocument.Parse(
            Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        JsonElement root = payload.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("identify").GetString(), Is.EqualTo("stable"));
            Assert.That(root.GetProperty("players_online").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("active_sessions").GetInt32(), Is.EqualTo(3));
            Assert.That(root.GetProperty("tournament_sessions").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("multiplayer_rooms").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("pending_scores").GetInt32(), Is.EqualTo(7));
        });
        scores.Verify(repository => repository.CountUnprocessedScoresAsync("stable"), Times.Once);
    }
}