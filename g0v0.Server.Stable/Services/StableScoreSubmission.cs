// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using g0v0.Server.Common.Rulesets;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO.Legacy;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using Score = g0v0.Server.Common.Database.Models.Score;

namespace g0v0.Server.Stable.Services;

/// <summary>
/// Decodes the stable modular score submission and constructs its legacy replay.
/// </summary>
/// <remarks>
/// <para>
/// The <c>score</c> form field is Rijndael-CBC ciphertext with a 256-bit block and PKCS7 padding,
/// base64 encoded. The key is <c>osu!-scoreburgr---------&lt;eight digit client version&gt;</c> and the
/// IV is the base64-decoded <c>iv</c> field, so a version mismatch shows up as an invalid padding
/// rather than a wrong value.
/// </para>
/// <para>
/// The plaintext is a colon-separated list. The first 18 fields are interpreted; the client appends
/// a nineteenth client-flags field that is deliberately ignored.
/// </para>
/// <code>
///  0  &lt;beatmap md5&gt;
///  1  &lt;username&gt;                          trailing space when the player is a supporter
///  2  &lt;online checksum&gt;                  used for duplicate detection
///  3  &lt;300 count&gt;
///  4  &lt;100 count&gt;
///  5  &lt;50 count&gt;
///  6  &lt;geki count&gt;
///  7  &lt;katu count&gt;
///  8  &lt;miss count&gt;
///  9  &lt;total score&gt;
/// 10  &lt;max combo&gt;
/// 11  &lt;perfect&gt;                           True / False
/// 12  &lt;grade&gt;                             letter reported by the client, replaced by the server
/// 13  &lt;mods&gt;                              legacy bitmask
/// 14  &lt;passed&gt;                            True / False
/// 15  &lt;ruleset&gt;                           0-3 vanilla, 4-7 relax/autopilot variants
/// 16  &lt;client timestamp&gt;                  yyMMddHHmmss, local time
/// 17  &lt;client build&gt;                      yyyyMMdd
/// 18  &lt;client flags&gt;                      sent but not interpreted
/// </code>
/// <para>
/// The submission is verified by rebuilding the client hash from these values; a mismatch means the
/// payload was replayed or tampered with, so the score is rejected.
/// </para>
/// </remarks>
public sealed class StableScoreSubmission
{
    /// <summary>
    /// The number of interpreted submission fields.
    /// </summary>
    private const int ScoreFieldCount = 18;

    private readonly string[] _fields;

    private StableScoreSubmission(string[] fields)
    {
        _fields = fields;
    }

    /// <summary>
    /// Gets the submitted beatmap MD5.
    /// </summary>
    public string BeatmapChecksum => _fields[0];

    /// <summary>
    /// Gets the online checksum used by Stable for duplicate tracking.
    /// </summary>
    public string Checksum => _fields[2].ToLowerInvariant();

    /// <summary>
    /// Gets the username, removing only the supporter suffix space.
    /// </summary>
    public string Username => _fields[1].EndsWith(' ') ? _fields[1][..^1] : _fields[1];

    /// <summary>
    /// Gets the legacy ruleset ID.
    /// </summary>
    public int Mode => Number(15);

    /// <summary>
    /// Gets the stable mod bitmask.
    /// </summary>
    public LegacyMods Mods => (LegacyMods)Number(13);

    /// <summary>
    /// Gets a value indicating whether gameplay was completed.
    /// </summary>
    public bool Passed => string.Equals(_fields[14], "True", StringComparison.Ordinal);

    /// <summary>
    /// Gets the client timestamp used by the submission checksum.
    /// </summary>
    public DateTime ClientTime => DateTime.ParseExact(_fields[16], "yyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>
    /// Decrypts Rijndael CBC with a 256-bit block and PKCS7 padding, as used by stable.
    /// </summary>
    /// <param name="encrypted">Base64 score data or client hash.</param>
    /// <param name="iv">Base64 initialization vector.</param>
    /// <param name="version">Eight-digit client version.</param>
    /// <returns>The UTF-8 plaintext.</returns>
    public static string Decrypt(string encrypted, string iv, string version)
    {
        if (version.Length != 8 || !version.All(char.IsAsciiDigit))
        {
            throw new FormatException("Invalid score encryption version.");
        }

        byte[] vector = Convert.FromBase64String(iv);
        if (vector.Length != 32)
        {
            throw new FormatException("Invalid score initialization vector.");
        }

        PaddedBufferedBlockCipher cipher = new(new CbcBlockCipher(new RijndaelEngine(256)), new Pkcs7Padding());
        cipher.Init(forEncryption: false, new ParametersWithIV(new KeyParameter(Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{version}")), vector));
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(cipher.DoFinal(Convert.FromBase64String(encrypted)));
    }

    /// <summary>
    /// Validates and parses decrypted submission fields without trusting the reported grade.
    /// </summary>
    /// <param name="plaintext">The colon-delimited score data.</param>
    /// <returns>The parsed submission.</returns>
    /// <remarks>
    /// The client sends nineteen fields; the last one carries its internal client flags and is not
    /// part of the legacy score. Only the first eighteen fields are interpreted, and a trailing
    /// newline or NUL left by the transport is stripped first.
    /// </remarks>
    public static StableScoreSubmission Parse(string plaintext)
    {
        // A trailing newline or NUL is a transport artifact, never part of the last field.
        string[] split = plaintext.TrimEnd('\r', '\n', '\0').Split(':');
        if (split.Length < ScoreFieldCount || split[0].Length != 32 || !split[0].All(char.IsAsciiHexDigit)
            || split[1].Length == 0 || split[2].Length != 32 || !split[2].All(char.IsAsciiHexDigit))
        {
            throw new FormatException("Invalid score fields.");
        }

        string[] fields = split.Length == ScoreFieldCount ? split : split[..ScoreFieldCount];
        StableScoreSubmission result = new(fields);
        for (int i = 3; i <= 10; i++)
        {
            int value = result.Number(i);
            if (value < 0 || (i != 9 && value > ushort.MaxValue))
            {
                throw new FormatException("Invalid score counts.");
            }
        }

        if (result.Mode is < 0 or > 3 || result.Number(13) < 0
                                      || (!string.Equals(fields[11], "True", StringComparison.Ordinal) && !string.Equals(fields[11], "False", StringComparison.Ordinal))
                                      || (!string.Equals(fields[14], "True", StringComparison.Ordinal) && !string.Equals(fields[14], "False", StringComparison.Ordinal)))
        {
            throw new FormatException("Invalid score mode or flags.");
        }

        _ = result.ClientTime;
        return result;
    }

    /// <summary>
    /// Checks the online checksum against all authenticated submission metadata.
    /// </summary>
    /// <param name="username">The authenticated account name.</param>
    /// <param name="version">The client version.</param>
    /// <param name="clientHash">The decrypted client hash.</param>
    /// <param name="storyboardHash">The storyboard hash, or an empty string.</param>
    /// <returns>Whether the checksum matches.</returns>
    public bool VerifyChecksum(string username, string version, string clientHash, string storyboardHash)
    {
        string value = FormattableString.Invariant($"chickenmcnuggets{Number(3) + Number(4)}o15{Number(5)}{Number(6)}smustard{Number(7)}{Number(8)}uu{BeatmapChecksum}{Number(10)}{_fields[11]}{username}{Number(9)}{_fields[12]}{Number(13)}Q{Passed}{Mode}{version}{ClientTime:yyMMddHHmmss}{clientHash}{storyboardHash}");
#pragma warning disable CA5351 // Stable requires this MD5 online checksum.
        return string.Equals(Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))), _fields[2], StringComparison.OrdinalIgnoreCase);
#pragma warning restore CA5351
    }

    /// <summary>
    /// Creates the unprocessed database record with mode-aware legacy statistics.
    /// </summary>
    /// <param name="rulesets">The registered rulesets.</param>
    /// <param name="userId">The authenticated user ID.</param>
    /// <param name="beatmapId">The resolved beatmap ID.</param>
    /// <param name="version">The client version.</param>
    /// <param name="endedAt">The server receipt time.</param>
    /// <param name="duration">The elapsed play duration in milliseconds.</param>
    /// <returns>A score awaiting background processing.</returns>
    public Score ToScore(RulesetManager rulesets, int userId, int beatmapId, string version, DateTimeOffset endedAt, int duration)
    {
        Ruleset ruleset = rulesets.GetRuleset(Mode);
        Mod[] mods = ruleset.ConvertFromLegacyMods(Mods).ToArray();
        Mod? classic = ruleset.CreateMod<ModClassic>();
        if (classic != null)
        {
            mods = [.. mods, classic];
        }

        ScoreInfo info = new() { Ruleset = ruleset.RulesetInfo };
        info.SetCount300(Number(3));
        info.SetCount100(Number(4));
        info.SetCount50(Number(5));
        info.SetCountGeki(Number(6));
        info.SetCountKatu(Number(7));
        info.SetCountMiss(Number(8));
        return new Score
        {
            Type = "stable",
            UserId = userId,
            BeatmapId = beatmapId,
            BeatmapChecksum = BeatmapChecksum,
            Mode = Mode,
            Mods = mods.Select(m => new APIMod(m)).ToList(),
            Statistics = info.Statistics,
            ClassicTotalScore = Number(9),
            MaxCombo = Number(10),
            Passed = Passed,
            Preserve = Passed,
            Rank = ScoreRank.F,
            Processed = false,
            EndedAt = endedAt,
            StartedAt = endedAt.AddMilliseconds(-duration),
            ClientVersion = version
        };
    }

    /// <summary>
    /// Wraps the uploaded compressed replay frames in a complete stable .osr file.
    /// </summary>
    /// <param name="score">The persisted score, including its allocated ID.</param>
    /// <param name="username">The authenticated username.</param>
    /// <param name="version">The client version.</param>
    /// <param name="frames">The compressed replay file from the multipart form.</param>
    /// <returns>A complete replay file.</returns>
    public byte[] CreateReplay(Score score, string username, string version, byte[] frames)
    {
        using MemoryStream stream = new();
        using (SerializationWriter writer = new(stream, leaveOpen: true))
        {
            writer.Write((byte)Mode);
            writer.Write(int.Parse(version, CultureInfo.InvariantCulture));
            writer.Write(BeatmapChecksum);
            writer.Write(username);
            writer.Write(_fields[2]);
            for (int i = 3; i <= 8; i++)
            {
                writer.Write((ushort)Number(i));
            }

            writer.Write(Number(9));
            writer.Write((ushort)Number(10));
            writer.Write(string.Equals(_fields[11], "True", StringComparison.Ordinal));
            writer.Write((int)Mods);
            writer.Write(string.Empty);
            writer.Write(score.EndedAt.UtcDateTime);
            writer.WriteByteArray(frames);
            writer.Write(score.Id);
        }

        return stream.ToArray();
    }

    private int Number(int index) => int.Parse(_fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture);
}