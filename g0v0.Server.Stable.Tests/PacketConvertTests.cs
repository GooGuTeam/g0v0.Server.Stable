// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Rulesets;
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Client;
using g0v0.Server.Stable.Packets.Server;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO.Legacy;

namespace g0v0.Server.Stable.Tests;

public class PacketConvertTests
{
    private readonly PacketFacade facade = new()
    {
        RulesetManager = new RulesetManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), includeOfficial: false)
    };

    [Test]
    public void ReadsNestedPropertiesAndUnicodeStringsInWireOrder()
    {
        string text = new('你', 150);
        using MemoryStream stream = packet(1, writer =>
        {
            writer.Write("sender");
            writer.Write(text);
            writer.Write("#中文");
            writer.Write(-42);
        });

        PacketSendPublicMessage? result = PacketConvert.Deserialize<PacketSendPublicMessage>(stream, facade);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Message.Sender, Is.EqualTo("sender"));
            Assert.That(result.Message.Text, Is.EqualTo(text));
            Assert.That(result.Message.Recipient, Is.EqualTo("#中文"));
            Assert.That(result.Message.SenderId, Is.EqualTo(-42));
            Assert.That(stream.CanRead, Is.True);
        });
    }

    [Test]
    public void ReadsEnumsUsingTheirUnderlyingWidths()
    {
        using MemoryStream stream = packet(79, writer => writer.Write((int)PresenceFilter.Friends));
        Assert.That(PacketConvert.Deserialize<PacketReceiveUpdates>(stream, facade)!.Value, Is.EqualTo(PresenceFilter.Friends));
        using MemoryStream mods = packet(51, writer => writer.Write((int)LegacyMods.Mirror));
        Assert.That(PacketConvert.Deserialize<PacketMatchChangeMods>(mods, facade)!.Mods, Is.EqualTo(LegacyMods.Mirror));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void ReadsIntegerArraysWithUnsignedShortCounts(int count)
    {
        int[] values = [-1, 123456, int.MaxValue];
        using MemoryStream stream = packet(85, writer =>
        {
            writer.Write((ushort)count);
            foreach (int value in values.Take(count))
            {
                writer.Write(value);
            }
        });
        Assert.That(PacketConvert.Deserialize<PacketUserStatsRequest>(stream, facade)!.UserIds, Is.EqualTo(values.Take(count)));
    }

    [Test]
    public void ReadsByteAndCharArraysWithSerializationReaderMethods()
    {
        byte[] bytes = [0, 255, 10];
        char[] characters = ['a', '你'];
        using MemoryStream stream = packet(50000, writer =>
        {
            writer.Write(bytes);
            writer.Write(characters);
            writer.Write((byte)UserAction.Playing);
        });

        ArrayPacket? result = PacketConvert.Deserialize<ArrayPacket>(stream, facade);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Bytes, Is.EqualTo(bytes));
            Assert.That(result.Characters, Is.EqualTo(characters));
            Assert.That(result.Action, Is.EqualTo(UserAction.Playing));
        });
    }

    [Test]
    public void RawPayloadHasNoArrayLengthPrefix()
    {
        byte[] bytes = [255, 0, 1, 2, 3];
        using MemoryStream stream = packet(20, writer => writer.WriteRawBytes(bytes));
        Assert.That(PacketConvert.Deserialize<PacketErrorReport>(stream, facade)!.Data, Is.EqualTo(bytes));
    }

    [Test]
    public void InvokesCustomReaderWithFacade()
    {
        using MemoryStream stream = packet(0, writer =>
        {
            writer.Write((byte)UserAction.Playing);
            writer.Write("playing");
            writer.Write("hash");
            writer.Write((uint)LegacyMods.Hidden);
            writer.Write((byte)255);
            writer.Write(1234);
        });

        PacketChangeAction? result = PacketConvert.Deserialize<PacketChangeAction>(stream, facade);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Activity.Action, Is.EqualTo(UserAction.Playing));
            Assert.That(result.Activity.InfoText, Is.EqualTo("playing"));
            Assert.That(result.Activity.MapHash, Is.EqualTo("hash"));
            Assert.That(result.Activity.Mode, Is.EqualTo(255));
            Assert.That(result.Activity.MapId, Is.EqualTo(1234));
            Assert.That(result.Activity.Mods, Is.Empty);
        });
    }

    [Test]
    public void ReturnsNullForMismatchedIdAndAllowsRepeatedAttributeReads()
    {
        using MemoryStream mismatch = packet(4);
        Assert.That(PacketConvert.Deserialize<PacketUserId>(mismatch, facade), Is.Null);
        for (int i = 0; i < 3; i++)
        {
            using MemoryStream stream = packet(5, writer => writer.Write(-8));
            Assert.That(PacketConvert.Deserialize<PacketUserId>(stream, facade)!.UserId, Is.EqualTo(-8));
        }

        Assert.That(OsuPacketAttribute.Packets[5].Type, Is.EqualTo(typeof(PacketUserId)));
    }

    [Test]
    public void ReadsAllSuccessfulPacketsAcrossUnknownAndInvalidPayloads()
    {
        using MemoryStream stream = new();
        foreach (MemoryStream source in new[]
                 {
                     packet(4), packet(60000, writer => writer.Write(123)),
                     packet(5, writer => writer.Write((byte)1)),
                     packet(5, writer => writer.Write(42)), packet(8)
                 })
        {
            using (source)
            {
                source.CopyTo(stream);
            }
        }

        stream.WriteByte(0); // An incomplete final header must preserve earlier successes.
        stream.Position = 0;
        IPacket[] result = PacketConvert.DeserializeAll(stream, facade);
        Type[] expected = [typeof(PacketPing), typeof(PacketUserId), typeof(PacketPong)];
        Assert.That(result.Select(p => p.GetType()), Is.EqualTo(expected));
        Assert.That(((PacketUserId)result[1]).UserId, Is.EqualTo(42));
    }

    [Test]
    public void ReadsSinglePacketByIdAndReturnsNullForUnknownId()
    {
        using MemoryStream stream = packet(5, writer => writer.Write(123));
        Assert.That(((PacketUserId)PacketConvert.Deserialize(stream, facade)!).UserId, Is.EqualTo(123));
        using MemoryStream unknown = packet(60000);
        Assert.That(PacketConvert.Deserialize(unknown, facade), Is.Null);
    }

    [Test]
    public void RejectsTruncatedHeadersAndPayloads()
    {
        using MemoryStream valid = packet(5, writer => writer.Write(42));
        byte[] bytes = valid.ToArray();
        for (int length = 0; length < bytes.Length; length++)
        {
            using MemoryStream stream = new(bytes[..length]);
            Assert.That(PacketConvert.Deserialize<PacketUserId>(stream, facade), Is.Null);
        }

        using MemoryStream oversized = new([5, 0, 0, 255, 255, 255, 255]);
        Assert.That(PacketConvert.Deserialize<PacketUserId>(oversized, facade), Is.Null);
    }

    [Test]
    public void RejectsInvalidStringsArraysAndTrailingPayloadBytes()
    {
        using MemoryStream invalidString = packet(63, writer => writer.WriteRawBytes([11, 128, 128, 128, 128, 128]));
        Assert.That(PacketConvert.Deserialize<PacketChannelJoin>(invalidString, facade), Is.Null);
        using MemoryStream invalidArray = packet(85, writer => writer.Write((ushort)2));
        Assert.That(PacketConvert.Deserialize<PacketUserStatsRequest>(invalidArray, facade), Is.Null);
        using MemoryStream shortBytes = packet(50000, writer =>
        {
            writer.Write(100);
            writer.Write((byte)1);
        });
        Assert.That(PacketConvert.Deserialize<ArrayPacket>(shortBytes, facade), Is.Null);
        using MemoryStream extraPayload = packet(4, writer => writer.Write((byte)1));
        Assert.That(PacketConvert.Deserialize<PacketPing>(extraPayload, facade), Is.Null);
    }

    [Test]
    public void SupportsNonSeekableStreamsAndShortReadsWithoutClosingInput()
    {
        using MemoryStream source = packet(5, writer => writer.Write(9876));
        using ShortReadStream stream = new(source.ToArray());
        Assert.That(((PacketUserId)PacketConvert.Deserialize(stream, facade)!).UserId, Is.EqualTo(9876));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(PacketConvert.DeserializeAll(stream, facade), Is.Empty);
    }

    private static MemoryStream packet(ushort id, Action<SerializationWriter>? write = null)
    {
        using MemoryStream payload = new();
        using (SerializationWriter writer = new(payload, leaveOpen: true))
        {
            write?.Invoke(writer);
        }

        MemoryStream result = new();
        using (BinaryWriter writer = new(result, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(id);
            writer.Write((byte)0);
            writer.Write((uint)payload.Length);
            writer.Write(payload.ToArray());
        }

        result.Position = 0;
        return result;
    }

    [OsuPacket(50000)]
    public class ArrayPacket : IPacket
    {
        public byte[] Bytes { get; set; } = [];
        public char[] Characters { get; set; } = [];
        public UserAction Action { get; set; }
    }

    private sealed class ShortReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 2));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}