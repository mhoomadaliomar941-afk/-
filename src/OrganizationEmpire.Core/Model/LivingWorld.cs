using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Json;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Model
{
    /// <summary>How a rival faction tends to decide (modified by its current leader's traits).</summary>
    public enum RivalTemperament { Aggressive, Expansionist, Opportunist, Defensive, Mercantile, Fanatic }

    /// <summary>What a rival faction is currently working towards.</summary>
    public enum RivalGoal { Consolidate, Recruit, Arm, Expand, Retaliate, SeekAllies, MakePeace, LayLow }

    public enum BattleStatus { Active, AttackerWon, DefenderWon, Stalemate, Cancelled }

    public enum WorldNewsKind { War, Peace, Alliance, Territory, Leader, Recruitment, Economy, Battle, Elimination, Player }

    /// <summary>A named leader or lieutenant of a rival faction. Traits are 0..1.</summary>
    public sealed class RivalLeader
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public int Age { get; set; } = 35;
        public float Ambition { get; set; } = 0.5f;
        public float Caution { get; set; } = 0.5f;
        public float Brutality { get; set; } = 0.5f;
        public float Charisma { get; set; } = 0.5f;
        /// <summary>Lieutenants only: loyalty to the current leader (low + ambitious = coup risk).</summary>
        public float Loyalty { get; set; } = 70;
        public bool Alive { get; set; } = true;
        public int SinceDay { get; set; }
        /// <summary>Model used when the leader appears in the world (picked from the faction's models).</summary>
        public string PedModel { get; set; }
    }

    /// <summary>Relationship between two non-player factions (rival ids; "state" for the government).</summary>
    public sealed class FactionRelation
    {
        public string A { get; set; }
        public string B { get; set; }
        public RelationStatus Status { get; set; } = RelationStatus.Neutral;
        /// <summary>0..100 mutual hostility.</summary>
        public float Hostility { get; set; } = 20;
        public int SinceDay { get; set; }

        public bool Involves(string id) => A == id || B == id;
        public string Other(string id) => A == id ? B : A;
    }

    /// <summary>
    /// A fight for a territory between two factions that is not the player's own defense. It runs for several
    /// strategic hours; when the player is near, the game spawns the fighters for real and their casualties feed
    /// back into the result.
    /// </summary>
    public sealed class WorldBattle
    {
        public string Id { get; set; }
        public string TerritoryId { get; set; }
        public string AttackerId { get; set; }
        /// <summary>Rival id, "state", or null when the territory was unclaimed (local resistance).</summary>
        public string DefenderId { get; set; }
        public float AttackerForce { get; set; }
        public float DefenderForce { get; set; }
        public float AttackerStart { get; set; }
        public float DefenderStart { get; set; }
        public int StartTick { get; set; }
        public int Hours { get; set; }
        public int MaxHours { get; set; } = 6;
        public BattleStatus Status { get; set; } = BattleStatus.Active;
        /// <summary>"attacker" / "defender" once the player has fought for one side.</summary>
        public string PlayerSide { get; set; }
        /// <summary>Fighters killed in the live game (reported by the runtime), per side.</summary>
        public int LiveAttackerLosses { get; set; }
        public int LiveDefenderLosses { get; set; }
        public int EndTick { get; set; }

        [JsonIgnore]
        public bool IsActive => Status == BattleStatus.Active;
    }

    public sealed class WorldNews
    {
        public int Day { get; set; }
        public int Hour { get; set; }
        public WorldNewsKind Kind { get; set; }
        public string Text { get; set; }
        public List<string> FactionIds { get; set; } = new List<string>();
        public string TerritoryId { get; set; }
    }

    /// <summary>A rival recruitment the runtime may stage as a visible scene near the player.</summary>
    public sealed class RecruitmentNotice
    {
        public string RivalId { get; set; }
        public string TerritoryId { get; set; }
        public int Count { get; set; }
        public int Tick { get; set; }
    }

    /// <summary>Living-world state stored with the world (all optional: older saves get it on load).</summary>
    public sealed class LivingWorldState
    {
        public bool Initialized { get; set; }
        public List<FactionRelation> Relations { get; set; } = new List<FactionRelation>();
        public List<WorldBattle> Battles { get; set; } = new List<WorldBattle>();
        public List<WorldNews> News { get; set; } = new List<WorldNews>();
        public List<RecruitmentNotice> Recruitments { get; set; } = new List<RecruitmentNotice>();
        public int BattlesFought { get; set; }
        public int LeadersFallen { get; set; }

        public FactionRelation Get(string a, string b)
        {
            if (a == null || b == null || a == b) return null;
            return Relations.FirstOrDefault(r => (r.A == a && r.B == b) || (r.A == b && r.B == a));
        }

        public FactionRelation GetOrCreate(string a, string b, int day)
        {
            var r = Get(a, b);
            if (r != null || a == null || b == null || a == b) return r;
            r = new FactionRelation { A = a, B = b, SinceDay = day };
            Relations.Add(r);
            return r;
        }

        public IEnumerable<WorldBattle> ActiveBattles => Battles.Where(b => b.IsActive);
    }
}
