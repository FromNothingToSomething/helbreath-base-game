using Google.Protobuf;
using Mmorpg.Network;
using Xunit;

namespace NocthemarTests;

/// <summary>Documents the encoder rule the round trip relies on: C# never writes an empty repeated field (ts-proto does for packed ones, which decode.ts tolerates).</summary>
public class ProtoCanonicalTests {
    [Fact]
    public void Empty_repeated_fields_are_not_written() {
        var plain = new NocSnapshot { Level = 7, Mana = 45 };
        var withEmptyLists = new NocSnapshot { Level = 7, Mana = 45 };
        withEmptyLists.Skills.AddRange([]);
        withEmptyLists.MimicEquipped.AddRange([]);
        withEmptyLists.Mimics.AddRange([]);
        withEmptyLists.Reputations.AddRange([]);

        Assert.Equal(plain.ToByteArray(), withEmptyLists.ToByteArray());
        Assert.Equal(new byte[] { 0x08, 0x07, 0x20, 0x2D }, withEmptyLists.ToByteArray());
    }

    [Fact]
    public void An_empty_catalog_is_empty_on_the_wire() {
        Assert.Empty(new NocCatalog().ToByteArray());
        Assert.Empty(new PlayerFactionChanged().ToByteArray());
        Assert.Equal(new byte[] { 0x10, 0x01 }, new PlayerFactionChanged { FactionId = 1 }.ToByteArray());
    }
}
