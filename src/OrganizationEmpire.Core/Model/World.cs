using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Json;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Model
{
    public static class FactionIds
    {
        public const string Player = "player";
        public const string State = "state";
    }

    /// <summary>
    /// A territory maps onto one or more of GTA V's own map zones (the codes returned by GET_NAME_OF_ZONE),
    /// so ownership is tracked using the game's reliable zone system rather than arbitrary shapes.
    /// </summary>
    public sealed class Territory
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> Zones { get; set; } = new List<string>();
        public Vec3 Center { get; set; }
        public float Radius { get; set; } = 400;
        public TerritoryType Type { get; set; }
        public int BaseValue { get; set; } = 100;
        public string OwnerId { get; set; }
        /// <summary>Influence per faction id (0..100). Owner is whoever holds the most influence above the capture threshold.</summary>
        public Dictionary<string, float> Influence { get; set; } = new Dictionary<string, float>();
        public float Unrest { get; set; }
        public float Fortification { get; set; }
        public bool Contested { get; set; }
        public int LastAttackDay { get; set; } = -100;
        /// <summary>True once the runtime has verified (or corrected) Center against the live zone map.</summary>
        public bool CenterVerified { get; set; }

        [JsonIgnore]
        public bool IsStrategic => Type == TerritoryType.Strategic;

        public float InfluenceOf(string factionId) => factionId != null && Influence.TryGetValue(factionId, out float v) ? v : 0f;
    }

    public sealed class RivalFaction
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public RivalKind Kind { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Eliminated { get; set; }
        public bool IsDefault { get; set; } = true;
        public float Strength { get; set; } = 50;
        public float Wealth { get; set; } = 50;
        /// <summary>0..1 tendency to attack/expand.</summary>
        public float Aggression { get; set; } = 0.5f;
        /// <summary>Hostility toward the player faction, 0..100.</summary>
        public float Hostility { get; set; } = 20;
        public RelationStatus Relation { get; set; } = RelationStatus.Neutral;
        /// <summary>Positive = player winning the conflict.</summary>
        public float WarScore { get; set; }
        public int RelationChangedDay { get; set; }
        public string Color { get; set; } = "#888888";
        public List<string> PedModels { get; set; } = new List<string>();
        public List<string> VehicleModels { get; set; } = new List<string>();
        public List<string> HomeTerritories { get; set; } = new List<string>();
        /// <summary>GTA's own relationship group for this gang's ambient peds (e.g. AMBIENT_GANG_BALLAS), if any.</summary>
        public string VanillaRelationshipGroup { get; set; }
        public string Weapon { get; set; } = "WEAPON_PISTOL";
        public string Description { get; set; }

        // ---- living world (see LivingWorldSystem)
        public RivalLeader Leader { get; set; }
        public List<RivalLeader> Lieutenants { get; set; } = new List<RivalLeader>();
        public RivalTemperament Temperament { get; set; } = RivalTemperament.Opportunist;
        /// <summary>0..100 stockpile of weapons and ammunition.</summary>
        public float Arms { get; set; } = 40;
        /// <summary>0..100 political/social leverage.</summary>
        public float Influence { get; set; } = 20;
        public float Morale { get; set; } = 60;
        public RivalGoal Goal { get; set; } = RivalGoal.Consolidate;
        public string GoalTargetId { get; set; }
        public int NextDecisionTick { get; set; }
        /// <summary>Registered by another mod through the integration API (not part of the default world).</summary>
        public bool External { get; set; }
        public string ExternalSource { get; set; }

        /// <summary>Approximate number of members (strength is the canonical military value).</summary>
        [JsonIgnore]
        public int Manpower => (int)System.Math.Round(Strength * 1.2f);

        [JsonIgnore]
        public bool Active => Enabled && !Eliminated;

        [JsonIgnore]
        public bool IsHostile => Relation == RelationStatus.War;
    }

    /// <summary>The fictional state government of San Andreas, the final obstacle of the takeover campaign.</summary>
    public sealed class StateAuthority
    {
        public string Name { get; set; } = "Government of San Andreas";
        public float Stability { get; set; } = 85;
        public float Authority { get; set; } = 80;
        public float ForceStrength { get; set; } = 80;
        public bool Overthrown { get; set; }
    }

    public sealed class AttackEvent
    {
        public string Id { get; set; }
        public string TerritoryId { get; set; }
        public string AttackerId { get; set; }
        public float Strength { get; set; }
        public int CreatedTick { get; set; }
        public int DeadlineTick { get; set; }
        public bool IsRaid { get; set; }
    }

    public sealed class WorldState
    {
        public int Seed { get; set; } = 1337;
        /// <summary>Strategic ticks elapsed (1 tick = 1 faction hour).</summary>
        public int Tick { get; set; }
        public List<Territory> Territories { get; set; } = new List<Territory>();
        public List<RivalFaction> Rivals { get; set; } = new List<RivalFaction>();
        public StateAuthority State { get; set; } = new StateAuthority();
        public List<AttackEvent> PendingAttacks { get; set; } = new List<AttackEvent>();
        public LivingWorldState Living { get; set; } = new LivingWorldState();

        [JsonIgnore]
        public int Day => Tick / 24;

        [JsonIgnore]
        public int Hour => Tick % 24;

        public Territory GetTerritory(string id) => id == null ? null : Territories.FirstOrDefault(t => t.Id == id);
        public RivalFaction GetRival(string id) => id == null ? null : Rivals.FirstOrDefault(r => r.Id == id);

        public Territory FindByZone(string zoneCode)
        {
            if (string.IsNullOrEmpty(zoneCode)) return null;
            foreach (var t in Territories)
            {
                foreach (var z in t.Zones)
                {
                    if (string.Equals(z, zoneCode, System.StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            return null;
        }

        public string FactionName(string id, FactionState player)
        {
            if (id == null) return "Unclaimed";
            if (id == FactionIds.Player) return player?.Name ?? "You";
            if (id == FactionIds.State) return State.Name;
            return GetRival(id)?.Name ?? id;
        }
    }

    public sealed class TakeoverState
    {
        /// <summary>Index of the current campaign stage (0-based). Equal to the stage count once complete.</summary>
        public int Stage { get; set; }
        public List<string> CompletedMilestones { get; set; } = new List<string>();
        public float Momentum { get; set; }
        public bool FinalMissionUnlocked { get; set; }
        public bool Completed { get; set; }
        public int CompletedDay { get; set; }
        public int FinalAttempts { get; set; }
    }

    public sealed class Ministry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OfficialMemberId { get; set; }
        public int Budget { get; set; } = 5000;
        public string Description { get; set; }
    }

    public sealed class GovernmentState
    {
        public bool Established { get; set; }
        public int EstablishedDay { get; set; }
        public string StateName { get; set; } = "Free State of San Andreas";
        public string GovernmentName { get; set; } = "Provisional Council";
        public string RulerTitle { get; set; } = "President";
        public GovernmentType Type { get; set; } = GovernmentType.Council;
        public Identity StateIdentity { get; set; } = new Identity();
        public bool KeptFactionFlag { get; set; } = true;
        public float Stability { get; set; } = 50;
        public float Legitimacy { get; set; } = 30;
        public float PublicSupport { get; set; } = 40;
        public float ForcesReadiness { get; set; } = 50;
        public float TaxRate { get; set; } = 0.15f;
        public long Treasury { get; set; } = 50000;
        public List<Ministry> Ministries { get; set; } = new List<Ministry>();
        public List<string> Policies { get; set; } = new List<string>();
        /// <summary>Rank layout before the transition, restorable if the player reverts.</summary>
        public List<Rank> FormerRanks { get; set; } = new List<Rank>();
        public string FormerFactionName { get; set; }
        public float Insurgency { get; set; } = 10;
    }

    public sealed class MissionOffer
    {
        public string Id { get; set; }
        public string DefinitionId { get; set; }
        public string TerritoryId { get; set; }
        public string RivalId { get; set; }
        public int Difficulty { get; set; } = 1;
        public long RewardCash { get; set; }
        public int RewardXp { get; set; }
        public int ExpiresTick { get; set; }
        public bool Urgent { get; set; }
        public string AttackId { get; set; }
        public int Seed { get; set; }
    }

    public sealed class MissionRecord
    {
        public string DefinitionId { get; set; }
        public string Title { get; set; }
        public int Day { get; set; }
        public bool Success { get; set; }
        public string Notes { get; set; }
    }

    public sealed class MemoryEntry
    {
        public int Day { get; set; }
        public MemoryKind Kind { get; set; }
        public string Text { get; set; }
        public float Importance { get; set; } = 0.5f;
    }

    public sealed class ChatTurn
    {
        /// <summary>"player" or "npc".</summary>
        public string Speaker { get; set; }
        public string Text { get; set; }
        public int Day { get; set; }
    }

    public sealed class NpcMemory
    {
        public string NpcId { get; set; }
        public string Name { get; set; }
        public List<MemoryEntry> Entries { get; set; } = new List<MemoryEntry>();
        public List<ChatTurn> Dialogue { get; set; } = new List<ChatTurn>();
        /// <summary>Compressed summary of trimmed older memories.</summary>
        public string Summary { get; set; } = "";
        public int LastTalkedDay { get; set; } = -1;
    }

    /// <summary>A world NPC profile (not yet a member). Deterministic from the ped so it stays stable.</summary>
    public sealed class NpcProfile
    {
        public string Key { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool Female { get; set; }
        public int Age { get; set; }
        public Personality Personality { get; set; }
        public Personality SecondaryTrait { get; set; }
        public string Background { get; set; }
        public string Hometown { get; set; }
        /// <summary>0..100 baseline willingness to join any faction.</summary>
        public float Willingness { get; set; }
        public float Skill { get; set; }
        /// <summary>Rival id if the NPC already belongs to a rival faction.</summary>
        public string AffiliatedRivalId { get; set; }
        public bool IsLawEnforcement { get; set; }
        public float RapportBonus { get; set; }

        [JsonIgnore]
        public string FullName => FirstName + " " + LastName;
    }

    /// <summary>Everything persisted in a save slot.</summary>
    public sealed class GameState
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string SlotName { get; set; }
        public System.DateTime SavedAtUtc { get; set; }
        public FactionState Faction { get; set; }
        public WorldState World { get; set; } = new WorldState();
        public Dictionary<string, NpcMemory> Memories { get; set; } = new Dictionary<string, NpcMemory>();
        public Dictionary<string, NpcProfile> KnownNpcs { get; set; } = new Dictionary<string, NpcProfile>();
        public List<MissionOffer> MissionBoard { get; set; } = new List<MissionOffer>();
        public List<MissionRecord> MissionHistory { get; set; } = new List<MissionRecord>();
        public int LastBoardRefreshTick { get; set; } = -1000;

        [JsonIgnore]
        public bool HasFaction => Faction != null;
    }
}
