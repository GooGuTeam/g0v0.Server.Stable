// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Parses the unframed text body of a stable client's login request.
/// </summary>
/// <remarks>
/// <para>
/// <c>POST /</c> with <c>User-Agent: osu!</c> and no packet framing carries a UTF-8 body of exactly
/// three lines:
/// </para>
/// <code>
/// &lt;username&gt;\n
/// &lt;password md5, lowercase hex&gt;\n
/// &lt;version&gt;|&lt;utc offset&gt;|&lt;display city&gt;|&lt;client hashes&gt;|&lt;pm private&gt;\n
/// </code>
/// <para>
/// The fourth field is the original colon-terminated client hash (ch) with five fields:
/// </para>
/// <code>
/// &lt;osu! path md5&gt;:&lt;adapters&gt;:&lt;adapters md5&gt;:&lt;installation guid double md5&gt;:&lt;disk signature double md5&gt;:
/// </code>
/// <para>
/// <c>&lt;adapters&gt;</c> is a dot-terminated adapter list, or the literal
/// <c>runningunderwine</c> when the client runs under Wine. <c>&lt;display city&gt;</c> and
/// <c>&lt;pm private&gt;</c> are the literals <c>1</c> or <c>0</c>. <c>&lt;utc offset&gt;</c> is a
/// signed decimal hour count.
/// </para>
/// </remarks>
public sealed class LoginPacket
{
    private const string WineAdapterSentinel = "runningunderwine";
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private LoginPacket(OsuVersion version)
    {
        Version = version;
    }

    /// <summary>
    /// Gets the username without whitespace normalization.
    /// </summary>
    public string Username { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the password MD5 as text, without hashing or decoding it.
    /// </summary>
    public string PasswordMd5 { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the original client version string.
    /// </summary>
    public string OsuVersionString { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the parsed build date, revision and release channel.
    /// </summary>
    public OsuVersion Version { get; }

    /// <summary>
    /// Gets the decimal UTC offset in whole hours.
    /// </summary>
    public int UtcOffset { get; private init; }

    /// <summary>
    /// Gets a value indicating whether display_city was exactly the text 1.
    /// </summary>
    public bool DisplayCity { get; private init; }

    /// <summary>
    /// Gets a value indicating whether pm_private was exactly the text 1.
    /// </summary>
    public bool PmPrivate { get; private init; }

    /// <summary>
    /// Gets the osu! installation path hash.
    /// </summary>
    public string OsuPathMd5 { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the original dot-delimited network adapter string.
    /// </summary>
    public string AdaptersString { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the parsed network adapter entries.
    /// </summary>
    public IReadOnlyList<string> Adapters { get; private init; } = [];

    /// <summary>
    /// Gets a value indicating whether the adapter string is the Wine sentinel.
    /// </summary>
    public bool RunningUnderWine { get; private init; }

    /// <summary>
    /// Gets the network adapter hash.
    /// </summary>
    public string AdaptersMd5 { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the MD5 of the client's already MD5-hashed installation GUID.
    /// </summary>
    public string UninstallMd5 { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the MD5 of the client's already MD5-hashed disk identifier.
    /// </summary>
    public string DiskSignatureMd5 { get; private init; } = string.Empty;

    /// <summary>
    /// Parses an unframed UTF-8 login request.
    /// </summary>
    /// <param name="data">The complete login body, without a seven-byte packet header.</param>
    /// <returns>The parsed login data.</returns>
    /// <exception cref="FormatException">The login fields, version, or adapter layout are invalid.</exception>
    /// <exception cref="DecoderFallbackException">The body is not valid UTF-8.</exception>
    public static LoginPacket Parse(ReadOnlySpan<byte> data)
    {
        return Parse(Utf8.GetString(data));
    }

    /// <summary>
    /// Parses the remaining UTF-8 body without closing the caller's stream.
    /// </summary>
    /// <param name="stream">The request body stream at its current position.</param>
    /// <returns>The parsed login data.</returns>
    public static LoginPacket Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using StreamReader reader = new(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Parses the three login lines and their pipe- and colon-delimited fields.
    /// </summary>
    /// <param name="data">The decoded request body. Trailing line feeds are optional.</param>
    /// <returns>The parsed login data.</returns>
    /// <exception cref="FormatException">The login fields, version, or adapter layout are invalid.</exception>
    public static LoginPacket Parse(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        string[] lines = data.TrimEnd('\n').Split('\n', 3);
        if (lines.Length != 3)
        {
            throw new FormatException("A login request must contain three lines.");
        }

        string[] fields = lines[2].Split('|', 5);
        if (fields.Length != 5)
        {
            throw new FormatException("The login metadata must contain five fields.");
        }

        string hashes = fields[3];
        if (!hashes.EndsWith(':'))
        {
            throw new FormatException("The client hash list must end with a colon.");
        }

        string[] clientHashes = hashes[..^1].Split(':', 5);
        if (clientHashes.Length != 5)
        {
            throw new FormatException("The client hash list must contain five fields.");
        }

        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int utcOffset))
        {
            throw new FormatException("The UTC offset must be a decimal integer.");
        }

        OsuVersion version = OsuVersion.Parse(fields[0]) ?? throw new FormatException("The osu! client version is invalid.");
        (string[] adapters, bool runningUnderWine) = ParseAdaptersString(clientHashes[1]);
        return new LoginPacket(version)
        {
            Username = lines[0],
            PasswordMd5 = lines[1],
            OsuVersionString = fields[0],
            UtcOffset = utcOffset,
            DisplayCity = string.Equals(fields[2], "1", StringComparison.Ordinal),
            PmPrivate = string.Equals(fields[4], "1", StringComparison.Ordinal),
            OsuPathMd5 = clientHashes[0],
            AdaptersString = clientHashes[1],
            Adapters = adapters,
            RunningUnderWine = runningUnderWine,
            AdaptersMd5 = clientHashes[2],
            UninstallMd5 = clientHashes[3],
            DiskSignatureMd5 = clientHashes[4]
        };
    }

    /// <summary>
    /// Parses a dot-terminated adapter list or the Wine sentinel.
    /// </summary>
    /// <param name="adaptersString">The original adapter string.</param>
    /// <returns>The adapter entries and whether the client is running under Wine.</returns>
    /// <exception cref="FormatException">A non-Wine list is missing its trailing dot.</exception>
    public static (string[] Adapters, bool RunningUnderWine) ParseAdaptersString(string adaptersString)
    {
        ArgumentNullException.ThrowIfNull(adaptersString);
        if (string.Equals(adaptersString, WineAdapterSentinel, StringComparison.Ordinal))
        {
            return ([WineAdapterSentinel], true);
        }

        return adaptersString.EndsWith('.')
            ? (adaptersString[..^1].Split('.'), false)
            : throw new FormatException("The adapter list must end with a dot.");
    }

    /// <summary>
    /// Returns the original five-field client hash (ch) used to identify this device.
    /// </summary>
    /// <returns>The client-supplied hashes and adapter string, including the trailing colon.</returns>
    /// <remarks>The client has already hashed each identifier; preserve its text without further hashing or normalization.</remarks>
    public string GetDeviceHash()
        => $"{OsuPathMd5}:{AdaptersString}:{AdaptersMd5}:{UninstallMd5}:{DiskSignatureMd5}:";
}