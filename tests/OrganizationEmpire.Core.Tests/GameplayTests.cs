using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using Xunit;

namespace OrganizationEmpire.Core.Tests;

public class FactionCreationTests
{
    [Theory]
    [InlineData(FactionType.StreetGang, "Leader", "Recruit")]
    [InlineData(FactionType.Mafia, "Don", "Associate")]
    [InlineData(FactionType.Militia, "Commander", "Recruit")]
    public void EachTypeGetsItsOwnRankTemplate(FactionType type, string top, string bottom)
    {
        var ctx = TestHelpers.NewGame(type);
        Assert.Equal(top, ctx.Faction.TopRank.Name);
        Assert.Equal(bottom, ctx.Faction.LowestRank.Name);
        Assert.True(ctx.Faction.TopRank.ReservedForPlayer);
        Assert.Equal(6, ctx.Faction.Ranks.Count);
    }

    [Fact]
    public void TypesHaveDistinctMechanics()
    {
        var gang = TestHelpers.NewGame(FactionType.StreetGang);
        var mafia = TestHelpers.NewGame(FactionType.Mafia);
        var militia = TestHelpers.NewGame(FactionType.Militia);
        Assert.True(mafia.Faction.Resources.Influence > gang.Faction.Resources.Influence);
        Assert.True(militia.Faction.Resources.Supplies > gang.Faction.Resources.Supplies);
        Assert.Equal(GangActivities.Both, gang.Faction.Activities);
        Assert.Equal(GangActivities.None, mafia.Faction.Activities);
        Assert.NotEqual(FactionTypeInfo.TierName(gang.Faction), FactionTypeInfo.TierName(militia.Faction));
        Assert.True(FactionTypeInfo.TakeoverSpeed(FactionType.Militia) > FactionTypeInfo.TakeoverSpeed(FactionType.StreetGang));
        Assert.True(TakeoverSystem.StagesFor(FactionType.StreetGang).Count > TakeoverSystem.StagesFor(FactionType.Militia).Count);
    }

    [Fact]
    public void InitialMembersAreCappedAndOrganized()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia, 50);
        Assert.Equal(FactionFactory.MaxInitialMembers, ctx.Faction.Members.Count);
        Assert.Single(ctx.Faction.Members, m => m.IsDeputy);
        Assert.NotNull(ctx.Faction.Squads[0].LeaderId);
        Assert.All(ctx.Faction.Members, m => Assert.False(ctx.Faction.GetRank(m.RankId)!.ReservedForPlayer));
    }

    [Fact]
    public void HqGivesTerritoryFoothold()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 2, StartingProgression.Powerful);
        var mirror = ctx.World.FindByZone("MIRR")!;
        Assert.Equal(FactionIds.Player, mirror.OwnerId);
        Assert.True(TerritorySystem.PlayerTerritoryCount(ctx.World) >= 2);
    }
}

public class RankAndSquadTests
{
    [Fact]
    public void CreateRenameMoveDeleteRanks()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 3);
        var f = ctx.Faction;
        Assert.True(RankSystem.Create(f, "Enforcer", out var rank).Ok);
        Assert.Equal(7, f.Ranks.Count);
        Assert.False(RankSystem.Create(f, "enforcer", out _).Ok);
        Assert.True(RankSystem.Rename(f, rank, "Hitter").Ok);
        int before = rank.Order;
        Assert.True(RankSystem.Move(f, rank, -1).Ok);
        Assert.Equal(before - 1, rank.Order);
        Assert.False(RankSystem.Move(f, f.TopRank, 1).Ok);
        Assert.False(RankSystem.Delete(f, f.TopRank).Ok);

        var m = f.Members[1];
        RankSystem.SetRank(f, m, rank, 0);
        Assert.True(RankSystem.Delete(f, rank).Ok);
        Assert.NotNull(f.GetRank(m.RankId));
        Assert.Equal(Enumerable.Range(0, f.Ranks.Count), f.Ranks.Select(r => r.Order));
    }

    [Fact]
    public void PromotionRaisesLoyaltyDemotionLowersIt()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia, 3);
        var f = ctx.Faction;
        var m = f.Members.First(x => !x.IsDeputy);
        float loyalty = m.Loyalty;
        Assert.True(RankSystem.Promote(f, m, 0).Ok);
        Assert.True(m.Loyalty > loyalty);
        float after = m.Loyalty;
        Assert.True(RankSystem.Demote(f, m, 0).Ok);
        Assert.True(m.Loyalty < after);
        // Cannot promote into the player's rank
        var second = f.Ranks.First(r => r.Order == 1);
        RankSystem.SetRank(f, m, second, 0);
        Assert.False(RankSystem.Promote(f, m, 0).Ok);
        Assert.False(RankSystem.SetRank(f, m, f.TopRank, 0).Ok);
    }

    [Fact]
    public void PermissionsToggleAndGateSquadLeadership()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia, 3);
        var f = ctx.Faction;
        var recruit = f.LowestRank;
        var m = f.Members.First(x => !x.IsDeputy);
        m.RankId = recruit.Id;
        Assert.True(SquadSystem.Create(f, "Night Watch", out var squad).Ok);
        Assert.False(SquadSystem.SetLeader(f, squad, m).Ok);
        RankSystem.TogglePermission(recruit, RankPermission.LeadSquad);
        Assert.True(SquadSystem.SetLeader(f, squad, m).Ok);
        Assert.Equal(squad.Id, m.SquadId);
        Assert.DoesNotContain(m.Id, f.Squads[0].MemberIds);
    }

    [Fact]
    public void SquadsAddRemoveDelete()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 4);
        var f = ctx.Faction;
        SquadSystem.Create(f, "B", out var b);
        var m = f.Members[0];
        Assert.True(SquadSystem.AddMember(f, b, m).Ok);
        Assert.False(SquadSystem.AddMember(f, b, m).Ok);
        Assert.True(SquadSystem.RemoveMember(f, b, m).Ok);
        Assert.Null(m.SquadId);
        SquadSystem.AddMember(f, b, m);
        Assert.True(SquadSystem.Delete(f, b).Ok);
        Assert.Null(m.SquadId);
    }

    [Fact]
    public void ApplyTemplateMapsMembersByRelativeRank()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 4);
        var f = ctx.Faction;
        RankSystem.ApplyTemplate(f, RankTemplates.Government());
        Assert.Equal("Head of State", f.TopRank.Name);
        Assert.All(f.Members, m => Assert.False(f.GetRank(m.RankId)!.ReservedForPlayer));
    }
}

public class RecruitmentTests
{
    private static NpcProfile Profile(Personality p, float willingness = 50, string? rival = null, bool law = false)
    {
        var prof = RecruitmentSystem.CreateProfile("k", 1, false, rival, law, p.ToString());
        prof.Willingness = willingness;
        return prof;
    }

    [Fact]
    public void ChanceIsClampedAndExplained()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang);
        var rc = RecruitmentSystem.ComputeChance(ctx, Profile(Personality.Cowardly, 0), RecruitApproach.Persuade);
        Assert.InRange(rc.Chance, 3f, 95f);
        Assert.Contains(rc.Factors, f => f.Key.StartsWith("Personality"));
        ctx.Faction.Reputation = 100; ctx.Faction.Level = 20;
        var high = RecruitmentSystem.ComputeChance(ctx, Profile(Personality.Loyal, 100), RecruitApproach.Pay);
        Assert.Equal(95f, high.Chance);
    }

    [Fact]
    public void PersonalityMattersPerFactionType()
    {
        var gang = TestHelpers.NewGame(FactionType.StreetGang);
        var militia = TestHelpers.NewGame(FactionType.Militia);
        var aggressive = Profile(Personality.Aggressive);
        float ga = RecruitmentSystem.ComputeChance(gang, aggressive, RecruitApproach.Persuade).Factors.First(f => f.Key.StartsWith("Personality")).Value;
        float ma = RecruitmentSystem.ComputeChance(militia, aggressive, RecruitApproach.Persuade).Factors.First(f => f.Key.StartsWith("Personality")).Value;
        Assert.True(ga > ma);
        var disciplined = Profile(Personality.Disciplined);
        float g = RecruitmentSystem.ComputeChance(gang, disciplined, RecruitApproach.Persuade).Factors.First(f => f.Key.StartsWith("Personality")).Value;
        float m = RecruitmentSystem.ComputeChance(militia, disciplined, RecruitApproach.Persuade).Factors.First(f => f.Key.StartsWith("Personality")).Value;
        Assert.True(m > g);
    }

    [Fact]
    public void BlocksLawEnforcementAndFullRoster()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia, 0);
        Assert.True(RecruitmentSystem.ComputeChance(ctx, Profile(Personality.Calm, 50, null, true), RecruitApproach.Persuade).Blocked);
        ctx.Config.Gameplay.MaxRosterSize = 0;
        Assert.True(RecruitmentSystem.ComputeChance(ctx, Profile(Personality.Calm), RecruitApproach.Persuade).Blocked);
    }

    [Fact]
    public void RivalAffiliationReducesChance()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang);
        float free = RecruitmentSystem.ComputeChance(ctx, Profile(Personality.Calm), RecruitApproach.Persuade).Chance;
        float ballas = RecruitmentSystem.ComputeChance(ctx, Profile(Personality.Calm, 50, "ballas"), RecruitApproach.Persuade).Chance;
        Assert.True(ballas < free);
    }

    [Fact]
    public void SuccessfulAttemptCreatesMemberWithAppearance()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 0);
        ctx.Faction.Reputation = 100; ctx.Faction.Level = 20;
        var p = Profile(Personality.Loyal, 100);
        var ap = new PedAppearance { Captured = true };
        ap.Drawables[3] = 5;
        RecruitResult? res = null;
        for (int i = 0; i < 20 && (res == null || !res.Success); i++) res = RecruitmentSystem.Attempt(ctx, p, RecruitApproach.Persuade, "a_m_y_stbla_01", ap);
        Assert.True(res!.Success);
        Assert.Single(ctx.Faction.Members);
        Assert.Equal(5, ctx.Faction.Members[0].Appearance.Drawables[3]);
        Assert.Equal(OrderType.Follow, ctx.Faction.Members[0].Order);
    }

    [Fact]
    public void PayApproachChargesCash()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 0);
        long before = ctx.Faction.Resources.Cash;
        RecruitmentSystem.Attempt(ctx, Profile(Personality.Calm), RecruitApproach.Pay, "a_m_y_stbla_01", null);
        Assert.Equal(before - RecruitmentSystem.PayCost(ctx.Faction), ctx.Faction.Resources.Cash);
    }
}

public class EconomyAndTerritoryTests
{
    [Fact]
    public void InfluenceNeverExceedsHundredAndOwnershipFlips()
    {
        var t = new Territory { Id = "t" };
        TerritorySystem.AddInfluence(t, "a", 70);
        TerritorySystem.AddInfluence(t, "b", 60);
        Assert.True(t.Influence.Values.Sum() <= 100.01f);
        TerritorySystem.UpdateOwnership(t, out _);
        Assert.Equal("b", t.OwnerId);
        Assert.True(t.Contested);
        TerritorySystem.AddInfluence(t, "b", -60);
        TerritorySystem.UpdateOwnership(t, out _);
        Assert.NotEqual("b", t.OwnerId);
    }

    [Fact]
    public void EconomyPaysIncomeAndSalaries()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia, 4, StartingProgression.Established);
        var ledger = EconomySystem.Compute(ctx, false);
        Assert.Contains(ledger.Expenses, l => l.Label == "Salaries");
        Assert.True(ledger.TotalIncome > 0);
        long before = ctx.Faction.Resources.Cash;
        EconomySystem.Compute(ctx, true);
        Assert.Equal(before + (long)Math.Round(ledger.Net), ctx.Faction.Resources.Cash);
    }

    [Fact]
    public void BankruptcyHurtsMorale()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 8);
        ctx.Faction.Resources.Cash = 0;
        ctx.Config.Gameplay.RandomEventsEnabled = false;
        ctx.Faction.Activities = GangActivities.None;
        foreach (var t in ctx.World.Territories) { t.Influence.Remove(FactionIds.Player); TerritorySystem.UpdateOwnership(t); }
        foreach (var r in ctx.Faction.Ranks) r.Salary = 5000;
        float morale = ctx.Faction.Members.Average(m => m.Morale);
        for (int i = 0; i < 48; i++) Simulation.Tick(ctx, null);
        Assert.Equal(0, ctx.Faction.Resources.Cash);
        Assert.True(ctx.Faction.Members.Where(m => m.Status == MemberStatus.Active).Average(m => m.Morale) < morale);
    }

    [Fact]
    public void GangNarcoticsActivityProducesAndSells()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 2, StartingProgression.Established);
        ctx.Faction.Activities = GangActivities.Narcotics;
        ctx.Faction.Resources.Supplies = 50;
        var ledger = EconomySystem.Compute(ctx, true);
        Assert.Contains(ledger.Income, l => l.Label.StartsWith("Product sales"));
    }

    [Fact]
    public void HqUpgradesCostMoneyAndRaiseCapacity()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia, 2, StartingProgression.Powerful);
        int cap = MemberSystem.RosterCapacity(ctx.Faction, 300);
        Assert.True(EconomySystem.UpgradeHq(ctx, HqUpgrade.Barracks).Ok);
        Assert.Equal(cap + 6, MemberSystem.RosterCapacity(ctx.Faction, 300));
    }

    [Fact]
    public void LoadoutsConsumeArmsAndRespectLevel()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 2);
        var m = ctx.Faction.Members[0];
        Assert.False(MemberSystem.SetLoadout(ctx.Faction, m, LoadoutType.Heavy).Ok);
        ctx.Faction.Level = 10;
        int arms = ctx.Faction.Resources.Arms;
        Assert.True(MemberSystem.SetLoadout(ctx.Faction, m, LoadoutType.SMG).Ok);
        Assert.True(ctx.Faction.Resources.Arms < arms);
    }

    [Fact]
    public void LevelingUpFromXp()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang);
        ProgressionSystem.AddXp(ctx, 5000);
        Assert.True(ctx.Faction.Level > 1);
        Assert.InRange(ProgressionSystem.LevelProgress(ctx.Faction), 0f, 1f);
    }
}

public class WarTests
{
    [Fact]
    public void DeclareWarAndCeasefireFlow()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia, 5, StartingProgression.Powerful);
        var r = ctx.World.GetRival("triads")!;
        Assert.True(WarSystem.DeclareWar(ctx, r).Ok);
        Assert.Equal(RelationStatus.War, r.Relation);
        Assert.False(WarSystem.Propose(ctx, r, RelationStatus.Peace).Ok);
        r.WarScore = 100;
        bool accepted = false;
        for (int i = 0; i < 30 && !accepted; i++) accepted = WarSystem.Propose(ctx, r, RelationStatus.Ceasefire).Ok;
        Assert.True(accepted);
        Assert.Equal(RelationStatus.Ceasefire, r.Relation);
    }

    [Fact]
    public void RivalsAttackAtWarAndAttacksResolve()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 3, StartingProgression.Powerful);
        foreach (var r in ctx.World.Rivals) { r.Relation = RelationStatus.War; r.Aggression = 1; r.Hostility = 100; }
        bool sawAttack = false;
        for (int i = 0; i < 200; i++)
        {
            Simulation.Tick(ctx, null);
            if (ctx.World.PendingAttacks.Count > 0) sawAttack = true;
            Assert.All(ctx.World.PendingAttacks, a => Assert.True(a.DeadlineTick >= ctx.World.Tick));
        }
        Assert.True(sawAttack);
        Assert.Contains(ctx.State.MissionBoard, o => o.Urgent || o.AttackId == null);
    }

    [Fact]
    public void EliminationRequiresNoTerritory()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia, 3);
        var r = ctx.World.GetRival("aztecas")!;
        r.Relation = RelationStatus.War; // the player's war: elimination is credited to the player
        r.Strength = 0;
        WarSystem.CheckElimination(ctx, r);
        Assert.False(r.Eliminated); // still holds Vespucci
        foreach (var t in ctx.World.Territories.Where(t => t.OwnerId == r.Id)) { t.Influence.Clear(); t.OwnerId = null; }
        r.Strength = 0;
        WarSystem.CheckElimination(ctx, r);
        Assert.True(r.Eliminated);
        Assert.Equal(1, ctx.Faction.Stats.RivalsEliminated);
    }

    [Fact]
    public void DisablingRivalRemovesItFromSimulationAndRestoreBringsItBack()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 2);
        var ballas = ctx.World.GetRival("ballas")!;
        WarSystem.SetRivalEnabled(ctx, ballas, false);
        Assert.DoesNotContain(ctx.World.Territories, t => t.OwnerId == "ballas");
        for (int i = 0; i < 50; i++) Simulation.Tick(ctx, null);
        Assert.DoesNotContain(ctx.World.Territories, t => t.InfluenceOf("ballas") > 0);
        WorldFactory.RestoreDefaultRivals(ctx.World);
        Assert.True(ballas.Enabled);
        Assert.Contains(ctx.World.Territories, t => t.InfluenceOf("ballas") > 0);
    }

    [Fact]
    public void SuspicionTriggersRaidForMilitia()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia, 3);
        ctx.World.FindByZone("MIRR")!.OwnerId = FactionIds.Player;
        bool raid = false;
        for (int i = 0; i < 400 && !raid; i++)
        {
            ctx.Faction.Suspicion = 95;
            SuspicionSystem.Tick(ctx, 0);
            raid = ctx.World.PendingAttacks.Any(a => a.IsRaid);
        }
        Assert.True(raid);
        Assert.Contains(ctx.State.MissionBoard, o => o.DefinitionId == "defend_hq" && o.Urgent);
    }
}

public class TakeoverAndGovernmentTests
{
    private static void ForceStageComplete(SimContext ctx)
    {
        var f = ctx.Faction;
        f.Level = 15; f.Reputation = 100; f.PublicSupport = 100; f.Suspicion = 0; f.Resources.Influence = 500; f.Resources.Arms = 500;
        while (f.Members.Count < 30) f.Members.Add(FactionFactory.GenerateMember(f, ctx.Rng, "a_m_y_stbla_01", false, 0));
        foreach (var m in f.Members) m.Morale = 90;
        f.Morale = 90;
        foreach (var t in ctx.World.Territories) { t.Influence.Clear(); TerritorySystem.AddInfluence(t, FactionIds.Player, 90); TerritorySystem.UpdateOwnership(t); }
        ctx.World.State.Stability = 10;
        foreach (var r in ctx.World.Rivals) { r.Eliminated = true; }
        f.Stats.RivalsEliminated = 5;
        while (f.Businesses.Count < 4) f.Businesses.Add(new Business { Id = Ids.New("b"), Kind = BusinessKind.Bodega, Name = "x" });
        f.Takeover.CompletedMilestones.Add("mil_hearts");
        f.Takeover.CompletedMilestones.Add("maf_councilman");
    }

    [Theory]
    [InlineData(FactionType.Militia)]
    [InlineData(FactionType.Mafia)]
    [InlineData(FactionType.StreetGang)]
    public void CampaignUnlocksFinalMissionThenGovernment(FactionType type)
    {
        var ctx = TestHelpers.NewGame(type, 3);
        ForceStageComplete(ctx);
        TakeoverSystem.Tick(ctx);
        Assert.True(ctx.Faction.Takeover.FinalMissionUnlocked);
        string finalId = TakeoverSystem.FinalMissionId(type);
        Assert.Contains(ctx.State.MissionBoard, o => o.DefinitionId == finalId);

        var offer = ctx.State.MissionBoard.First(o => o.DefinitionId == finalId);
        MissionRewards.Apply(ctx, offer, new MissionOutcome { Success = true, Kills = 20 });
        Assert.True(ctx.Faction.Takeover.Completed);
        Assert.True(TakeoverSystem.AwaitingGovernmentFormation(ctx.Faction));

        var res = TakeoverSystem.EstablishGovernment(ctx, new GovernmentSetup { StateName = "Republic of Los Santos", GovernmentName = "Council", RulerTitle = "Chancellor", Type = GovernmentType.Republic, KeepFactionFlag = true });
        Assert.True(res.Ok);
        Assert.True(ctx.Faction.IsGovernment);
        Assert.Equal("Chancellor", ctx.Faction.PlayerTitle);
        Assert.Equal("Head of State", ctx.Faction.TopRank.Name);
        Assert.Equal(6, ctx.Faction.Government.FormerRanks.Count);
        Assert.DoesNotContain(ctx.World.Territories, t => t.OwnerId == FactionIds.State);
    }

    [Fact]
    public void GangPathIsLongerThanMilitiaPath()
    {
        var gang = TestHelpers.NewGame(FactionType.StreetGang, 8, StartingProgression.Established);
        var mil = TestHelpers.NewGame(FactionType.Militia, 8, StartingProgression.Established);
        Assert.True(TakeoverSystem.OverallProgress(mil) >= TakeoverSystem.OverallProgress(gang));
    }

    [Fact]
    public void FailedFinalMissionStrengthensState()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia, 3);
        ForceStageComplete(ctx);
        TakeoverSystem.Tick(ctx);
        var offer = ctx.State.MissionBoard.First(o => o.DefinitionId == "final_militia");
        float stab = ctx.World.State.Stability;
        MissionRewards.Apply(ctx, offer, new MissionOutcome { Success = false, FailureReason = "wasted" });
        Assert.False(ctx.Faction.Takeover.Completed);
        Assert.True(ctx.World.State.Stability > stab);
        Assert.Equal(1, ctx.Faction.Takeover.FinalAttempts);
    }

    [Fact]
    public void GovernmentAppointmentsPoliciesAndTick()
    {
        var ctx = TestHelpers.NewGame(FactionType.Militia, 6);
        ForceStageComplete(ctx);
        TakeoverSystem.OnFinalMissionSuccess(ctx);
        TakeoverSystem.EstablishGovernment(ctx, new GovernmentSetup { Type = GovernmentType.Junta });
        var g = ctx.Faction.Government;
        var m = ctx.Faction.Members[0];
        m.Loyalty = 80;
        Assert.True(GovernmentSystem.Appoint(ctx, g.Ministries[0], m).Ok);
        Assert.Equal(g.Ministries[0].Name, m.GovernmentRole);
        Assert.True(GovernmentSystem.Effectiveness(ctx.Faction, g.Ministries[0]) > 0);
        Assert.True(GovernmentSystem.TogglePolicy(ctx, "martial_law").Ok);
        Assert.Contains("martial_law", g.Policies);
        for (int i = 0; i < 100; i++) Simulation.Tick(ctx, null);
        Assert.InRange(g.Stability, 0, 100);
        Assert.True(GovernmentSystem.Rename(ctx, "New SA", null, "Marshal").Ok);
        Assert.Equal("Marshal", ctx.Faction.PlayerTitle);
    }
}

public class MissionTests
{
    [Theory]
    [InlineData(FactionType.StreetGang)]
    [InlineData(FactionType.Mafia)]
    [InlineData(FactionType.Militia)]
    public void EachTypeHasSeveralPlayableMissions(FactionType type)
    {
        var defs = MissionCatalog.All.Where(d => d.Types != null && d.Types.Contains(type) && d.Template != MissionTemplate.FinalCampaign).ToList();
        Assert.True(defs.Count >= 5);
        Assert.True(defs.Select(d => d.Template).Distinct().Count() >= 4);
    }

    [Fact]
    public void BoardOffersOnlyEligibleMissions()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia, 4, StartingProgression.Established);
        MissionBoard.Refresh(ctx, true);
        Assert.NotEmpty(ctx.State.MissionBoard);
        foreach (var o in ctx.State.MissionBoard)
        {
            var d = MissionCatalog.Get(o.DefinitionId)!;
            Assert.True(d.AvailableFor(FactionType.Mafia));
            Assert.True(MissionCatalog.IsEligible(ctx, d, out _));
        }
    }

    [Fact]
    public void SuccessAppliesRewardsAndTerritory()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 4);
        var def = MissionCatalog.Get("gang_corner")!;
        var offer = MissionBoard.TryAddOffer(ctx, def, false)!;
        var t = ctx.World.GetTerritory(offer.TerritoryId)!;
        float before = t.InfluenceOf(FactionIds.Player);
        long cash = ctx.Faction.Resources.Cash;
        var lines = MissionRewards.Apply(ctx, offer, new MissionOutcome { Success = true, Kills = 6, ParticipantIds = { ctx.Faction.Members[0].Id } });
        Assert.True(t.InfluenceOf(FactionIds.Player) > before);
        Assert.True(ctx.Faction.Resources.Cash > cash);
        Assert.Equal(1, ctx.Faction.Members[0].MissionsCompleted);
        Assert.NotEmpty(lines);
        Assert.DoesNotContain(ctx.State.MissionBoard, o => o.Id == offer.Id);
        Assert.Single(ctx.State.MissionHistory);
    }

    [Fact]
    public void RequirementsGateActivityMissions()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang);
        ctx.Faction.Activities = GangActivities.None;
        Assert.False(MissionCatalog.IsEligible(ctx, MissionCatalog.Get("gang_stash")!, out string reason));
        Assert.Contains("Narcotics", reason);
        ctx.Faction.Activities = GangActivities.Narcotics;
        Assert.True(MissionCatalog.IsEligible(ctx, MissionCatalog.Get("gang_stash")!, out _));
    }
}

public class SimulationSoakTests
{
    [Theory]
    [InlineData(FactionType.StreetGang, 1)]
    [InlineData(FactionType.Mafia, 2)]
    [InlineData(FactionType.Militia, 3)]
    public void LongRunKeepsInvariants(FactionType type, int seed)
    {
        var ctx = TestHelpers.NewGame(type, 8, StartingProgression.Established, seed);
        for (int i = 0; i < 24 * 30; i++)
        {
            Simulation.Tick(ctx, new WorldSignals { PlayerWantedLevel = i % 50 == 0 ? 2 : 0 });
            ctx.DrainNotifications();
        }
        var f = ctx.Faction;
        Assert.True(f.Resources.Cash >= 0);
        Assert.InRange(f.Reputation, 0, 100);
        Assert.InRange(f.Suspicion, 0, 100);
        Assert.All(f.Members, m => { Assert.InRange(m.Loyalty, 0, 100); Assert.InRange(m.Morale, 0, 100); });
        Assert.All(ctx.World.Territories, t => Assert.True(t.Influence.Values.Sum() <= 100.5f));
        Assert.True(f.Events.Count <= EventLog.MaxEvents);
        Assert.All(f.Squads, s => Assert.All(s.MemberIds, id => Assert.NotNull(f.GetMember(id))));
        Assert.Equal(24 * 30, ctx.World.Tick);
    }

    [Fact]
    public void AllEventsCanFireWithoutErrors()
    {
        foreach (var type in new[] { FactionType.StreetGang, FactionType.Mafia, FactionType.Militia })
        {
            var ctx = TestHelpers.NewGame(type, 6);
            ctx.Faction.Members[0].Status = MemberStatus.Dead;
            foreach (var id in EventSystem.EventIds) EventSystem.Trigger(ctx, id);
        }
    }
}
