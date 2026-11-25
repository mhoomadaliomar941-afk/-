using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Persistence;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using Xunit;

namespace OrganizationEmpire.Core.Tests;

public class LivingWorldTests
{
    private static SimContext WorldOnly(int seed = 7)
    {
        var state = Simulation.NewGame(seed);
        return new SimContext(state, new ModConfig(), new Rng(seed));
    }

    [Fact]
    public void EveryRivalGetsALeaderLieutenantsAndRelations()
    {
        var ctx = WorldOnly();
        LivingWorldSystem.EnsureInitialized(ctx);
        foreach (var r in ctx.World.Rivals)
        {
            Assert.NotNull(r.Leader);
            Assert.False(string.IsNullOrEmpty(r.Leader.Name));
            Assert.Equal(2, r.Lieutenants.Count);
        }
        var lw = ctx.World.Living;
        Assert.Equal(RelationStatus.War, lw.Get("ballas", "families")!.Status);
        int n = ctx.World.Rivals.Count;
        Assert.Equal(n * (n - 1) / 2, lw.Relations.Count);
    }

    [Fact]
    public void WorldEvolvesWithoutAPlayerFaction()
    {
        var ctx = WorldOnly(11);
        Assert.Null(ctx.Faction);
        var ownersBefore = ctx.World.Territories.ToDictionary(t => t.Id, t => t.OwnerId);
        for (int h = 0; h < 24 * 14; h++) Simulation.Tick(ctx, null);
        var lw = ctx.World.Living;
        Assert.True(lw.BattlesFought > 0, "two weeks without a single battle");
        Assert.Contains(lw.News, n => n.Kind == WorldNewsKind.Recruitment);
        Assert.True(ctx.World.Territories.Any(t => t.OwnerId != ownersBefore[t.Id]), "no territory changed hands in two weeks");
        Assert.Equal(24 * 14, ctx.World.Tick);
    }

    [Fact]
    public void SameSeedGivesTheSameHistory()
    {
        string Run()
        {
            var ctx = WorldOnly(5);
            for (int h = 0; h < 24 * 5; h++) Simulation.Tick(ctx, null);
            return string.Join("|", ctx.World.Living.News.Select(n => n.Text)) + string.Join(",", ctx.World.Territories.Select(t => t.OwnerId));
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void BattleAgainstAWeakDefenderTakesTheTerritory()
    {
        var ctx = WorldOnly();
        LivingWorldSystem.EnsureInitialized(ctx);
        var ballas = ctx.World.GetRival("ballas")!;
        var families = ctx.World.GetRival("families")!;
        ballas.Strength = 90; ballas.Arms = 90; ballas.Morale = 90;
        families.Strength = 8; families.Arms = 5; families.Morale = 20;
        var strawberry = ctx.World.GetTerritory("strawberry")!;
        var b = LivingWorldSystem.StartBattle(ctx, ballas, strawberry);
        for (int i = 0; i < 12 && b.IsActive; i++) Simulation.Tick(ctx, null);
        Assert.Equal(BattleStatus.AttackerWon, b.Status);
        Assert.Equal("ballas", strawberry.OwnerId);
        Assert.Contains(ctx.World.Living.News, n => n.Kind == WorldNewsKind.Territory && n.TerritoryId == "strawberry");
    }

    [Fact]
    public void LiveCasualtiesDecideTheBattleAndTheFavoredSideRemembersThePlayer()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang);
        LivingWorldSystem.EnsureInitialized(ctx);
        var ballas = ctx.World.GetRival("ballas")!;
        var families = ctx.World.GetRival("families")!;
        float familiesHostility = families.Hostility, ballasHostility = ballas.Hostility;
        var b = LivingWorldSystem.StartBattle(ctx, ballas, ctx.World.GetTerritory("strawberry")!);
        // The player kills the attackers in the street.
        LivingWorldSystem.ReportLiveCasualties(ctx, b.Id, true, (int)Math.Ceiling(b.AttackerForce), true);
        Assert.Equal(BattleStatus.DefenderWon, b.Status);
        Assert.Equal("defender", b.PlayerSide);
        Assert.True(families.Hostility < familiesHostility);
        Assert.True(ballas.Hostility > ballasHostility);
    }

    [Fact]
    public void AlliancesFormAgainstACommonEnemy()
    {
        var ctx = WorldOnly(3);
        LivingWorldSystem.EnsureInitialized(ctx);
        var lw = ctx.World.Living;
        // Vagos are at war with the Aztecas; make the Marabunta hate the Aztecas too and like the Vagos.
        lw.Get("marabunta", "aztecas")!.Hostility = 80;
        lw.Get("marabunta", "aztecas")!.Status = RelationStatus.War;
        lw.Get("marabunta", "vagos")!.Status = RelationStatus.Neutral;
        lw.Get("marabunta", "vagos")!.Hostility = 5;
        var vagos = ctx.World.GetRival("vagos")!;
        vagos.Leader!.Charisma = 1f;
        ctx.World.GetRival("marabunta")!.Leader!.Caution = 1f;
        var options = LivingWorldSystem.ScoreOptions(ctx, vagos);
        Assert.Contains(options, o => o.Goal == RivalGoal.SeekAllies && o.Target == "marabunta");
        bool allied = false;
        for (int i = 0; i < 20 && !allied; i++)
        {
            LivingWorldSystem.Execute(ctx, vagos, RivalGoal.SeekAllies, "marabunta");
            allied = lw.Get("vagos", "marabunta")!.Status == RelationStatus.Alliance;
            if (!allied) lw.Get("vagos", "marabunta")!.Hostility = 5;
        }
        Assert.True(allied);
    }

    [Fact]
    public void LosingWarsLeadToPeaceTalks()
    {
        var ctx = WorldOnly(9);
        LivingWorldSystem.EnsureInitialized(ctx);
        var families = ctx.World.GetRival("families")!;
        var ballas = ctx.World.GetRival("ballas")!;
        families.Strength = 10;
        ballas.Strength = 80;
        families.Leader!.Caution = 1f;
        var options = LivingWorldSystem.ScoreOptions(ctx, families);
        Assert.Contains(options, o => o.Goal == RivalGoal.MakePeace && o.Target == "ballas");
    }

    [Fact]
    public void KilledLeaderIsSucceededAndThePlayerGetsTheBlame()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia);
        LivingWorldSystem.EnsureInitialized(ctx);
        var triads = ctx.World.GetRival("triads")!;
        var oldLeader = triads.Leader!;
        float hostility = triads.Hostility;
        LivingWorldSystem.KillLeader(ctx, triads, null, true);
        Assert.False(oldLeader.Alive);
        Assert.NotSame(oldLeader, triads.Leader);
        Assert.True(triads.Leader!.Alive);
        Assert.True(triads.Hostility > hostility);
        Assert.Contains(ctx.World.Living.News, n => n.Text.Contains(oldLeader.Name));
    }

    [Fact]
    public void FactionWithoutTurfOrFightersCollapsesWithoutCreditingThePlayer()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang);
        LivingWorldSystem.EnsureInitialized(ctx);
        var lost = ctx.World.GetRival("lost")!;
        foreach (var t in ctx.World.Territories.Where(t => t.OwnerId == "lost")) { t.Influence.Clear(); t.Influence["oneil"] = 80; TerritorySystem.UpdateOwnership(t); }
        lost.Strength = 0;
        int eliminatedBefore = ctx.Faction!.Stats.RivalsEliminated;
        Simulation.Tick(ctx, null);
        Assert.True(lost.Eliminated);
        Assert.Equal(eliminatedBefore, ctx.Faction.Stats.RivalsEliminated);
    }

    [Fact]
    public void RecruitmentGrowsManpowerAndCostsMoney()
    {
        var ctx = WorldOnly();
        LivingWorldSystem.EnsureInitialized(ctx);
        var vagos = ctx.World.GetRival("vagos")!;
        vagos.Wealth = 60;
        int before = vagos.Manpower;
        LivingWorldSystem.Execute(ctx, vagos, RivalGoal.Recruit, null);
        Assert.True(vagos.Manpower > before);
        Assert.True(vagos.Wealth < 60);
        Assert.Contains(ctx.World.Living.Recruitments, n => n.RivalId == "vagos");
    }

    [Fact]
    public void LivingWorldCanBeSwitchedOff()
    {
        var ctx = WorldOnly();
        ctx.Config.World.LivingWorld = false;
        for (int h = 0; h < 48; h++) Simulation.Tick(ctx, null);
        Assert.Empty(ctx.World.Living.News);
        Assert.Equal(0, ctx.World.Living.BattlesFought);
    }

    [Fact]
    public void LivingWorldSurvivesSaveAndLoad()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia);
        for (int h = 0; h < 72; h++) Simulation.Tick(ctx, null);
        string dir = TestHelpers.TempDir();
        var sm = new SaveManager(dir);
        Assert.True(sm.Save(ctx.State, "lw").Ok);
        var loaded = sm.Load("lw", out var message);
        Assert.NotNull(loaded);
        Assert.Equal(ctx.World.Living.Relations.Count, loaded!.World.Living.Relations.Count);
        Assert.Equal(ctx.World.GetRival("ballas")!.Leader!.Name, loaded.World.GetRival("ballas")!.Leader!.Name);
        Assert.Equal(ctx.World.Living.News.Count, loaded.World.Living.News.Count);
    }

    [Fact]
    public void NotificationLevelFiltersToasts()
    {
        var ctx = WorldOnly(13);
        ctx.Config.World.LivingWorldNotifications = WorldNotifyLevel.None;
        for (int h = 0; h < 24 * 4; h++) Simulation.Tick(ctx, null);
        Assert.DoesNotContain(ctx.Notifications, n => n.Category == "World");
        Assert.NotEmpty(ctx.World.Living.News);
    }

    [Fact]
    public void YearOfSimulationStaysFastAndConsistent()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, seed: 21);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int h = 0; h < 24 * 365; h++) Simulation.Tick(ctx, null);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 20000, "a simulated year took " + sw.ElapsedMilliseconds + " ms");
        var lw = ctx.World.Living;
        Assert.True(lw.News.Count <= LivingWorldSystem.MaxNews);
        Assert.True(lw.ActiveBattles.Count() <= LivingWorldSystem.MaxActiveBattles);
        Assert.All(ctx.World.Rivals, r => Assert.InRange(r.Strength, 0, 100));
        Assert.All(ctx.World.Rivals.Where(r => r.Active), r => Assert.True(r.Leader != null && r.Leader.Alive));
    }
}
