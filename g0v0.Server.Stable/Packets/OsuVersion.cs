// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.RegularExpressions;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Describes a parsed osu! client build.
/// </summary>
/// <param name="Date">The build date.</param>
/// <param name="Revision">The optional single-digit build revision.</param>
/// <param name="Stream">The release channel.</param>
public sealed partial record OsuVersion(DateOnly Date, int? Revision, OsuReleaseChannel Stream)
{
    /// <summary>
    /// Parses the build string a stable client reports in its login form.
    /// </summary>
    /// <param name="value">A version such as b20230814.2cuttingedge.</param>
    /// <returns>The parsed version, or null if the format or calendar date is invalid.</returns>
    public static OsuVersion? Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        System.Text.RegularExpressions.Match match = VersionPattern().Match(value);
        if (!match.Success || !DateOnly.TryParseExact(
                match.Groups["date"].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date))
        {
            return null;
        }

        int? revision = match.Groups["revision"].Success
            ? int.Parse(match.Groups["revision"].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;
        OsuReleaseChannel stream = match.Groups["stream"].Value switch
        {
            "beta" => OsuReleaseChannel.Beta,
            "cuttingedge" => OsuReleaseChannel.CuttingEdge,
            "dev" => OsuReleaseChannel.Dev,
            "tourney" => OsuReleaseChannel.Tourney,
            _ => OsuReleaseChannel.Stable
        };
        return new OsuVersion(date, revision, stream);
    }

    [GeneratedRegex(@"\Ab(?<date>[0-9]{8})(?:\.(?<revision>[0-9]))?(?<stream>beta|cuttingedge|dev|tourney)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionPattern();
}