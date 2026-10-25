// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Common.Scoring;
using g0v0.Server.Common.Storage;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Objects.Players;
using g0v0.Server.Stable.Services;
using Org.BouncyCastle.Crypto;

namespace g0v0.Server.Stable.Routes;

/// <summary>
/// Serves <c>POST /web/osu-submit-modular-selector.php</c>: the modular score submission a stable
/// client sends once a play has ended.
/// </summary>
/// <remarks>
/// <para>
/// Request: <c>multipart/form-data</c> with an <c>osu-token</c> header. The encrypted fields are
/// Rijndael-CBC ciphertext (256-bit block) whose key and IV are derived from <c>osuver</c>; see
/// <see cref="StableScoreSubmission"/>.
/// </para>
/// <code>
/// x      = &lt;int&gt;             fixed 0
/// ft     = &lt;int&gt;             milliseconds spent in the failed attempt
/// fs     = &lt;int&gt;             exit count of the failed attempt
/// st     = &lt;int&gt;             milliseconds spent in the successful attempt
/// bmk    = &lt;beatmap md5&gt;     checksum of the played .osu
/// sbk    = &lt;storyboard md5&gt;  checksum of the storyboard bundle
/// iv     = &lt;base64&gt;          initialization vector of both encrypted fields
/// c1     = &lt;string&gt;          client-specific blob
/// s      = &lt;base64&gt;          encrypted client hash
/// score  = &lt;base64&gt;          encrypted submission, colon separated (see below)
/// osuver = &lt;string&gt;          eight-digit client version
/// pass   = &lt;password md5&gt;    account password, hashed once
/// </code>
/// <para>
/// The decrypted <c>score</c> payload is the colon-separated field list documented on
/// <see cref="StableScoreSubmission"/>; the upload also carries a file part named <c>score</c> with
/// the replay frames.
/// </para>
/// <para>
/// Response (<c>text/plain</c>): either the literal <c>error: no</c> / <c>error: pass</c> rejection,
/// or the chart block the results screen renders, fields separated by <c>|</c> and charts by
/// <c>\n</c>:
/// </para>
/// <code>
/// beatmapId:&lt;id&gt;|beatmapSetId:&lt;id&gt;|beatmapPlaycount:&lt;n&gt;|beatmapPasscount:&lt;n&gt;|approvedDate:&lt;yyyy-MM-dd HH:mm:ss&gt;|
///
/// |chartId:beatmap|chartUrl:&lt;url&gt;|chartName:Beatmap Ranking|
///   rankBefore:&lt;n&gt;|rankAfter:&lt;n&gt;|rankedScoreBefore:..|rankedScoreAfter:..|
///   totalScoreBefore:..|totalScoreAfter:..|maxComboBefore:..|maxComboAfter:..|
///   accuracyBefore:..|accuracyAfter:..|ppBefore:..|ppAfter:..|onlineScoreId:&lt;id&gt;|
///
/// |chartId:overall|chartUrl:&lt;url&gt;|chartName:Overall Ranking|
///   rankBefore:..|rankAfter:..|rankedScoreBefore:..|rankedScoreAfter:..|
///   totalScoreBefore:..|totalScoreAfter:..|maxComboBefore:..|maxComboAfter:..|
///   accuracyBefore:..|accuracyAfter:..|ppBefore:..|ppAfter:..|achievements-new:
/// </code>
/// <para>
/// <c>rank</c> is the beatmap leaderboard position as an integer, not the grade letter: the client
/// stores it in an integer field and shows nothing when it cannot parse. <c>error: pass</c> makes the
/// client tell the player that the password is wrong, so it is only used for authentication failures.
/// </para>
/// </remarks>
/// <param name="authenticator">Authenticates the submitted username and password MD5.</param>
/// <param name="scores">The score repository.</param>
/// <param name="beatmaps">The beatmap repository.</param>
/// <param name="fetcher">Fetches missing beatmap metadata.</param>
/// <param name="players">The online player registry.</param>
/// <param name="rulesets">The installed rulesets.</param>
/// <param name="storage">The replay storage service.</param>
/// <param name="queue">The background processing queue.</param>
/// <param name="submissions">The score-side duplicate submission tracker.</param>
/// <param name="statistics">Reads the personal statistics shown in the response charts.</param>
/// <param name="processor">Calculates the score before responding, so the charts carry final values.</param>
/// <param name="configs">The shared configuration manager.</param>
/// <param name="logger">The logger.</param>
public sealed class SubmitScoreRouteHandler(
    StableWebAuthenticator authenticator,
    IScoreRepository scores,
    IBeatmapRepository beatmaps,
    IFetcher fetcher,
    IPlayerManager players,
    RulesetManager rulesets,
    IStorageService storage,
    ScoreProcessingQueue queue,
    StableScoreSubmissionTracker submissions,
    IUserStatisticsRepository statistics,
    IScoreProcessingService processor,
    ConfigManager configs,
    ILogger<SubmitScoreRouteHandler> logger) : IRouteHandler
{
    private readonly GeneralConfig _general = configs.Get<GeneralConfig>();
    private readonly GameConfig _game = configs.Get<GameConfig>();

    /// <inheritdoc/>
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
        {
            logger.LogWarning("Rejected a stable score submission: the request is not a form upload.");
            return Results.Text("error: no");
        }

        IFormCollection form;
        StableScoreSubmission submission;
        string clientHash;
        string version;
        int duration;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
            version = form["osuver"].ToString();
            if (form["score"].Count != 1 || form.Files.Count(f => string.Equals(f.Name, "score", StringComparison.Ordinal)) > 1)
            {
                logger.LogWarning(
                    "Rejected a stable score submission: {ScoreParts} score parts and {ReplayParts} replay parts were sent.",
                    form["score"].Count,
                    form.Files.Count(f => string.Equals(f.Name, "score", StringComparison.Ordinal)));
                return Results.Text("error: no");
            }

            submission = StableScoreSubmission.Parse(StableScoreSubmission.Decrypt(form["score"].ToString(), form["iv"].ToString(), version));
            clientHash = StableScoreSubmission.Decrypt(form["s"].ToString(), form["iv"].ToString(), version);
            duration = int.Parse(form[submission.Passed ? "st" : "ft"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
            if (duration is < 0 or > 86400000)
            {
                logger.LogWarning("Rejected a stable score submission: the play duration {Duration} ms is out of range.", duration);
                return Results.Text("error: no");
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCipherTextException or DataLengthException or InvalidDataException or System.Text.DecoderFallbackException)
        {
            // The decrypted payload, the initialization vector or the version mismatch; log the
            // message because a silent "error: no" is impossible to diagnose from the client.
            logger.LogWarning(exception, "Rejected a stable score submission: the payload could not be decoded.");
            return Results.Text("error: no");
        }

        // The submission names the account and carries the password MD5, which is exactly the
        // credential pair the other stable web endpoints authenticate with.
        User? user = await authenticator.AuthenticateAsync(submission.Username, form["pass"].ToString(), context.RequestAborted);
        IPlayer? player = user == null ? null : players.GetPlayer(user.Id);
        if (user == null || player == null || player.IsRestricted || player.IsTourneyClient)
        {
            logger.LogWarning(
                "Rejected a score submission for {Username}: user={HasUser}, online={IsOnline}, restricted={IsRestricted}, tourney={IsTourney}.",
                submission.Username,
                user != null,
                player != null,
                player?.IsRestricted ?? false,
                player?.IsTourneyClient ?? false);
            return Results.Text("error: pass");
        }

        if (!submission.VerifyChecksum(user.Username, version, clientHash, form["sbk"].ToString())
            || !string.Equals(submission.BeatmapChecksum, form["bmk"].ToString(), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Rejected a score submission for {Username}: checksum={Checksum}, beatmap={BeatmapMatches}.",
                user.Username,
                submission.VerifyChecksum(user.Username, version, clientHash, form["sbk"].ToString()),
                string.Equals(submission.BeatmapChecksum, form["bmk"].ToString(), StringComparison.OrdinalIgnoreCase));
            return Results.Text("error: no");
        }

        IFormFile? replay = form.Files.GetFile("score");
        if ((submission.Passed && (replay == null || replay.Length < 13)) || replay?.Length > 8 * 1024 * 1024)
        {
            logger.LogWarning(
                "Rejected a score submission for {Username}: the replay is {Length} bytes.",
                user.Username,
                replay?.Length);
            return Results.Text("error: no");
        }

        using StableScoreSubmissionTracker.Lease lease = await submissions.AcquireAsync(user.Id, submission.Checksum, context.RequestAborted);
        Beatmap beatmap = await beatmaps.GetOrFetchByChecksumAsync(submission.BeatmapChecksum, fetcher, context.RequestAborted);
        Score draft = submission.ToScore(rulesets, user.Id, beatmap.Id, version, DateTimeOffset.UtcNow, duration);
        draft.Mode = ScoreModeUtils.GetScoreMode(draft.Mode, draft.Mods, _game);

        // The charts compare the state before and after the play, so both are captured as
        // immutable snapshots: processing updates the tracked statistics instance in place.
        Score? previousBest = await scores.GetBestBeatmapScoreAsync(beatmap.Id, draft.Mode, user.Id, cancellationToken: context.RequestAborted);
        StatisticsSnapshot before = await SnapshotAsync(user.Id, draft.Mode);

        // The beatmap chart reports the leaderboard position of the play, not its grade letter: the
        // client stores the value in an integer field, so a letter is parsed as zero and the results
        // screen shows no rank at all. The previous position has to be read before processing writes
        // the new leaderboard row.
        int? previousBeatmapRank = previousBest is null
            ? null
            : RankOrNull(await scores.GetBeatmapLeaderboardRankAsync(beatmap.Id, draft.Mode, user.Id, legacyOnly: true, context.RequestAborted));

        Score? score = lease.ScoreId is null ? null : await scores.GetByIdAsync(lease.ScoreId.Value);
        if (score == null)
        {
            score = draft;
            await scores.CreateAsync(score);
            lease.ScoreId = score.Id;
        }

        if (!score.HasReplay && replay is { Length: > 0 })
        {
            using MemoryStream frames = new();
            await replay.CopyToAsync(frames, context.RequestAborted);
            byte[] content = submission.CreateReplay(score, user.Username, version, frames.ToArray());
            await storage.WriteFileAsync(score.ReplayFilename, content, "application/x-osu-replay", cancellationToken: context.RequestAborted);
            await scores.MarkScoreHasReplay(score.Id);
        }

        if (!score.Passed)
        {
            // Failed plays still join the queue for the play-count statistics.
            queue.NotifyPendingScore();
            return Results.Text("error: no");
        }

        // Calculate now instead of only queueing: the client renders the results screen from this
        // response, so pp, rank and accuracy have to be final before it is written. On failure the
        // score stays queued and the screen falls back to the values that are already known.
        try
        {
            await processor.ProcessAsync(score, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Stable score {ScoreId} could not be calculated synchronously; leaving it queued.", score.Id);
            queue.NotifyPendingScore();
        }

        StatisticsSnapshot after = await SnapshotAsync(user.Id, score.Mode);
        int? beatmapRank = RankOrNull(await scores.GetBeatmapLeaderboardRankAsync(
            beatmap.Id,
            score.Mode,
            user.Id,
            legacyOnly: true,
            context.RequestAborted));
        IReadOnlyList<Score> beatmapScores = await scores.GetByBeatmapIdAsync(beatmap.Id);
        logger.LogInformation(
            "Accepted the stable score {ScoreId} of user {UserId} on beatmap {BeatmapId}: {Pp:0.##}pp, rank {Rank}, beatmap rank {BeatmapRank}.",
            score.Id,
            user.Id,
            beatmap.Id,
            score.Pp,
            score.Rank,
            beatmapRank ?? 0);
        return Results.Text(BuildResponse(score, beatmap, beatmapScores, previousBest, previousBeatmapRank, beatmapRank, before, after));
    }

    private static int? RankOrNull(int rank) => rank > 0 ? rank : null;

    private async Task<StatisticsSnapshot> SnapshotAsync(int userId, int mode)
    {
        UserStatistics? statisticsRow = await statistics.GetByUserIdAndModeAsync(userId, mode);
        int? rank = await statistics.GetGlobalRankAsync(userId, mode);
        return statisticsRow is null
            ? new StatisticsSnapshot(rank, 0, 0, 0, 0, 0)
            : new StatisticsSnapshot(
                rank,
                statisticsRow.RankedScore,
                statisticsRow.TotalScore,
                statisticsRow.MaximumCombo,
                statisticsRow.HitAccuracy,
                statisticsRow.Pp);
    }

    private string BuildResponse(
        Score score,
        Beatmap beatmap,
        IReadOnlyList<Score> beatmapScores,
        Score? previousBest,
        int? previousBeatmapRank,
        int? beatmapRank,
        StatisticsSnapshot before,
        StatisticsSnapshot after)
    {
        long previousScore = previousBest?.ClassicTotalScore ?? 0;
        int previousCombo = previousBest?.MaxCombo ?? 0;
        double previousAccuracy = previousBest is null ? 0 : Math.Round(previousBest.Accuracy * 100, 2, MidpointRounding.AwayFromZero);
        double previousPp = previousBest?.Pp ?? 0;
        string approvedDate = beatmap.LastUpdate?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;

        List<string> charts =
        [

            // beatmap header
            string.Create(CultureInfo.InvariantCulture, $"beatmapId:{beatmap.Id}"),
            string.Create(CultureInfo.InvariantCulture, $"beatmapSetId:{beatmap.BeatmapSetId}"),
            string.Create(CultureInfo.InvariantCulture, $"beatmapPlaycount:{beatmapScores.Count}"),
            string.Create(CultureInfo.InvariantCulture, $"beatmapPasscount:{beatmapScores.Count(other => other.Passed)}"),
            $"approvedDate:{approvedDate}",
            "\n",

            // beatmap ranking chart
            "chartId:beatmap",
            $"chartUrl:{_general.WebUrl}beatmapsets/{beatmap.BeatmapSetId}#{score.Mode}/{beatmap.Id}",
            "chartName:Beatmap Ranking",
            StableWebFormat.ChartEntry("rank", previousBeatmapRank, beatmapRank),
            StableWebFormat.ChartEntry("rankedScore", previousScore, score.ClassicTotalScore),
            StableWebFormat.ChartEntry("totalScore", previousScore, score.ClassicTotalScore),
            StableWebFormat.ChartEntry("maxCombo", previousCombo, score.MaxCombo),
            StableWebFormat.ChartEntry("accuracy", previousAccuracy, Math.Round(score.Accuracy * 100, 2, MidpointRounding.AwayFromZero)),
            StableWebFormat.ChartEntry("pp", previousPp, score.Pp),
            string.Create(CultureInfo.InvariantCulture, $"onlineScoreId:{score.Id}"),
            "\n",

            // overall ranking chart
            "chartId:overall",
            $"chartUrl:{_general.WebUrl}users/{score.UserId}",
            "chartName:Overall Ranking",
            StableWebFormat.ChartEntry("rank", before.Rank, after.Rank),
            StableWebFormat.ChartEntry("rankedScore", before.RankedScore, after.RankedScore),
            StableWebFormat.ChartEntry("totalScore", before.TotalScore, after.TotalScore),
            StableWebFormat.ChartEntry("maxCombo", before.MaximumCombo, after.MaximumCombo),
            StableWebFormat.ChartEntry("accuracy", before.Accuracy, after.Accuracy),
            StableWebFormat.ChartEntry("pp", before.Pp, after.Pp),
            "achievements-new:"
        ];

        return string.Join('|', charts);
    }

    /// <summary>
    /// An immutable copy of the personal statistics chart values.
    /// </summary>
    /// <param name="Rank">The global rank, or <see langword="null"/> when unranked.</param>
    /// <param name="RankedScore">The ranked score.</param>
    /// <param name="TotalScore">The total score.</param>
    /// <param name="MaximumCombo">The best combo.</param>
    /// <param name="Accuracy">The weighted accuracy in percent.</param>
    /// <param name="Pp">The weighted performance points.</param>
    private sealed record StatisticsSnapshot(int? Rank, long RankedScore, long TotalScore, int MaximumCombo, double Accuracy, double Pp);
}