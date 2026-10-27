// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using g0v0.Server.Stable.Packets;

namespace g0v0.Server.Stable.Tests;

public class BanchoPacketCompatibilityTests
{
    [TestCaseSource(nameof(BanchoCases))]
    public void MatchesOriginalBanchoPyExpectedBytes(string typeName, string packetJson, string expectedHex)
    {
        Type type = typeof(IPacket).Assembly.GetType($"g0v0.Server.Stable.Packets.Server.{typeName}", throwOnError: true)!;
        IPacket packet = (IPacket)JsonSerializer.Deserialize(packetJson, type)!;
        Assert.That(PacketConvert.Serialize(packet), Is.EqualTo(Convert.FromHexString(expectedHex)));
    }

    [TestCaseSource(nameof(SupplementalCases))]
    public void MatchesConditionalLayoutsFromBanchoPyReaderAndProtocol(string typeName, string packetJson, string expectedHex)
    {
        MatchesOriginalBanchoPyExpectedBytes(typeName, packetJson, expectedHex);
    }

    [TestCaseSource(nameof(ReaderCases))]
    public void ReadsBanchoPyPayloadsIntoClientPackets(string typeName, string packetJson, string expectedHex)
    {
        Type type = typeof(IPacket).Assembly.GetType($"g0v0.Server.Stable.Packets.Client.{typeName}", throwOnError: true)!;
        object expected = JsonSerializer.Deserialize(packetJson, type)!;
        using MemoryStream stream = new(Convert.FromHexString(expectedHex));
        IPacket? actual = PacketConvert.Deserialize(stream, new PacketFacade());
        Assert.That(actual, Is.TypeOf(type));
        Assert.That(JsonSerializer.Serialize(actual, type), Is.EqualTo(JsonSerializer.Serialize(expected, type)));
    }

    private static IEnumerable<TestCaseData> BanchoCases()
    {
        return Cases("Cases");
    }

    private static IEnumerable<TestCaseData> SupplementalCases()
    {
        return Cases("SupplementalCases");
    }

    private static IEnumerable<TestCaseData> ReaderCases()
    {
        return Cases("ReaderCases");
    }

    private static IEnumerable<TestCaseData> Cases(string section)
    {
        using Stream stream = typeof(BanchoPacketCompatibilityTests).Assembly.GetManifestResourceStream(
            "g0v0.Server.Stable.Tests.Fixtures.bancho-packets.json")!;
        using JsonDocument document = JsonDocument.Parse(stream);
        foreach (JsonElement test in document.RootElement.GetProperty(section).EnumerateArray())
        {
            yield return new TestCaseData(
                    test.GetProperty("Type").GetString(),
                    test.GetProperty("Packet").GetRawText(),
                    test.GetProperty("Hex").GetString())
                .SetName($"bancho.py.{section}.{test.GetProperty("Name").GetString()}");
        }
    }
}