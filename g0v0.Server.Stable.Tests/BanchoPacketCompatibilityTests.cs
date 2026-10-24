// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using g0v0.Server.Stable.Packets;

namespace g0v0.Server.Stable.Tests;

public class BanchoPacketCompatibilityTests
{
    [TestCaseSource(nameof(banchoCases))]
    public void MatchesOriginalBanchoPyExpectedBytes(string typeName, string packetJson, string expectedHex)
    {
        Type type = typeof(IPacket).Assembly.GetType($"g0v0.Server.Stable.Packets.Server.{typeName}", throwOnError: true)!;
        IPacket packet = (IPacket)JsonSerializer.Deserialize(packetJson, type)!;
        Assert.That(PacketConvert.Serialize(packet), Is.EqualTo(Convert.FromHexString(expectedHex)));
    }

    [TestCaseSource(nameof(supplementalCases))]
    public void MatchesConditionalLayoutsFromBanchoPyReaderAndProtocol(string typeName, string packetJson, string expectedHex)
    {
        MatchesOriginalBanchoPyExpectedBytes(typeName, packetJson, expectedHex);
    }

    [TestCaseSource(nameof(readerCases))]
    public void ReadsBanchoPyPayloadsIntoClientPackets(string typeName, string packetJson, string expectedHex)
    {
        Type type = typeof(IPacket).Assembly.GetType($"g0v0.Server.Stable.Packets.Client.{typeName}", throwOnError: true)!;
        object expected = JsonSerializer.Deserialize(packetJson, type)!;
        using MemoryStream stream = new(Convert.FromHexString(expectedHex));
        IPacket? actual = PacketConvert.Deserialize(stream, new PacketFacade());
        Assert.That(actual, Is.TypeOf(type));
        Assert.That(JsonSerializer.Serialize(actual, type), Is.EqualTo(JsonSerializer.Serialize(expected, type)));
    }

    private static IEnumerable<TestCaseData> banchoCases()
    {
        return cases("Cases");
    }

    private static IEnumerable<TestCaseData> supplementalCases()
    {
        return cases("SupplementalCases");
    }

    private static IEnumerable<TestCaseData> readerCases()
    {
        return cases("ReaderCases");
    }

    private static IEnumerable<TestCaseData> cases(string section)
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