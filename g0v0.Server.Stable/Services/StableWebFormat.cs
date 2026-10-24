// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Rulesets;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using Score = g0v0.Server.Common.Database.Models.Score;

namespace g0v0.Server.Stable.Services;

/// <summary>
/// Formats the pipe-delimited text responses of the stable web endpoints.
/// </summary>
/// <remarks>
/// The stable client parses these responses positionally and without error handling, so a single
/// malformed field makes it discard the whole line. Every helper here therefore has to produce the
/// exact literal the client expects.
/// </remarks>
public static class StableWebFormat
{
    /// <summary>
    /// Converts the stored beatmap status to the numeric status the stable client expects.
    /// </summary>
    /// <param name="status">The stored online status.</param>
    /// <returns>The client-facing status value.</returns>
    /// <remarks>
    /// <para>
    /// The two enums are the same list shifted by one: the server reserves <c>0</c> for
    /// <see cref="BeatmapOnlineStatus.Pending"/> and counts downwards for the unsubmitted states,
    /// while the client counts upwards starting at <c>1</c> for pending. The conversion is therefore
    /// the enum value plus one.
    /// </para>
    /// <para>
    /// <see cref="BeatmapOnlineStatus.Graveyard"/>, <see cref="BeatmapOnlineStatus.WIP"/> and
    /// <see cref="BeatmapOnlineStatus.None"/> become negative that way, and the client reads a
    /// negative status as "beatmap not found"; they are clamped to <c>0</c> ("not submitted")
    /// instead, which is the answer it can actually act on.
    /// </para>
    /// <para>
    /// The client only enables score submission for maps it knows to be ranked, and it learns that
    /// status from these web responses, so this mapping decides whether a play can submit at all.
    /// </para>
    /// </remarks>
    public static int ToRankStatus(BeatmapOnlineStatus status) => Math.Max(0, (int)status + 1);

    /// <summary>
    /// Formats the per-mode grade letters of <c>osu-getbeatmapinfo.php</c>.
    /// </summary>
    /// <param name="rank">The best rank achieved, or <see langword="null"/> when unplayed.</param>
    /// <returns>The stable grade letter, or <c>N</c> for an unplayed difficulty.</returns>
    /// <remarks>
    /// <see cref="ScoreRank"/> is named after the letters the client displays, so the enum name is
    /// the wire value; only the unplayed case needs a separate letter because the client reads the
    /// absence of a grade as "not played", not as <c>F</c>.
    /// </remarks>
    public static string ToGradeLetter(ScoreRank? rank) => rank?.ToString() ?? "N";

    /// <summary>
    /// Converts stored API mods back to the stable legacy mod bitmask.
    /// </summary>
    /// <param name="rulesets">The installed rulesets.</param>
    /// <param name="rulesetId">The stored (possibly RX/AP) ruleset ID.</param>
    /// <param name="mods">The stored mods.</param>
    /// <returns>The legacy mod flags.</returns>
    public static LegacyMods ToLegacyMods(RulesetManager rulesets, int rulesetId, IEnumerable<osu.Game.Online.API.APIMod> mods)
    {
        try
        {
            Ruleset ruleset = rulesets.GetRuleset(Common.Utils.ScoreModeUtils.GetBaseRulesetId(rulesetId));
            return ruleset.ConvertToLegacyMods(mods.Select(mod => mod.ToMod(ruleset)).ToArray());
        }
        catch (ArgumentException)
        {
            return LegacyMods.None;
        }
    }

    /// <summary>
    /// Formats one leaderboard row of <c>osu-osz2-getscores.php</c>.
    /// </summary>
    /// <param name="score">The stored score.</param>
    /// <param name="name">The display name of the score owner.</param>
    /// <param name="rank">The one-based leaderboard position, or 0 when it is unknown.</param>
    /// <param name="mods">The legacy mod flags of the score.</param>
    /// <returns>The pipe-delimited row.</returns>
    /// <remarks>
    /// The grammar is fixed by the client's <c>Score</c> constructor, which splits on <c>|</c> and
    /// then parses position after position, in this order:
    /// <c>id</c> (int64), <c>name</c>, <c>score</c> (int32), <c>max_combo</c>, <c>50</c>, <c>100</c>,
    /// <c>300</c>, <c>miss</c>, <c>katu</c>, <c>geki</c> (uint16), <c>perfect</c> (the literal
    /// <c>True</c>/<c>False</c>), <c>mods</c> (int32), <c>user_id</c> (int32), <c>rank</c> (int32,
    /// skipped when empty), <c>time</c> (Unix seconds), <c>has_replay</c> (the literal
    /// <c>True</c>/<c>False</c>).
    /// Every numeric position is parsed without a try/catch, so one malformed value makes the
    /// client drop the whole row - which is exactly what "no scores" looks like in game.
    /// </remarks>
    public static string FormatScoreRow(Score score, string name, int rank, LegacyMods mods)
    {
        ArgumentNullException.ThrowIfNull(score);
        int count300 = Statistic(score, HitResult.Great);
        int count100 = Statistic(score, HitResult.Ok);
        int count50 = Statistic(score, HitResult.Meh);
        int countMiss = Statistic(score, HitResult.Miss);
        int countGeki = Statistic(score, HitResult.Perfect);
        int countKatu = Statistic(score, HitResult.Good);
        bool perfect = countMiss == 0 && count50 == 0 && count100 == 0;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{score.Id}|{name}|{score.ClassicTotalScore}|{score.MaxCombo}|{count50}|{count100}|{count300}|{countMiss}|{countKatu}|{countGeki}|{FormatBoolean(perfect)}|{(int)mods}|{score.UserId}|{rank}|{score.EndedAt.ToUnixTimeSeconds()}|{FormatBoolean(score.HasReplay)}");
    }

    /// <summary>
    /// Formats a boolean for the legacy text protocols.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns><c>True</c> or <c>False</c>, never <c>1</c>/<c>0</c>.</returns>
    public static string FormatBoolean(bool value) => value ? "True" : "False";

    /// <summary>
    /// Formats one <c>nameBefore:x|nameAfter:y</c> chart pair.
    /// </summary>
    /// <param name="name">The chart entry name.</param>
    /// <param name="before">The value before the play, or <see langword="null"/> when unknown.</param>
    /// <param name="after">The value after the play.</param>
    /// <returns>The pipe-delimited pair.</returns>
    public static string ChartEntry(string name, object? before, object? after) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}Before:{FormatValue(before)}|{name}After:{FormatValue(after)}");

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        double number => Math.Round(number, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture),
        float number => Math.Round(number, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static int Statistic(Score score, HitResult result) =>
        score.Statistics.TryGetValue(result, out int value) ? value : 0;
}