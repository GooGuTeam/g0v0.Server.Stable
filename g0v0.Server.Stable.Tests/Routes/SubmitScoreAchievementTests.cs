// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using g0v0.Server.Common.Achievements;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Stable.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace g0v0.Server.Stable.Tests.Routes;

public partial class SubmitScoreRouteHandlerTests
{
    [Test]
    public async Task AchievementsAreEvaluatedAfterProcessingAndIncludedInTheOverallChart()
    {
        _processor.Setup(service => service.ProcessAsync(It.IsAny<Score>(), It.IsAny<CancellationToken>()))
            .Callback<Score, CancellationToken>((score, _) => score.Processed = true).Returns(Task.CompletedTask);
        _achievements.Setup(service => service.UnlockAsync(It.Is<Score>(score => score.Processed && score.Id == 42), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Achievement(1, "Rising Star", "First steps.", "osu-skill-pass-1"), new Achievement(21, "500 Combo", "Keep going.", "osu-combo-500")]);
        DefaultHttpContext context = await Context();
        await (await _handler.HandleAsync(context)).ExecuteAsync(context);
        string body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.That(body, Does.Contain("achievements-new:osu-skill-pass-1+Rising Star+First steps./osu-combo-500+500 Combo+Keep going."));
        _achievements.Verify(service => service.UnlockAsync(It.IsAny<Score>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CalculationFailuresDoNotUnlockMedals()
    {
        _processor.Setup(service => service.ProcessAsync(It.IsAny<Score>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("calculation failed"));
        await _handler.HandleAsync(await Context());
        _achievements.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AchievementFailuresDoNotDiscardAnAlreadyProcessedScore()
    {
        _processor.Setup(service => service.ProcessAsync(It.IsAny<Score>(), It.IsAny<CancellationToken>()))
            .Callback<Score, CancellationToken>((score, _) => score.Processed = true).Returns(Task.CompletedTask);
        _achievements.Setup(service => service.UnlockAsync(It.IsAny<Score>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("medal storage unavailable"));
        DefaultHttpContext context = await Context();
        await (await _handler.HandleAsync(context)).ExecuteAsync(context);
        string body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.That(body, Does.Contain("onlineScoreId:42"));
        Assert.That(body, Does.EndWith("achievements-new:"));
        Assert.That(_saved!.Processed, Is.True);
    }

    [Test]
    public void AchievementFieldsCannotInjectChartOrMedalSeparators()
    {
        string text = StableWebFormat.FormatAchievements([new Achievement(1, "A+B/C|D\n", "E/F+G", "icon")]);
        Assert.That(text, Is.EqualTo("icon+A B CID +E F G"));
        Assert.That(StableWebFormat.FormatAchievements([]), Is.Empty);
    }
}