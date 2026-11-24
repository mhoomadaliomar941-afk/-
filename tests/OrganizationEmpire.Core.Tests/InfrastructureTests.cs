using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Json;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Persistence;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using Xunit;

namespace OrganizationEmpire.Core.Tests;

public class JsonTests
{
    [Fact]
    public void ParsesAndStringifiesRoundTrip()
    {
        string src = "{\"a\":1,\"b\":[true,false,null],\"c\":\"he said \\\"hi\\\"\\n\\u00e9\",\"d\":-2.5e3,\"e\":{}}";
        var tree = Json.Json.Parse(src);
        Assert.Equal(1L, Json.Json.Get(tree, "a"));
        Assert.Equal(-2500.0, Json.Json.Get(tree, "d"));
        Assert.Equal("he said \"hi\"\né", Json.Json.GetString(tree, "c"));
        var again = Json.Json.Parse(Json.Json.Stringify(tree, true));
        Assert.Equal(Json.Json.Stringify(tree), Json.Json.Stringify(again));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[1,2")]
    [InlineData("{\"a\" 1}")]
    [InlineData("tru")]
    [InlineData("{\"a\":1} x")]
    public void RejectsMalformed(string bad)
    {
        Assert.False(Json.Json.TryParse(bad, out _, out string err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void EscapesControlAndLineSeparators()
    {
        string s = Json.Json.Quote("a\u0001b\u2028c");
        Assert.Equal("\"a\\u0001b\\u2028c\"", s);
    }

    private sealed class Poco
    {
        public string Name { get; set; } = "";
        public FactionType Type { get; set; }
        public List<int> Numbers { get; set; } = new();
        public Dictionary<string, float> Map { get; set; } = new();
        public Dictionary<HqUpgrade, int> EnumMap { get; set; } = new();
        public Vec3 Pos { get; set; }
        public int? Maybe { get; set; }
        public DateTime When { get; set; }
        [JsonIgnore] public string Ignored { get; set; } = "x";
        public int[] Arr { get; set; } = Array.Empty<int>();
    }

    [Fact]
    public void MapperRoundTripsComplexObjects()
    {
        var p = new Poco
        {
            Name = "Families",
            Type = FactionType.Mafia,
            Numbers = { 1, 2, 3 },
            Map = { ["x"] = 1.5f },
            EnumMap = { [HqUpgrade.Garage] = 2 },
            Pos = new Vec3(1, -2, 3.5f),
            Maybe = 7,
            When = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Ignored = "should not persist",
            Arr = new[] { 9, 8 },
        };
        string json = JsonMapper.Serialize(p);
        Assert.DoesNotContain("Ignored", json);
        Assert.Contains("\"Mafia\"", json);
        var back = JsonMapper.Deserialize<Poco>(json);
        Assert.Equal("Families", back.Name);
        Assert.Equal(FactionType.Mafia, back.Type);
        Assert.Equal(new[] { 1, 2, 3 }, back.Numbers);
        Assert.Equal(1.5f, back.Map["x"]);
        Assert.Equal(2, back.EnumMap[HqUpgrade.Garage]);
        Assert.Equal(-2f, back.Pos.Y);
        Assert.Equal(7, back.Maybe);
        Assert.Equal(p.When, back.When.ToUniversalTime());
        Assert.Equal("x", back.Ignored);
        Assert.Equal(new[] { 9, 8 }, back.Arr);
    }

    [Fact]
    public void MapperToleratesUnknownAndMissingMembers()
    {
        var back = JsonMapper.Deserialize<Poco>("{\"Name\":\"A\",\"Unknown\":{\"deep\":[1]},\"Type\":\"NotAType\"}");
        Assert.Equal("A", back.Name);
        Assert.Equal(FactionType.StreetGang, back.Type);
        Assert.NotNull(back.Numbers);
    }
}

public class ConfigTests
{
    [Fact]
    public void IniRoundTripAndBinding()
    {
        var cfg = new ModConfig();
        cfg.Hotkeys.OpenMenu = "F9";
        cfg.AI.Provider = AiProviderKind.Anthropic;
        cfg.AI.Temperature = 0.3f;
        cfg.Spawning.MaxDeployedMembers = 20;
        string text = IniFile.Write(cfg, ModConfig.Header);
        Assert.Contains("[AI]", text);
        Assert.Contains("; Options: Offline, OpenAICompatible, Anthropic, Gemini", text);

        var loaded = new ModConfig();
        var warnings = IniFile.Parse(text).BindTo(loaded);
        Assert.Empty(warnings);
        Assert.Equal("F9", loaded.Hotkeys.OpenMenu);
        Assert.Equal(AiProviderKind.Anthropic, loaded.AI.Provider);
        Assert.Equal(0.3f, loaded.AI.Temperature, 3);
        Assert.Equal(20, loaded.Spawning.MaxDeployedMembers);
    }

    [Fact]
    public void BadValuesProduceWarningsAndSanitizeClamps()
    {
        var ini = IniFile.Parse("[Spawning]\nMaxDeployedMembers = 999\nMaxMissionHostiles = abc\n[AI]\nProvider = Bogus\n");
        var cfg = new ModConfig();
        var warnings = ini.BindTo(cfg);
        Assert.Equal(2, warnings.Count);
        cfg.Sanitize();
        Assert.Equal(30, cfg.Spawning.MaxDeployedMembers);
    }

    [Fact]
    public void LoadOrCreateWritesDefaults()
    {
        string dir = TestHelpers.TempDir();
        string path = Path.Combine(dir, "cfg.ini");
        var cfg = ModConfig.LoadOrCreate(path, out var warnings);
        Assert.True(File.Exists(path));
        Assert.Equal("F7", cfg.Hotkeys.OpenMenu);
        Assert.Single(warnings);
    }
}

public class LogTests
{
    [Fact]
    public void RedactsRegisteredSecretsAndKeyShapes()
    {
        SecretRedactor.Register("my-super-secret-value-123");
        string r = SecretRedactor.Redact("key=my-super-secret-value-123 and sk-ant-abcdefghijklmnop1234 and Bearer abcdefghijklmnopqrstuvwxyz");
        Assert.DoesNotContain("my-super-secret-value-123", r);
        Assert.DoesNotContain("sk-ant-abcdefghijklmnop1234", r);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", r);
        Assert.Contains("hidden", r);
    }

    [Fact]
    public void LogFileNeverContainsSecret()
    {
        string dir = TestHelpers.TempDir();
        string path = Path.Combine(dir, "test.log");
        Log.Initialize(path, LogLevel.Debug);
        SecretRedactor.Register("zz-top-secret-key-998877");
        Log.Info("Using key zz-top-secret-key-998877 now");
        Log.Initialize(Path.Combine(dir, "other.log"), LogLevel.Info);
        Assert.DoesNotContain("zz-top-secret-key-998877", File.ReadAllText(path));
    }
}

public class UtilTests
{
    [Fact]
    public void RngIsDeterministic()
    {
        var a = new Rng(5); var b = new Rng(5);
        for (int i = 0; i < 50; i++) Assert.Equal(a.Next(1000), b.Next(1000));
        Assert.Equal(Rng.StableHash("abc"), Rng.StableHash("abc"));
    }

    [Fact]
    public void SafeForGameStripsFormattingCodes()
    {
        Assert.Equal("-r-red (b)", TextUtil.SafeForGame("~r~red <b>"));
        Assert.Equal("\"Hi\" - ok...", TextUtil.SafeForGame("“Hi” — ok…"));
    }

    [Fact]
    public void RgbParses()
    {
        Assert.True(Rgb.TryParse("#B01E23", out var c));
        Assert.Equal(176, c.R);
        Assert.Equal("#B01E23", c.ToHex());
        Assert.False(Rgb.TryParse("nope", out _));
    }
}

public class SaveManagerTests
{
    [Fact]
    public void SaveLoadRoundTripPreservesFaction()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia, 6);
        ctx.Faction.Resources.Cash = 12345;
        ctx.Faction.Members[0].Opinions["x"] = 12;
        ctx.State.Memories["m1"] = new NpcMemory { NpcId = "m1", Name = "A", Entries = { new MemoryEntry { Text = "hello" } } };
        var sm = new SaveManager(TestHelpers.TempDir());
        Assert.True(sm.Save(ctx.State, "My Slot!").Ok);
        var loaded = sm.Load("my_slot", out string msg);
        Assert.NotNull(loaded);
        Assert.Equal(12345, loaded!.Faction.Resources.Cash);
        Assert.Equal(6, loaded.Faction.Members.Count);
        Assert.Equal(FactionType.Militia, loaded.Faction.Type);
        Assert.Equal("hello", loaded.Memories["m1"].Entries[0].Text);
        Assert.Equal(ctx.World.Territories.Count, loaded.World.Territories.Count);
        Assert.Single(sm.ListSlots());
        Assert.Equal("Test Militia", sm.ListSlots()[0].FactionName);
    }

    [Fact]
    public void CorruptSaveFallsBackToBackup()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia);
        string dir = TestHelpers.TempDir();
        var sm = new SaveManager(dir);
        ctx.Faction.Resources.Cash = 1;
        sm.Save(ctx.State, "slot");
        ctx.Faction.Resources.Cash = 2;
        sm.Save(ctx.State, "slot");
        File.WriteAllText(Path.Combine(dir, "slot.json"), "{ broken");
        var loaded = sm.Load("slot", out string msg);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Faction.Resources.Cash);
        Assert.Contains("damaged", msg);
    }

    [Fact]
    public void ValidateRepairsDanglingReferences()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 3);
        ctx.Faction.Members[0].RankId = "does-not-exist";
        ctx.Faction.Members[1].SquadId = "nope";
        ctx.Faction.Squads[0].MemberIds.Add("ghost");
        Simulation.Validate(ctx.State);
        Assert.NotNull(ctx.Faction.GetRank(ctx.Faction.Members[0].RankId));
        Assert.NotEqual("nope", ctx.Faction.Members[1].SquadId);
        Assert.DoesNotContain("ghost", ctx.Faction.Squads[0].MemberIds);
    }
}
