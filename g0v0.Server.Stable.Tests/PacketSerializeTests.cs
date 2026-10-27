// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// ReSharper disable AccessToDisposedClosure
using g0v0.Server.Stable.Attributes;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Packets.Server;
using osu.Game.IO.Legacy;

namespace g0v0.Server.Stable.Tests;

public class PacketSerializeTests
{
    [Test]
    public void WritesLittleEndianHeaderAndSignedIntegerPayload()
    {
        byte[] expected = [5, 0, 0, 4, 0, 0, 0, 248, 255, 255, 255];
        Assert.That(PacketConvert.Serialize(new PacketUserId { UserId = -8 }), Is.EqualTo(expected));
    }

    [Test]
    public void WritesEmptyPayload()
    {
        byte[] expected = [8, 0, 0, 0, 0, 0, 0];
        Assert.That(PacketConvert.Serialize(new PacketPong()), Is.EqualTo(expected));
    }

    [Test]
    public void ExpandsNestedClassInPlaceWithoutAnExtraHeaderOrLength()
    {
        PacketSendMessage packet = new()
        {
            Message = new Message { Sender = "A", Text = "你", Recipient = string.Empty, SenderId = 1 }
        };
        byte[] expected = [7, 0, 0, 13, 0, 0, 0, 11, 1, 65, 11, 3, 228, 189, 160, 0, 1, 0, 0, 0];
        byte[] bytes = PacketConvert.Serialize(packet);
        Assert.That(bytes, Is.EqualTo(expected));
        using MemoryStream stream = new(bytes);
        PacketSendMessage? result = PacketConvert.Deserialize<PacketSendMessage>(stream, new PacketFacade());
        Assert.That(result!.Message.Text, Is.EqualTo("你"));
    }

    [Test]
    public void WritesNullAndEmptyOrdinaryStringsAsTheAbsentMarker()
    {
        byte[] nullExpected = [24, 0, 0, 1, 0, 0, 0, 0];
        Assert.Multiple(() =>
        {
            Assert.That(PacketConvert.Serialize(new PacketNotification { Message = null! }), Is.EqualTo(nullExpected));
            Assert.That(PacketConvert.Serialize(new PacketNotification { Message = string.Empty }), Is.EqualTo(nullExpected));
        });
    }

    [Test]
    public void WritesArraysWithUnsignedShortCounts()
    {
        byte[] expected = [72, 0, 0, 10, 0, 0, 0, 2, 0, 255, 255, 255, 255, 42, 0, 0, 0];
        Assert.That(PacketConvert.Serialize(new PacketFriendsList { Friends = [-1, 42] }), Is.EqualTo(expected));
    }

    [Test]
    public void WritesRawDataWithoutAnArrayLengthPrefix()
    {
        byte[] expected = [15, 0, 0, 3, 0, 0, 0, 255, 0, 17];
        Assert.That(PacketConvert.Serialize(new PacketSpectateFrames { Data = [255, 0, 17] }), Is.EqualTo(expected));
    }

    [Test]
    public void WritesByteAndCharArraysAndByteEnumsUsingReaderCompatibleFormats()
    {
        PacketConvertTests.ArrayPacket packet = new() { Bytes = [255], Characters = ['你'], Action = UserAction.Playing };
        byte[] expected = [80, 195, 0, 13, 0, 0, 0, 1, 0, 0, 0, 255, 1, 0, 0, 0, 228, 189, 160, 2];
        Assert.That(PacketConvert.Serialize(packet), Is.EqualTo(expected));
        using MemoryStream stream = new(PacketConvert.Serialize(packet));
        PacketConvertTests.ArrayPacket? result = PacketConvert.Deserialize<PacketConvertTests.ArrayPacket>(stream, new PacketFacade());
        Assert.Multiple(() =>
        {
            Assert.That(result!.Bytes, Is.EqualTo(packet.Bytes));
            Assert.That(result.Characters, Is.EqualTo(packet.Characters));
            Assert.That(result.Action, Is.EqualTo(packet.Action));
        });
    }

    [Test]
    public void WritesEnumsUsingTheirSignedUnderlyingType()
    {
        byte[] expected = [71, 0, 0, 4, 0, 0, 0, 1, 0, 0, 128];
        PacketPrivileges packet = new() { Privileges = unchecked((ClientPrivileges)0x80000001) };
        Assert.That(PacketConvert.Serialize(packet), Is.EqualTo(expected));
    }

    [Test]
    public void SerializeAllKeepsOrderAndEachPacketHeader()
    {
        byte[] expected = [8, 0, 0, 0, 0, 0, 0, 5, 0, 0, 4, 0, 0, 0, 42, 0, 0, 0];
        byte[] bytes = PacketConvert.SerializeAll([new PacketPong(), new PacketUserId { UserId = 42 }]);
        Assert.That(bytes, Is.EqualTo(expected));
        using MemoryStream stream = new(bytes);
        IPacket[] result = PacketConvert.DeserializeAll(stream, new PacketFacade());
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Length.EqualTo(2));
            Assert.That(result[0], Is.TypeOf<PacketPong>());
            Assert.That(((PacketUserId)result[1]).UserId, Is.EqualTo(42));
            Assert.That(PacketConvert.SerializeAll([]), Is.Empty);
        });
    }

    [Test]
    public void StreamOverloadsWriteAtCurrentPositionAndLeaveOutputOpen()
    {
        using MemoryStream stream = new();
        stream.WriteByte(99);
        PacketConvert.Serialize(stream, new PacketPong());
        PacketConvert.SerializeAll(stream, [new PacketUserId { UserId = 42 }]);
        byte[] expected = [99, 8, 0, 0, 0, 0, 0, 0, 5, 0, 0, 4, 0, 0, 0, 42, 0, 0, 0];
        Assert.Multiple(() =>
        {
            Assert.That(stream.CanWrite, Is.True);
            Assert.That(stream.ToArray(), Is.EqualTo(expected));
        });
    }

    [Test]
    public void InvalidArrayDoesNotWriteAPartialPacket()
    {
        using MemoryStream stream = new();
        stream.WriteByte(99);
        Assert.Throws<OverflowException>(() => PacketConvert.Serialize(stream, new PacketFriendsList { Friends = new int[ushort.MaxValue + 1] }));
        byte[] expected = [99];
        Assert.That(stream.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void CallsCustomWriterOnlyWhenExplicitlyMarked()
    {
        byte[] expected = [81, 195, 0, 1, 0, 0, 0, 42];
        Assert.That(PacketConvert.Serialize(new CustomWritePacket { Value = 42 }), Is.EqualTo(expected));
    }

    [OsuPacket(50001, hasCustomWriter: true)]
    public class CustomWritePacket : IPacket
    {
        public int Value { get; set; }

        public void Write(SerializationWriter writer)
        {
            writer.Write(checked((byte)Value));
        }
    }
}