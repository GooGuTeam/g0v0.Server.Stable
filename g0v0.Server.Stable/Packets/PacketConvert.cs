// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using g0v0.Server.Stable.Attributes;
using osu.Game.IO.Legacy;

namespace g0v0.Server.Stable.Packets;

/// <summary>
/// Reads and writes framed stable protocol packets without closing the caller's stream.
/// </summary>
public static class PacketConvert
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();
    private static readonly ConcurrentDictionary<PropertyInfo, OsuFieldAttribute> Fields = new();

    /// <summary>
    /// Serializes an inline protocol value without a packet header.
    /// </summary>
    /// <param name="value">The payload object, such as a replay frame bundle.</param>
    /// <returns>The payload bytes.</returns>
    public static byte[] SerializePayload(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using MemoryStream stream = new();
        using (SerializationWriter writer = new(stream, leaveOpen: true))
        {
            WriteValue(value.GetType(), value, writer);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Serializes one packet, including its seven-byte header.
    /// </summary>
    /// <param name="packet">The packet to serialize.</param>
    /// <returns>The complete packet bytes.</returns>
    public static byte[] Serialize(IPacket packet)
    {
        using MemoryStream stream = new();
        Serialize(stream, packet);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes one complete packet at the stream's current position.
    /// </summary>
    /// <param name="stream">The destination stream, which is left open.</param>
    /// <param name="packet">The packet to serialize.</param>
    public static void Serialize(Stream stream, IPacket packet)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(packet);
        Type type = packet.GetType();
        OsuPacketAttribute attribute = GetPacketAttribute(type);
        ushort id = checked((ushort)attribute.Id);
        using MemoryStream payload = new();
        using (SerializationWriter writer = new(payload, leaveOpen: true))
        {
            if (attribute.RawData)
            {
                byte[] data = type.GetProperty("Data", BindingFlags.Public | BindingFlags.Instance)?.GetValue(packet) as byte[]
                    ?? throw new InvalidOperationException($"Type {type.FullName} must have a non-null byte[] Data property.");
                writer.WriteRawBytes(data);
            }
            else if (attribute.HasCustomWriter)
            {
                MethodInfo write = type.GetMethod("Write", BindingFlags.Public | BindingFlags.Instance, [typeof(SerializationWriter)])
                    ?? throw new InvalidOperationException($"Type {type.FullName} does not have a custom Write method.");
                write.Invoke(packet, [writer]);
            }
            else
            {
                WriteValue(type, packet, writer);
            }
        }

        Span<byte> header = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(header, id);
        header[2] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(header[3..], checked((uint)payload.Length));
        stream.Write(header);
        payload.Position = 0;
        payload.CopyTo(stream);
    }

    /// <summary>
    /// Serializes packets in enumeration order, each with its own header.
    /// </summary>
    /// <param name="packets">The packets to serialize.</param>
    /// <returns>The concatenated packet bytes.</returns>
    public static byte[] SerializeAll(IEnumerable<IPacket> packets)
    {
        using MemoryStream stream = new();
        SerializeAll(stream, packets);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes packets in enumeration order at the stream's current position.
    /// </summary>
    /// <param name="stream">The destination stream, which is left open.</param>
    /// <param name="packets">The packets to serialize.</param>
    public static void SerializeAll(Stream stream, IEnumerable<IPacket> packets)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(packets);
        foreach (IPacket packet in packets)
        {
            Serialize(stream, packet);
        }
    }

    /// <summary>
    /// Reads all complete frames, skipping unknown IDs and invalid payloads.
    /// </summary>
    /// <param name="stream">The packet stream at its current position.</param>
    /// <param name="facade">Services available to custom readers.</param>
    /// <returns>Successfully parsed packets in wire order. An incomplete frame ends parsing.</returns>
    public static IPacket[] DeserializeAll(Stream stream, IPacketFacade facade)
    {
        List<IPacket> packets = new();
        while (TryReadEnvelope(stream, out ushort id, out byte[] payload))
        {
            if (OsuPacketAttribute.Packets.TryGetValue(id, out PacketRegistration registration)
                && DeserializePayload(registration.Type, payload, facade) is { } packet)
            {
                packets.Add(packet);
            }
        }

        return packets.ToArray();
    }

    /// <summary>
    /// Reads a single packet if its ID matches the requested type.
    /// </summary>
    /// <typeparam name="T">The expected packet type.</typeparam>
    /// <param name="stream">The packet stream at its current position.</param>
    /// <param name="facade">Services available to custom readers.</param>
    /// <returns>The packet, or null if its ID does not match or parsing fails.</returns>
    public static T? Deserialize<T>(Stream stream, IPacketFacade facade)
        where T : class, IPacket
    {
        if (!TryReadEnvelope(stream, out ushort id, out byte[] payload))
        {
            return null;
        }

        OsuPacketAttribute? attribute = typeof(T).GetCustomAttribute<OsuPacketAttribute>();
        return attribute?.Id == id ? DeserializePayload(typeof(T), payload, facade) as T : null;
    }

    /// <summary>
    /// Reads a single packet using its registered ID.
    /// </summary>
    /// <param name="stream">The packet stream at its current position.</param>
    /// <param name="facade">Services available to custom readers.</param>
    /// <returns>The packet, or null if its ID is unknown or parsing fails.</returns>
    public static IPacket? Deserialize(Stream stream, IPacketFacade facade)
    {
        return TryReadEnvelope(stream, out ushort id, out byte[] payload)
               && OsuPacketAttribute.Packets.TryGetValue(id, out PacketRegistration registration)
            ? DeserializePayload(registration.Type, payload, facade)
            : null;
    }

    private static bool TryReadEnvelope(Stream stream, out ushort id, out byte[] payload)
    {
        id = 0;
        payload = [];
        try
        {
            Span<byte> header = stackalloc byte[7];
            stream.ReadExactly(header);
            id = BinaryPrimitives.ReadUInt16LittleEndian(header);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header[3..]);
            if (length > int.MaxValue || (stream.CanSeek && length > stream.Length - stream.Position))
            {
                return false;
            }

            // Grow only as bytes arrive, including for non-seekable streams with untrusted lengths.
            using MemoryStream buffer = new();
            byte[] chunk = new byte[(int)Math.Min(length, 81920u)];
            uint remaining = length;
            while (remaining > 0)
            {
                int count = stream.Read(chunk, 0, (int)Math.Min(remaining, (uint)chunk.Length));
                if (count == 0)
                {
                    return false;
                }

                buffer.Write(chunk, 0, count);
                remaining -= (uint)count;
            }

            payload = buffer.ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static IPacket? DeserializePayload(Type type, byte[] payload, IPacketFacade facade)
    {
        try
        {
            OsuPacketAttribute attribute = GetPacketAttribute(type);
            using MemoryStream stream = new(payload, writable: false);
            using SerializationReader reader = new(stream);
            object? packet;
            if (attribute.RawData)
            {
                packet = Activator.CreateInstance(type);
                PropertyInfo data = type.GetProperty("Data", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"Type {type.FullName} does not have a Data property.");
                data.SetValue(packet, payload);
                return packet as IPacket;
            }

            if (attribute.HasCustomReader)
            {
                MethodInfo read = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static, [typeof(SerializationReader), typeof(IPacketFacade)])
                    ?? throw new InvalidOperationException($"Type {type.FullName} does not have a custom Read method.");
                packet = read.Invoke(null, [reader, facade]);
            }
            else
            {
                packet = ReadValue(type, reader);
            }

            return stream.Position == stream.Length ? packet as IPacket : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException
                                        or NotSupportedException or MemberAccessException or TargetInvocationException)
        {
            return null;
        }
    }

    private static object? ReadValue(Type type, SerializationReader reader, OsuFieldAttribute? field = null, object? owner = null)
    {
        if (type.IsEnum)
        {
            return Enum.ToObject(type, ReadValue(Enum.GetUnderlyingType(type), reader)!);
        }

        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
        {
            return ReadValue(underlyingType, reader);
        }

        if (type.IsArray)
        {
            return ReadArray(type, reader, field, owner);
        }

        if (type == typeof(string))
        {
            return ReadString(reader, field?.DistinguishEmptyString == true);
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean: return reader.ReadBoolean();
            case TypeCode.Byte: return reader.ReadByte();
            case TypeCode.SByte: return reader.ReadSByte();
            case TypeCode.Int16: return reader.ReadInt16();
            case TypeCode.UInt16: return reader.ReadUInt16();
            case TypeCode.Int32: return reader.ReadInt32();
            case TypeCode.UInt32: return reader.ReadUInt32();
            case TypeCode.Int64: return reader.ReadInt64();
            case TypeCode.UInt64: return reader.ReadUInt64();
            case TypeCode.Single: return reader.ReadSingle();
            case TypeCode.Double: return reader.ReadDouble();
            case TypeCode.Decimal: return reader.ReadDecimal();
            case TypeCode.Char: return reader.ReadChar();
            case TypeCode.DateTime: return reader.ReadDateTime();
        }

        object instance = Activator.CreateInstance(type)
            ?? throw new NotSupportedException($"Cannot create type {type.FullName}.");
        foreach (PropertyInfo property in GetProperties(type))
        {
            OsuFieldAttribute member = GetField(property);
            if (IsFieldPresent(member, instance))
            {
                property.SetValue(instance, ReadValue(property.PropertyType, reader, member, instance));
            }
        }

        return instance;
    }

    private static Array? ReadArray(Type type, SerializationReader reader, OsuFieldAttribute? field, object? owner)
    {
        int arrayLength = GetArrayLength(field, owner);
        if (arrayLength < 0 && (type == typeof(byte[]) || type == typeof(char[])))
        {
            // These reader methods can silently return short arrays; validate the count first.
            long position = reader.BaseStream.Position;
            int length = reader.ReadInt32();
            if (length < -1 || length > reader.RemainingBytes)
            {
                throw new InvalidDataException("Invalid array length.");
            }

            reader.BaseStream.Position = position;
            Array? array = type == typeof(byte[]) ? reader.ReadByteArray() : reader.ReadCharArray();
            return array != null && array.Length != length ? throw new EndOfStreamException() : array;
        }

        if (type.GetArrayRank() != 1)
        {
            throw new NotSupportedException("Only one-dimensional packet arrays are supported.");
        }

        Type elementType = type.GetElementType()!;
        int count = arrayLength >= 0 ? arrayLength : reader.ReadUInt16();
        Array result = Array.CreateInstance(elementType, count);
        for (int i = 0; i < count; i++)
        {
            if (IsElementPresent(field, owner, i))
            {
                result.SetValue(ReadValue(elementType, reader), i);
            }
        }

        return result;
    }

    private static void WriteValue(Type type, object? value, SerializationWriter writer, OsuFieldAttribute? field = null, object? owner = null)
    {
        if (type.IsEnum)
        {
            Type underlyingType = Enum.GetUnderlyingType(type);
            WriteValue(underlyingType, Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture), writer);
            return;
        }

        if (Nullable.GetUnderlyingType(type) is { } nullableType)
        {
            WriteValue(nullableType, value, writer);
            return;
        }

        if (type.IsArray)
        {
            WriteArray(type, (Array?)value, writer, field, owner);
            return;
        }

        if (type == typeof(string))
        {
            string? text = (string?)value;
            writer.Write(field?.DistinguishEmptyString == true || !string.IsNullOrEmpty(text) ? text! : null!);
            return;
        }

        if (value == null)
        {
            throw new InvalidOperationException($"Type {type.FullName} has no null representation in the packet payload. Use a custom writer for conditional fields.");
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean: writer.Write((bool)value); return;
            case TypeCode.Byte: writer.Write((byte)value); return;
            case TypeCode.SByte: writer.Write((sbyte)value); return;
            case TypeCode.Int16: writer.Write((short)value); return;
            case TypeCode.UInt16: writer.Write((ushort)value); return;
            case TypeCode.Int32: writer.Write((int)value); return;
            case TypeCode.UInt32: writer.Write((uint)value); return;
            case TypeCode.Int64: writer.Write((long)value); return;
            case TypeCode.UInt64: writer.Write((ulong)value); return;
            case TypeCode.Single: writer.Write((float)value); return;
            case TypeCode.Double: writer.Write((double)value); return;
            case TypeCode.Decimal: writer.Write((decimal)value); return;
            case TypeCode.Char: writer.Write((char)value); return;
            case TypeCode.DateTime: writer.Write((DateTime)value); return;
        }

        foreach (PropertyInfo property in GetProperties(type))
        {
            OsuFieldAttribute member = GetField(property);
            if (IsFieldPresent(member, value))
            {
                WriteValue(property.PropertyType, property.GetValue(value), writer, member, value);
            }
        }
    }

    private static void WriteArray(Type type, Array? array, SerializationWriter writer, OsuFieldAttribute? field, object? owner)
    {
        int arrayLength = GetArrayLength(field, owner);
        if (arrayLength < 0 && type == typeof(byte[]))
        {
            writer.Write((byte[])array!);
            return;
        }

        if (arrayLength < 0 && type == typeof(char[]))
        {
            writer.Write((char[])array!);
            return;
        }

        if (type.GetArrayRank() != 1)
        {
            throw new NotSupportedException("Only one-dimensional packet arrays are supported.");
        }

        if (array == null)
        {
            throw new InvalidOperationException("Count-prefixed packet arrays cannot be null.");
        }

        if (arrayLength >= 0)
        {
            if (array.Length != arrayLength)
            {
                throw new InvalidOperationException($"Expected {arrayLength} array elements, got {array.Length}.");
            }
        }
        else
        {
            writer.Write(checked((ushort)array.Length));
        }

        Type elementType = type.GetElementType()!;
        for (int i = 0; i < array.Length; i++)
        {
            if (IsElementPresent(field, owner, i))
            {
                WriteValue(elementType, array.GetValue(i), writer);
            }
        }
    }

    private static string? ReadString(SerializationReader reader, bool distinguishEmptyString)
    {
        long position = reader.BaseStream.Position;
        byte marker = reader.ReadByte();

        // The wire format marks "no string" with 0x00 and "empty string" with 0x0B; the payload then
        // repeats the marker that the reader consumes itself.
        if (marker == 0)
        {
            return distinguishEmptyString ? null : string.Empty;
        }

        if (marker != 11)
        {
            throw new InvalidDataException("Invalid string marker.");
        }

        reader.BaseStream.Position = position;
        return reader.ReadString();
    }

    private static OsuFieldAttribute GetField(PropertyInfo property)
    {
        return Fields.GetOrAdd(property, static p => p.GetCustomAttribute<OsuFieldAttribute>() ?? new OsuFieldAttribute());
    }

    private static object GetFieldValue(object? owner, string name)
    {
        return owner?.GetType().GetProperty(name)?.GetValue(owner)
            ?? throw new InvalidOperationException($"Cannot read wire layout property {name}.");
    }

    private static bool IsFieldPresent(OsuFieldAttribute field, object owner)
    {
        return field.Condition == null || (bool)GetFieldValue(owner, field.Condition);
    }

    private static int GetArrayLength(OsuFieldAttribute? field, object? owner)
    {
        return field?.LengthFrom is { } name
            ? Convert.ToInt32(GetFieldValue(owner, name), CultureInfo.InvariantCulture)
            : field?.ArrayLength ?? -1;
    }

    private static bool IsElementPresent(OsuFieldAttribute? field, object? owner, int index)
    {
        if (field?.ElementFlags is not { } name)
        {
            return true;
        }

        Array flags = (Array)GetFieldValue(owner, name);
        return (Convert.ToInt32(flags.GetValue(index), CultureInfo.InvariantCulture) & field.ElementMask) != 0;
    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        return Properties.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken)
            .ToArray());
    }

    private static OsuPacketAttribute GetPacketAttribute(Type type)
    {
        return type.GetCustomAttribute<OsuPacketAttribute>()
            ?? throw new InvalidOperationException($"Type {type.FullName} does not have an OsuPacketAttribute.");
    }
}