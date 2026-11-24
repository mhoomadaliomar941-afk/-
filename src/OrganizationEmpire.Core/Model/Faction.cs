using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Json;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Model
{
    public sealed class Resources
    {
        public long Cash { get; set; }
        /// <summary>Generic materials (food, parts, fuel, ammunition stock) used by missions and production.</summary>
        public int Supplies { get; set; }
        /// <summary>Street gang fictional "product" produced by the Narcotics activity; abstract game currency.</summary>
        public int Product { get; set; }
        /// <summary>Weapons stock used for loadouts and the Arms activity.</summary>
        public int Arms { get; set; }
        /// <summary>Political/social leverage (mafia favors, militia sympathizers).</summary>
        public int Influence { get; set; }
        /// <summary>Information gathered (recon, informants).</summary>
        public int Intel { get; set; }

        public Resources Copy() => (Resources)MemberwiseClone();
    }

    public sealed class FactionStats
    {
        public int MissionsCompleted { get; set; }
        public int MissionsFailed { get; set; }
        public int EnemiesKilled { get; set; }
        public int MembersLost { get; set; }
        public int MembersRecruited { get; set; }
        public int TerritoriesCaptured { get; set; }
        public int TerritoriesLost { get; set; }
        public int RivalsEliminated { get; set; }
        public int RaidsSurvived { get; set; }
        public long TotalEarned { get; set; }
        public long TotalSpent { get; set; }
    }

    public sealed class FactionEvent
    {
        public int Day { get; set; }
        public int Hour { get; set; }
        public string Category { get; set; }
        public string Text { get; set; }
        public EventSeverity Severity { get; set; }
    }

    public sealed class Rank
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>0 = highest.</summary>
        public int Order { get; set; }
        public RankPermission Permissions { get; set; }
        /// <summary>Daily pay per member of this rank.</summary>
        public int Salary { get; set; }
        /// <summary>The top rank belongs to the player and cannot be assigned to members.</summary>
        public bool ReservedForPlayer { get; set; }

        public bool Has(RankPermission p) => (Permissions & p) == p;
    }

    public sealed class Squad
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Callsign { get; set; }
        public string LeaderId { get; set; }
        public List<string> MemberIds { get; set; } = new List<string>();
        public SquadRole Role { get; set; }
        public FormationType Formation { get; set; }
        public string VehicleId { get; set; }
        public OrderType Order { get; set; } = OrderType.Standby;
        public Vec3 OrderPosition { get; set; }
        /// <summary>Territory this squad garrisons/patrols when not deployed (adds passive control and defense).</summary>
        public string AssignedTerritoryId { get; set; }
        public bool GuardsHeadquarters { get; set; }
    }

    public sealed class PedAppearance
    {
        public int[] Drawables { get; set; } = new int[12];
        public int[] Textures { get; set; } = new int[12];
        public int[] Palettes { get; set; } = new int[12];
        public int[] PropIndices { get; set; } = { -1, -1, -1, -1, -1 };
        public int[] PropTextures { get; set; } = { 0, 0, 0, 0, 0 };
        public bool Captured { get; set; }
    }

    public sealed class Member
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Nickname { get; set; }
        public bool Female { get; set; }
        public int Age { get; set; }
        public string Hometown { get; set; }
        public string Background { get; set; }

        /// <summary>Ped model name (e.g. "g_m_y_mexgoon_01") or "0x..." hash.</summary>
        public string Model { get; set; }
        public PedAppearance Appearance { get; set; } = new PedAppearance();

        public Personality Personality { get; set; }
        public Personality SecondaryTrait { get; set; }
        public MemberRole Role { get; set; }
        public string RankId { get; set; }
        public string SquadId { get; set; }
        public LoadoutType Loadout { get; set; } = LoadoutType.Sidearm;

        public float Loyalty { get; set; } = 60;
        public float Morale { get; set; } = 60;
        public float Skill { get; set; } = 40;
        public float Discipline { get; set; } = 50;

        public int Kills { get; set; }
        public int MissionsCompleted { get; set; }
        public int JoinedDay { get; set; }

        public MemberStatus Status { get; set; } = MemberStatus.Active;
        public float RecoveryHours { get; set; }
        public OrderType Order { get; set; } = OrderType.Standby;
        public Vec3 OrderPosition { get; set; }

        public bool IsDeputy { get; set; }
        public string GovernmentRole { get; set; }
        /// <summary>Opinion of other members, -100..100.</summary>
        public Dictionary<string, float> Opinions { get; set; } = new Dictionary<string, float>();
        /// <summary>Temporary recruitment/relationship bonus earned through dialogue.</summary>
        public float RapportBonus { get; set; }

        [JsonIgnore]
        public string FullName => (FirstName + " " + LastName).Trim();

        [JsonIgnore]
        public string DisplayName => string.IsNullOrEmpty(Nickname) ? FullName : FirstName + " \"" + Nickname + "\" " + LastName;

        [JsonIgnore]
        public string ShortName => string.IsNullOrEmpty(Nickname) ? FirstName : Nickname;

        [JsonIgnore]
        public bool IsAvailable => Status == MemberStatus.Active;
    }

    public sealed class HeadquartersState
    {
        public string Name { get; set; } = "Headquarters";
        public Vec3 Position { get; set; }
        public float Heading { get; set; }
        public string ZoneCode { get; set; }
        public Dictionary<HqUpgrade, int> Upgrades { get; set; } = new Dictionary<HqUpgrade, int>();
        public GuardBehavior GuardBehavior { get; set; } = GuardBehavior.Static;
        public List<string> GuardMemberIds { get; set; } = new List<string>();
        public bool ShowFlag { get; set; } = true;
        public int EstablishedDay { get; set; }

        public int Level(HqUpgrade u) => Upgrades.TryGetValue(u, out int l) ? l : 0;
    }

    public sealed class FactionVehicle
    {
        public string Id { get; set; }
        public string Model { get; set; }
        public string DisplayName { get; set; }
        public bool UseFactionColors { get; set; } = true;
        public string PrimaryColor { get; set; }
        public string SecondaryColor { get; set; }
        public string Plate { get; set; }
        public Dictionary<int, int> Mods { get; set; } = new Dictionary<int, int>();
        public string AssignedSquadId { get; set; }
        public string DriverMemberId { get; set; }
        public bool Destroyed { get; set; }
        public float RepairHoursLeft { get; set; }
        public int Seats { get; set; } = 4;

        [JsonIgnore]
        public bool Available => !Destroyed;
    }

    public sealed class Business
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public BusinessKind Kind { get; set; }
        public int Level { get; set; } = 1;
        public string TerritoryId { get; set; }
        public int AcquiredDay { get; set; }
    }

    public sealed class FlagDesign
    {
        public FlagPattern Pattern { get; set; } = FlagPattern.HorizontalTricolor;
        public string Color1 { get; set; } = "#1E1E1E";
        public string Color2 { get; set; } = "#B01E23";
        public string Color3 { get; set; } = "#F2F2F2";
        public EmblemType Emblem { get; set; } = EmblemType.Monogram;
        public string EmblemColor { get; set; } = "#F2C94C";
        public string MonogramText { get; set; } = "";
        /// <summary>PNG/JPG/DDS file name inside the mod's flags folder; overrides the pattern in the UI when set.</summary>
        public string CustomImage { get; set; } = "";
        /// <summary>In-game flag pole prop used physically in the world (GTA cannot texture props with custom images at runtime).</summary>
        public string WorldFlagModel { get; set; } = "prop_flag_sa";

        public FlagDesign Copy() => (FlagDesign)MemberwiseClone();
    }

    public sealed class Identity
    {
        public string PrimaryColor { get; set; } = "#B01E23";
        public string SecondaryColor { get; set; } = "#1E1E1E";
        public FlagDesign Flag { get; set; } = new FlagDesign();
        public EmblemType Emblem { get; set; } = EmblemType.Star;
        public string Tag { get; set; } = "";
        public string Motto { get; set; } = "";
        public string UniformPreset { get; set; } = "";
        public bool ApplyUniformToRecruits { get; set; }

        public Identity Copy()
        {
            var c = (Identity)MemberwiseClone();
            c.Flag = Flag.Copy();
            return c;
        }

        public Rgb Primary => Rgb.Parse(PrimaryColor, new Rgb(176, 30, 35));
        public Rgb Secondary => Rgb.Parse(SecondaryColor, new Rgb(30, 30, 30));
    }

    public sealed class IdentityPreset
    {
        public string Name { get; set; }
        public Identity Identity { get; set; }
    }

    public sealed class RecruitmentSettings
    {
        public bool Open { get; set; } = true;
        public bool AllowFemaleRecruits { get; set; } = true;
        public float MinimumWillingness { get; set; } = 0;
        public int HireCost { get; set; } = 2500;
        public MemberRole DefaultRole { get; set; } = MemberRole.Soldier;
    }

    /// <summary>The player's faction (and later, government).</summary>
    public sealed class FactionState
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public FactionType Type { get; set; }
        public Difficulty Difficulty { get; set; } = Difficulty.Normal;
        public FactionPersonality Personality { get; set; }
        public FactionBehavior Behavior { get; set; }
        public GangActivities Activities { get; set; }
        public int CreatedDay { get; set; }

        public Identity Identity { get; set; } = new Identity();
        public List<IdentityPreset> IdentityPresets { get; set; } = new List<IdentityPreset>();

        public Resources Resources { get; set; } = new Resources();
        public float Reputation { get; set; } = 10;
        public float Morale { get; set; } = 60;
        public float Discipline { get; set; } = 50;
        /// <summary>Militia: abstract suspicion level. Other types: police/public "heat".</summary>
        public float Suspicion { get; set; } = 5;
        public float PublicSupport { get; set; } = 10;

        public int Level { get; set; } = 1;
        public long Experience { get; set; }

        public List<Rank> Ranks { get; set; } = new List<Rank>();
        public List<Member> Members { get; set; } = new List<Member>();
        public List<Squad> Squads { get; set; } = new List<Squad>();
        public HeadquartersState Headquarters { get; set; }
        public List<FactionVehicle> Vehicles { get; set; } = new List<FactionVehicle>();
        public List<Business> Businesses { get; set; } = new List<Business>();
        public RecruitmentSettings Recruitment { get; set; } = new RecruitmentSettings();

        public TakeoverState Takeover { get; set; } = new TakeoverState();
        public GovernmentState Government { get; set; }

        public List<FactionEvent> Events { get; set; } = new List<FactionEvent>();
        public FactionStats Stats { get; set; } = new FactionStats();

        [JsonIgnore]
        public bool IsGovernment => Government != null && Government.Established;

        [JsonIgnore]
        public IEnumerable<Member> ActiveMembers => Members.Where(m => m.Status == MemberStatus.Active);

        [JsonIgnore]
        public int LivingMemberCount => Members.Count(m => m.Status != MemberStatus.Dead && m.Status != MemberStatus.Deserted);

        public Member GetMember(string id) => id == null ? null : Members.FirstOrDefault(m => m.Id == id);
        public Rank GetRank(string id) => id == null ? null : Ranks.FirstOrDefault(r => r.Id == id);
        public Squad GetSquad(string id) => id == null ? null : Squads.FirstOrDefault(s => s.Id == id);
        public FactionVehicle GetVehicle(string id) => id == null ? null : Vehicles.FirstOrDefault(v => v.Id == id);

        public Rank TopRank => Ranks.OrderBy(r => r.Order).FirstOrDefault();
        public Rank LowestRank => Ranks.OrderBy(r => r.Order).LastOrDefault();

        public string PlayerTitle
        {
            get
            {
                if (IsGovernment && !string.IsNullOrEmpty(Government.RulerTitle)) return Government.RulerTitle;
                return TopRank?.Name ?? "Leader";
            }
        }
    }
}
