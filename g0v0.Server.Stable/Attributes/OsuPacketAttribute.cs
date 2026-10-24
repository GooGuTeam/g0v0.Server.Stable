// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace g0v0.Server.Stable.Attributes;

/// <summary>
/// Describes a stable protocol packet and its payload reader.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class OsuPacketAttribute : Attribute
{
    // Static initialisers run in declaration order: the table is built before the view is read.
    private static readonly Dictionary<int, PacketRegistration> Registrations = BuildRegistrations();

    /// <summary>
    /// Initializes a new instance of the <see cref="OsuPacketAttribute"/> class.
    /// </summary>
    /// <param name="id">The packet ID.</param>
    /// <param name="allowRestrict">Whether restricted users may send this packet.</param>
    /// <param name="rawData">Whether Data receives the entire payload.</param>
    /// <param name="hasCustomReader">Whether the packet supplies a static Read method.</param>
    /// <param name="hasCustomWriter">Whether the packet supplies an instance Write method.</param>
    public OsuPacketAttribute(int id, bool allowRestrict = false, bool rawData = false, bool hasCustomReader = false, bool hasCustomWriter = false)
    {
        Id = id;
        AllowRestrict = allowRestrict;
        RawData = rawData;
        HasCustomReader = hasCustomReader;
        HasCustomWriter = hasCustomWriter;
    }

    /// <summary>
    /// Gets the packet table the dispatcher reads.
    /// </summary>
    /// <remarks>
    /// The dispatcher needs the packet type and the restricted-account flag on every poll, so both
    /// are resolved once from the attribute metadata at startup instead of being reflected per
    /// request. The view is read-only while <see cref="Register"/> and <see cref="Unregister"/> keep
    /// the mutable dictionary private.
    /// </remarks>
    public static IReadOnlyDictionary<int, PacketRegistration> Packets => Registrations;

    /// <summary>
    /// Gets the packet ID.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets a value indicating whether restricted users may send this packet.
    /// </summary>
    public bool AllowRestrict { get; }

    /// <summary>
    /// Gets a value indicating whether Data receives the entire payload.
    /// </summary>
    public bool RawData { get; }

    /// <summary>
    /// Gets a value indicating whether the packet supplies a static Read method.
    /// </summary>
    public bool HasCustomReader { get; }

    /// <summary>
    /// Gets a value indicating whether the packet supplies an instance Write method.
    /// </summary>
    public bool HasCustomWriter { get; }

    /// <summary>
    /// Reads the restricted-account flag of a packet type from its attribute metadata.
    /// </summary>
    /// <param name="type">The packet type.</param>
    /// <returns><see langword="true"/> when a restricted account may send the packet.</returns>
    /// <remarks>
    /// The constructor argument is read through <see cref="CustomAttributeData" />, so no attribute
    /// instance is constructed.
    /// </remarks>
    public static bool IsAllowedForRestricted(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        CustomAttributeData? attribute = type.CustomAttributes
            .FirstOrDefault(candidate => candidate.AttributeType == typeof(OsuPacketAttribute));
        if (attribute is null)
        {
            return false;
        }

        // Select first: the default value of the struct returned by FirstOrDefault would throw when
        // its MemberName is read.
        bool? named = attribute.NamedArguments
            .Where(candidate => string.Equals(candidate.MemberName, nameof(AllowRestrict), StringComparison.Ordinal))
            .Select(candidate => (bool?)candidate.TypedValue.Value)
            .FirstOrDefault();
        if (named.HasValue)
        {
            return named.Value;
        }

        // The flag is the second constructor argument of the attribute.
        return attribute.ConstructorArguments.Count > 1 && attribute.ConstructorArguments[1].Value is true;
    }

    /// <summary>
    /// Registers a packet type at runtime.
    /// </summary>
    /// <param name="id">The packet ID.</param>
    /// <param name="type">The packet type.</param>
    /// <remarks>
    /// Only tests use this: production IDs come from the attributes scanned at startup. Registering
    /// an ID that is already present replaces it.
    /// </remarks>
    public static void Register(int id, Type type)
    {
        Registrations[id] = PacketRegistration.For(type);
    }

    /// <summary>
    /// Removes a runtime registration.
    /// </summary>
    /// <param name="id">The packet ID to remove.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public static bool Unregister(int id) => Registrations.Remove(id);

    private static Dictionary<int, PacketRegistration> BuildRegistrations()
    {
        Dictionary<int, PacketRegistration> packets = new();
        foreach (Type type in typeof(OsuPacketAttribute).Assembly.GetTypes())
        {
            // Inspect metadata without constructing attributes or registering them on every reflection read.
            CustomAttributeData? attribute = type.CustomAttributes
                .FirstOrDefault(candidate => candidate.AttributeType == typeof(OsuPacketAttribute));
            if (attribute == null)
            {
                continue;
            }

            int id = (int)attribute.ConstructorArguments[0].Value!;
            PacketRegistration registration = PacketRegistration.For(type);
            if (!packets.TryAdd(id, registration))
            {
                throw new InvalidOperationException($"Duplicate packet ID {id} found for types {packets[id].Type.FullName} and {type.FullName}.");
            }
        }

        return packets;
    }
}