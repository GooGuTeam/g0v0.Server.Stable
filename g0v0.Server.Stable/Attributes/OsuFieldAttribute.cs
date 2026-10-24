// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Stable.Attributes;

/// <summary>
/// Describes exceptions to the default inline payload field layout.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OsuFieldAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a fixed array size with no count prefix; -1 uses the default prefix.
    /// </summary>
    public int ArrayLength { get; set; } = -1;

    /// <summary>
    /// Gets or sets the preceding property containing an array's size instead of a prefix.
    /// </summary>
    public string? LengthFrom { get; set; }

    /// <summary>
    /// Gets or sets the preceding boolean property enabling this field.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets the preceding flags array determining which elements are present.
    /// </summary>
    public string? ElementFlags { get; set; }

    /// <summary>
    /// Gets or sets the mask selecting present elements from ElementFlags.
    /// </summary>
    public int ElementMask { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether null and present-but-empty strings remain distinct.
    /// </summary>
    public bool DistinguishEmptyString { get; set; }
}