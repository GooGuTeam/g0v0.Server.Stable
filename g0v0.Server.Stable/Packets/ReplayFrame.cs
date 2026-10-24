// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// One cursor sample of a spectated or replayed play.
/// </summary>
/// <remarks>
/// The layout is shared by every ruleset; the taiko, catch and mania clients reuse
/// <see cref="ButtonState"/> and <see cref="TaikoByte"/> for their own input encoding.
/// </remarks>
public class ReplayFrame
{
    /// <summary>
    /// Gets or sets the pressed mouse buttons, as the osu! ruleset reports them.
    /// </summary>
    public byte ButtonState { get; set; }

    /// <summary>
    /// Gets or sets the ruleset-specific input byte used by taiko, catch and mania.
    /// </summary>
    public byte TaikoByte { get; set; }

    /// <summary>
    /// Gets or sets the horizontal cursor position in osu!pixels.
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Gets or sets the vertical cursor position in osu!pixels.
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// Gets or sets the sample timestamp in milliseconds from the start of the play.
    /// </summary>
    public int Time { get; set; }
}