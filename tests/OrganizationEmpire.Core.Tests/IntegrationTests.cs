using OrganizationEmpire.Api;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Integration;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using Xunit;

namespace OrganizationEmpire.Core.Tests;

/// <summary>In-memory GTA V folder for detection tests.</summary>
internal sealed class FakeEnv : IModEnvironment
{
    public readonly Dictionary<string, string> FileText = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Modules = new();
    public readonly List<LoadedAssemblyInfo> Asms = new();
    public string Shvdn = "3.6.0";

    public FakeEnv File(string path, string text = "") { FileText[path] = text; return this; }

    public IEnumerable<string> Files(string relativeFolder, bool recursive)
    {
        string prefix = relativeFolder.Length == 0 ? "" : relativeFolder.TrimEnd('/') + "/";
        foreach (var f in FileText.Keys)
        {
            if (!f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = f.Substring(prefix.Length);
            if (!recursive && rest.Contains('/')) continue;
            yield return f;
        }
    }

    public bool FileExists(string relativePath) => FileText.ContainsKey(relativePath);
    public bool FolderExists(string relativePath) => FileText.Keys.Any(f => f.StartsWith(relativePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    public string? ReadText(string relativePath, int maxBytes = 262144) => FileText.TryGetValue(relativePath, out var t) && t.Length <= maxBytes ? t : null;
    public IEnumerable<string> NativeModules => Modules;
    public IEnumerable<LoadedAssemblyInfo> Assemblies => Asms;
    public string ShvdnVersion => Shvdn;
}

public class ModDetectionTests
{
    private static FakeEnv TypicalSetup()
    {
        var env = new FakeEnv()
            .File("ScriptHookV.dll").File("dinput8.dll").File("ScriptHookVDotNet.asi")
            .File("scripts/OrganizationEmpire.dll")
            .File("Menyoo.asi").File("menyooStuff/Spooner/house.xml", "<SpoonerPlacements/>")
            .File("trainerv.asi").File("trainerv.ini", "[KeyBindings]\nMenuKey=114 //Enter Menu, Default F3\nHideMenuKey=115\n")
            .File("scripts/GangAndTurfMod.dll").File("scripts/LemonUI.SHVDN3.dll");
        env.Modules.AddRange(new[] { "GTA5.exe", "ScriptHookV.dll", "ScriptHookVDotNet.asi", "Menyoo.asi", "trainerv.asi" });
        env.Asms.Add(new LoadedAssemblyInfo { Name = "OrganizationEmpire", Version = "1.0.0", RelativePath = "scripts/OrganizationEmpire.dll", ScriptCount = 1 });
        env.Asms.Add(new LoadedAssemblyInfo { Name = "GangAndTurfMod", Version = "2.1.0", RelativePath = "scripts/GangAndTurfMod.dll", ScriptCount = 3, References = { "LemonUI.SHVDN3" } });
        env.Asms.Add(new LoadedAssemblyInfo { Name = "LemonUI.SHVDN3", Version = "2.0.0", RelativePath = "scripts/LemonUI.SHVDN3.dll" });
        env.Asms.Add(new LoadedAssemblyInfo { Name = "CoolCarMod", Version = "0.9", RelativePath = "scripts/CoolCarMod.dll", ScriptCount = 1, References = { "NativeUI" } });
        return env;
    }

    [Fact]
    public void DetectsKnownModsAndUnknownScripts()
    {
        var scan = ModScanner.Scan(TypicalSetup());
        foreach (var id in new[] { "scripthookv", "shvdn", "asiloader", "menyoo", "simpletrainer", "gangandturf", "lemonui" })
            Assert.True(scan.Has(id), id + " not detected");
        Assert.Equal("2.1.0", scan.Get("gangandturf")!.Version);
        Assert.Contains(scan.Mods, m => m.Id == "script:CoolCarMod" && m.Version == "0.9");
        Assert.DoesNotContain(scan.Mods, m => m.Name == "OrganizationEmpire");
        Assert.DoesNotContain(scan.Mods, m => m.Id == "script:GangAndTurfMod"); // claimed by the catalog entry
        Assert.False(scan.Has("lspdfr"));
    }

    [Fact]
    public void HotkeyConflictWithMenyooOffersASpareKey()
    {
        var env = TypicalSetup();
        var scan = ModScanner.Scan(env);
        var cfg = new ModConfig();
        CompatibilityAnalyzer.Analyze(scan, env, cfg);
        var clash = scan.Issues.Single(i => i.Kind == IssueKind.HotkeyConflict && i.ModIds.Contains("menyoo"));
        Assert.Equal("rebind:QuickOrders:F9", clash.Fix);
        Assert.True(CompatibilityAnalyzer.ApplyRebind(cfg, clash.Fix));
        Assert.Equal("F9", cfg.Hotkeys.QuickOrders);
        CompatibilityAnalyzer.Analyze(scan, env, cfg);
        Assert.DoesNotContain(scan.Issues, i => i.Kind == IssueKind.HotkeyConflict && i.ModIds.Contains("menyoo"));
    }

    [Fact]
    public void TrainerKeysAreReadFromItsOwnConfig()
    {
        var env = TypicalSetup().File("trainerv.ini", "[KeyBindings]\nMenuKey=118 //F7\nHideMenuKey=115\n");
        var scan = ModScanner.Scan(env);
        CompatibilityAnalyzer.Analyze(scan, env, new ModConfig());
        Assert.Contains(scan.Issues, i => i.Kind == IssueKind.HotkeyConflict && i.ModIds.Contains("simpletrainer") && i.Text.Contains("OpenMenu"));
    }

    [Fact]
    public void GangModTurnsCoexistenceOnInAutoMode()
    {
        var env = TypicalSetup();
        var scan = ModScanner.Scan(env);
        var cfg = new ModConfig();
        Assert.True(CompatibilityAnalyzer.Coexistence(scan, cfg));
        cfg.Integrations.Coexistence = CoexistenceMode.Off;
        CompatibilityAnalyzer.Analyze(scan, env, cfg);
        var overlap = scan.Issues.Single(i => i.Kind == IssueKind.Overlap && i.ModIds.Contains("gangandturf"));
        Assert.Equal(IssueSeverity.Warning, overlap.Severity);
        Assert.Equal("coexistence:On", overlap.Fix);
        Assert.False(CompatibilityAnalyzer.Coexistence(ModScanner.Scan(new FakeEnv()), new ModConfig()));
    }

    [Fact]
    public void ReportsMissingDependenciesDuplicatesAndOldFramework()
    {
        var env = TypicalSetup();
        env.FileText.Remove("scripts/LemonUI.SHVDN3.dll");
        env.Asms.RemoveAll(a => a.Name == "LemonUI.SHVDN3");
        env.File("scripts/OrganizationEmpire (1).dll");
        env.Shvdn = "3.5.1";
        var scan = ModScanner.Scan(env);
        CompatibilityAnalyzer.Analyze(scan, env, new ModConfig());
        Assert.Contains(scan.Issues, i => i.Kind == IssueKind.MissingDependency && i.Text.Contains("Gang and Turf") && i.Text.Contains("LemonUI"));
        Assert.Contains(scan.Issues, i => i.Kind == IssueKind.MissingDependency && i.Text.Contains("CoolCarMod") && i.Text.Contains("NativeUI"));
        Assert.Contains(scan.Issues, i => i.Kind == IssueKind.Duplicate && i.Severity == IssueSeverity.Error);
        Assert.Contains(scan.Issues, i => i.Kind == IssueKind.Version);
    }

    [Fact]
    public void BrokenEnvironmentNeverThrows()
    {
        var scan = ModScanner.Scan(new ThrowingEnv());
        CompatibilityAnalyzer.Analyze(scan, new ThrowingEnv(), new ModConfig());
        Assert.Empty(scan.Mods);
    }

    private sealed class ThrowingEnv : IModEnvironment
    {
        public IEnumerable<string> Files(string relativeFolder, bool recursive) => throw new IOException("denied");
        public bool FileExists(string relativePath) => throw new IOException("denied");
        public bool FolderExists(string relativePath) => throw new IOException("denied");
        public string ReadText(string relativePath, int maxBytes = 262144) => throw new IOException("denied");
        public IEnumerable<string> NativeModules => throw new IOException("denied");
        public IEnumerable<LoadedAssemblyInfo> Assemblies => throw new IOException("denied");
        public string ShvdnVersion => null!;
    }

    [Theory]
    [InlineData("114", "F3")]
    [InlineData("0x77", "F8")]
    [InlineData("f8", "F8")]
    [InlineData("D1", "1")]
    [InlineData("Next", "PageDown")]
    [InlineData("e", "E")]
    public void NormalizesKeyNamesAndVirtualKeys(string input, string expected) => Assert.Equal(expected, KeyNames.Normalize(input));

    [Fact]
    public void CatalogEntriesAreWellFormed()
    {
        Assert.Equal(ModCatalog.All.Count, ModCatalog.All.Select(m => m.Id).Distinct().Count());
        foreach (var m in ModCatalog.All)
        {
            Assert.NotEmpty(m.Signatures);
            Assert.False(string.IsNullOrWhiteSpace(m.Integration));
            Assert.False(string.IsNullOrWhiteSpace(m.License));
            foreach (var dep in m.Requires) Assert.NotNull(ModCatalog.Get(dep));
        }
    }
}

public class IntegrationHostTests
{
    private sealed class TestModule : IntegrationModule
    {
        public string MyId = "test";
        public string[] Needs = new string[0];
        public bool ThrowOnTick, ThrowOnStart;
        public int Ticks, Starts, Stops;
        public override string Id => MyId;
        public override string Name => "Test";
        public override string Description => "Test module";
        public override IntegrationLevel Level => IntegrationLevel.Supported;
        public override IEnumerable<string> RequiresAny => Needs;
        public override void Start(ScanResult scan) { Starts++; if (ThrowOnStart) throw new InvalidOperationException("boom key=sk-secret-123456789012345678"); }
        public override void Tick() { Ticks++; if (ThrowOnTick) throw new InvalidOperationException("tick failed"); }
        public override void Stop() { Stops++; }
    }

    [Fact]
    public void FailingIntegrationIsIsolatedAndSwitchedOff()
    {
        var cfg = new ModConfig();
        var host = new IntegrationHost(() => cfg);
        var bad = new TestModule { MyId = "bad", ThrowOnTick = true };
        var good = new TestModule { MyId = "good" };
        host.Register(bad);
        host.Register(good);
        host.Apply(new ScanResult());
        for (int i = 0; i < 5; i++) host.Tick();
        Assert.Equal(IntegrationStatus.Failed, host.Get("bad")!.Status);
        Assert.Equal(IntegrationHost.MaxFailures, bad.Ticks);
        Assert.Equal(1, bad.Stops);
        Assert.Equal(IntegrationStatus.Active, host.Get("good")!.Status);
        Assert.Equal(5, good.Ticks);
        host.Reset("bad");
        bad.ThrowOnTick = false;
        host.Apply(new ScanResult());
        Assert.Equal(IntegrationStatus.Active, host.Get("bad")!.Status);
    }

    [Fact]
    public void StartErrorsAreRedactedAndFailImmediately()
    {
        var cfg = new ModConfig();
        var host = new IntegrationHost(() => cfg);
        SecretRedactor.Register("sk-secret-123456789012345678");
        host.Register(new TestModule { MyId = "s", ThrowOnStart = true });
        host.Apply(new ScanResult());
        var st = host.Get("s")!;
        Assert.Equal(IntegrationStatus.Failed, st.Status);
        Assert.DoesNotContain("sk-secret-123456789012345678", st.Reason);
        Assert.DoesNotContain(host.RecentLog, l => l.Contains("sk-secret-123456789012345678"));
    }

    [Fact]
    public void DependenciesAndUserChoiceDecideAvailability()
    {
        var cfg = new ModConfig();
        var host = new IntegrationHost(() => cfg);
        var phone = new TestModule { MyId = "phone", Needs = new[] { "ifruitaddon2" } };
        host.Register(phone);
        host.Apply(new ScanResult());
        Assert.Equal(IntegrationStatus.Unavailable, host.Get("phone")!.Status);
        var scan = new ScanResult();
        scan.Mods.Add(new DetectedMod { Id = "ifruitaddon2", Name = "iFruitAddon2" });
        host.Apply(scan);
        Assert.Equal(IntegrationStatus.Active, host.Get("phone")!.Status);
        host.SetEnabledByUser("phone", false);
        host.Apply(scan);
        Assert.Equal(IntegrationStatus.Disabled, host.Get("phone")!.Status);
        Assert.Equal(1, phone.Stops);
        Assert.Contains("phone", cfg.Integrations.DisabledIntegrations);
        cfg.Integrations.Enabled = false;
        host.SetEnabledByUser("phone", true);
        host.Apply(scan);
        Assert.Equal(IntegrationStatus.Disabled, host.Get("phone")!.Status);
    }
}

public class MapLocationTests
{
    private const string MapEditorXml = @"<?xml version=""1.0""?>
<Map xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <Objects>
    <MapObject><Type>Prop</Type><Position><X>100</X><Y>200</Y><Z>30</Z></Position><Rotation><X>0</X><Y>0</Y><Z>90</Z></Rotation><Hash>123</Hash></MapObject>
    <MapObject><Type>Prop</Type><Position><X>102</X><Y>204</Y><Z>31</Z></Position></MapObject>
    <MapObject><Type>Prop</Type><Position><X>104</X><Y>202</Y><Z>29</Z></Position></MapObject>
    <MapObject><Type>Prop</Type><Position><X>9000</X><Y>-9000</Y><Z>30</Z></Position></MapObject>
  </Objects>
</Map>";

    private const string MenyooXml = @"<SpoonerPlacements>
  <ReferenceCoords><X>-1000.5</X><Y>-2000.25</Y><Z>12</Z></ReferenceCoords>
  <Placement><ModelHash>0x1</ModelHash><PositionRotation><X>-990</X><Y>-1990</Y><Z>12</Z><Pitch>0</Pitch></PositionRotation></Placement>
</SpoonerPlacements>";

    [Fact]
    public void MapEditorCenterIsTheMedianOfObjects()
    {
        var loc = MapLocations.Parse(MapEditorXml, "scripts/MapEditor/club.xml")!;
        Assert.Equal("Map Editor", loc.Format);
        Assert.Equal("club", loc.Name);
        Assert.Equal(4, loc.ObjectCount);
        Assert.InRange(loc.Center.X, 101, 104);
        Assert.InRange(loc.Center.Y, 200, 204);
    }

    [Fact]
    public void MenyooUsesItsReferenceCoordinates()
    {
        var loc = MapLocations.Parse(MenyooXml, "menyooStuff/Spooner/hq.xml")!;
        Assert.Equal("Menyoo", loc.Format);
        Assert.Equal(-1000.5f, loc.Center.X, 2);
        Assert.Equal(-2000.25f, loc.Center.Y, 2);
    }

    [Fact]
    public void BadFilesAreIgnored()
    {
        Assert.Null(MapLocations.Parse("<Map><broken", "a.xml"));
        Assert.Null(MapLocations.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><Map>&e;</Map>", "b.xml"));
        Assert.Null(MapLocations.Parse("<Map/>", "c.xml"));
    }

    [Fact]
    public void FindsMapsInKnownAndExtraFolders()
    {
        var env = new FakeEnv()
            .File("scripts/MapEditor/AutoloadMaps/club.xml", MapEditorXml)
            .File("menyooStuff/Spooner/hq.xml", MenyooXml)
            .File("mymaps/base.xml", MapEditorXml)
            .File("scripts/MapEditor/readme.txt", "hi");
        var found = MapLocations.Find(env, new[] { "mymaps" });
        Assert.Equal(3, found.Count);
    }
}

[CollectionDefinition("ApiHub", DisableParallelization = true)]
public class ApiHubCollection { }

[Collection("ApiHub")]
public class PublicApiTests
{
    private static SimContext Attach(bool allowCommands)
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang);
        ctx.Config.Integrations.AllowExternalCommands = allowCommands;
        var notes = new List<string>();
        ApiHub.Attach(() => ctx.State, () => ctx.Config, "test", h => h == 42, null, notes.Add);
        return ctx;
    }

    [Fact]
    public void QueriesReflectTheGame()
    {
        var ctx = Attach(false);
        try
        {
            Assert.True(EmpireApi.IsReady);
            Assert.Equal(ctx.Faction!.Name, EmpireApi.FactionName);
            Assert.True(EmpireApi.IsFactionMember(42));
            Assert.False(EmpireApi.IsFactionMember(7));
            Assert.Equal("ballas", EmpireApi.GetTerritoryByZone("DAVIS")!.OwnerId);
            Assert.Contains(EmpireApi.GetRivals(), r => r.Id == "families");
        }
        finally { ApiHub.Detach(); }
    }

    [Fact]
    public void CommandsNeedPermission()
    {
        var ctx = Attach(false);
        try
        {
            Assert.False(EmpireApi.RegisterFaction(new ExternalFactionSpec { Id = "nightowls", Name = "Night Owls" }, out string error));
            Assert.Contains("disabled", error);
            Assert.False(EmpireApi.ShowNotification("hello"));
        }
        finally { ApiHub.Detach(); }
    }

    [Fact]
    public void RegisteredFactionJoinsTheLivingWorld()
    {
        var ctx = Attach(true);
        try
        {
            Assert.True(EmpireApi.RegisterFaction(new ExternalFactionSpec { Id = "night-owls!", Name = "Night Owls", PedModels = new[] { "g_m_y_mexgoon_01" }, HomeTerritories = new[] { "lamesa", "airport" } }, out _));
            Assert.Null(ctx.World.GetRival("ext_nightowls"));   // queued until the mod's tick
            Assert.Equal(1, ApiHub.ProcessQueue());
            var r = ctx.World.GetRival("ext_nightowls")!;
            Assert.True(r.External);
            Assert.Equal(new[] { "lamesa" }, r.HomeTerritories);   // strategic sites are refused
            Simulation.Tick(ctx, null);
            Assert.NotNull(r.Leader);
        }
        finally { ApiHub.Detach(); }
    }

    [Fact]
    public void ThrowingSubscriberIsUnsubscribedAndOthersStillReceive()
    {
        var ctx = Attach(false);
        int good = 0, bad = 0;
        Action<ApiNews> goodHandler = n => good++;
        Action<ApiNews> badHandler = n => { bad++; throw new InvalidOperationException("nope"); };
        EmpireApi.NewsPosted += goodHandler;
        EmpireApi.NewsPosted += badHandler;
        try
        {
            for (int i = 0; i < 5; i++) LivingWorldSystem.News(ctx, WorldNewsKind.Economy, "news " + i, false, null);
            Assert.Equal(5, good);
            Assert.Equal(ApiHub.MaxHandlerFailures, bad);
        }
        finally
        {
            EmpireApi.NewsPosted -= goodHandler;
            EmpireApi.NewsPosted -= badHandler;
            ApiHub.Detach();
        }
    }
}
