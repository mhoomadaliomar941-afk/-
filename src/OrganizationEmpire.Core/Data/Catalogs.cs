using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Data
{
    public static class RankTemplates
    {
        public static List<Rank> For(FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia:
                    return Build(new[]
                    {
                        ("Don", RankPermission.All, 0),
                        ("Underboss", RankPermission.All, 900),
                        ("Consigliere", RankPermission.Negotiate | RankPermission.ManageResources | RankPermission.Promote | RankPermission.Leadership, 850),
                        ("Capo", RankPermission.Recruit | RankPermission.CommandSquads | RankPermission.LeadSquad | RankPermission.Armory, 500),
                        ("Soldier", RankPermission.LeadSquad | RankPermission.Armory, 260),
                        ("Associate", RankPermission.None, 120),
                    });
                case FactionType.Militia:
                    return Build(new[]
                    {
                        ("Commander", RankPermission.All, 0),
                        ("Deputy Commander", RankPermission.All, 800),
                        ("Unit Commander", RankPermission.Recruit | RankPermission.CommandSquads | RankPermission.LeadSquad | RankPermission.Promote | RankPermission.Armory, 520),
                        ("Officer", RankPermission.LeadSquad | RankPermission.Armory | RankPermission.Recruit, 360),
                        ("Member", RankPermission.Armory, 200),
                        ("Recruit", RankPermission.None, 90),
                    });
                default:
                    return Build(new[]
                    {
                        ("Leader", RankPermission.All, 0),
                        ("Deputy", RankPermission.All, 600),
                        ("Boss", RankPermission.Recruit | RankPermission.CommandSquads | RankPermission.LeadSquad | RankPermission.Promote | RankPermission.Armory, 380),
                        ("Senior Member", RankPermission.LeadSquad | RankPermission.Recruit | RankPermission.Armory, 240),
                        ("Member", RankPermission.Armory, 140),
                        ("Recruit", RankPermission.None, 60),
                    });
            }
        }

        public static List<Rank> Government()
        {
            return Build(new[]
            {
                ("Head of State", RankPermission.All, 0),
                ("Minister", RankPermission.All, 1200),
                ("General", RankPermission.Recruit | RankPermission.CommandSquads | RankPermission.LeadSquad | RankPermission.Promote | RankPermission.Armory, 900),
                ("Colonel", RankPermission.LeadSquad | RankPermission.CommandSquads | RankPermission.Armory | RankPermission.Recruit, 650),
                ("Officer", RankPermission.LeadSquad | RankPermission.Armory, 420),
                ("Trooper", RankPermission.Armory, 250),
            });
        }

        private static List<Rank> Build((string name, RankPermission perms, int salary)[] defs)
        {
            var list = new List<Rank>();
            for (int i = 0; i < defs.Length; i++)
            {
                list.Add(new Rank
                {
                    Id = Ids.New("rank"),
                    Name = defs[i].name,
                    Order = i,
                    Permissions = defs[i].perms,
                    Salary = defs[i].salary,
                    ReservedForPlayer = i == 0,
                });
            }
            return list;
        }
    }

    public sealed class UniformPreset
    {
        public string Id;
        public string Name;
        public FactionType? Type;
        public string[] Models;
    }

    public static class UniformCatalog
    {
        public static readonly UniformPreset[] All =
        {
            new UniformPreset { Id = "street_casual", Name = "Street Casual", Type = FactionType.StreetGang,
                Models = new[] { "a_m_y_stbla_01", "a_m_y_stbla_02", "a_m_y_stwhi_01", "a_m_y_stwhi_02", "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_y_soucent_03", "a_m_y_soucent_04" } },
            new UniformPreset { Id = "southside", Name = "Southside Hustlers", Type = FactionType.StreetGang,
                Models = new[] { "a_m_y_mexthug_01", "a_m_y_latino_01", "a_m_y_eastsa_01", "a_m_y_eastsa_02", "a_m_y_stlat_01" } },
            new UniformPreset { Id = "sharp_suits", Name = "Sharp Suits", Type = FactionType.Mafia,
                Models = new[] { "a_m_m_business_01", "a_m_y_business_01", "a_m_y_business_02", "a_m_y_business_03", "s_m_m_highsec_01", "s_m_m_highsec_02" } },
            new UniformPreset { Id = "old_money", Name = "Old Money", Type = FactionType.Mafia,
                Models = new[] { "a_m_m_bevhills_01", "a_m_m_bevhills_02", "a_m_y_bevhills_01", "s_m_m_bouncer_01" } },
            new UniformPreset { Id = "black_ops", Name = "Tactical Black", Type = FactionType.Militia,
                Models = new[] { "s_m_y_blackops_01", "s_m_y_blackops_02", "s_m_y_blackops_03" } },
            new UniformPreset { Id = "woodland", Name = "Woodland Fatigues", Type = FactionType.Militia,
                Models = new[] { "s_m_y_marine_01", "s_m_y_marine_03", "s_m_m_marine_01" } },
            new UniformPreset { Id = "irregulars", Name = "Blaine County Irregulars", Type = FactionType.Militia,
                Models = new[] { "a_m_m_salton_01", "a_m_m_salton_02", "a_m_y_salton_01", "a_m_m_hillbilly_01" } },
            new UniformPreset { Id = "state_forces", Name = "State Forces", Type = null,
                Models = new[] { "s_m_y_swat_01", "s_m_y_marine_01", "s_m_y_marine_03", "s_m_m_marine_01" } },
        };

        public static UniformPreset Get(string id) => All.FirstOrDefault(u => u.Id == id);

        public static UniformPreset DefaultFor(FactionType type) => All.First(u => u.Type == type);

        public static IEnumerable<UniformPreset> ForType(FactionType type) => All.Where(u => u.Type == null || u.Type == type);
    }

    public sealed class LoadoutDef
    {
        public LoadoutType Type;
        public string[] Weapons;
        public int ArmsCost;
        public int Armor;
    }

    public static class LoadoutCatalog
    {
        public static LoadoutDef Get(LoadoutType type, FactionType faction)
        {
            switch (type)
            {
                case LoadoutType.Unarmed: return new LoadoutDef { Type = type, Weapons = new string[0], ArmsCost = 0 };
                case LoadoutType.Melee:
                    return new LoadoutDef { Type = type, Weapons = new[] { faction == FactionType.Mafia ? "WEAPON_BAT" : faction == FactionType.Militia ? "WEAPON_KNIFE" : "WEAPON_BAT" }, ArmsCost = 0 };
                case LoadoutType.Sidearm:
                    return new LoadoutDef { Type = type, Weapons = new[] { faction == FactionType.Militia ? "WEAPON_COMBATPISTOL" : "WEAPON_PISTOL" }, ArmsCost = 1 };
                case LoadoutType.SMG:
                    return new LoadoutDef { Type = type, Weapons = new[] { faction == FactionType.StreetGang ? "WEAPON_MICROSMG" : "WEAPON_SMG", "WEAPON_PISTOL" }, ArmsCost = 3, Armor = 20 };
                case LoadoutType.Shotgun:
                    return new LoadoutDef { Type = type, Weapons = new[] { faction == FactionType.StreetGang ? "WEAPON_SAWNOFFSHOTGUN" : "WEAPON_PUMPSHOTGUN", "WEAPON_PISTOL" }, ArmsCost = 3, Armor = 20 };
                case LoadoutType.Rifle:
                    return new LoadoutDef { Type = type, Weapons = new[] { faction == FactionType.Militia ? "WEAPON_CARBINERIFLE" : "WEAPON_ASSAULTRIFLE", "WEAPON_PISTOL" }, ArmsCost = 5, Armor = 50 };
                case LoadoutType.Marksman:
                    return new LoadoutDef { Type = type, Weapons = new[] { "WEAPON_SNIPERRIFLE", "WEAPON_COMBATPISTOL" }, ArmsCost = 6, Armor = 30 };
                case LoadoutType.Heavy:
                    return new LoadoutDef { Type = type, Weapons = new[] { "WEAPON_COMBATMG", "WEAPON_PISTOL" }, ArmsCost = 8, Armor = 80 };
                default:
                    return new LoadoutDef { Type = LoadoutType.Sidearm, Weapons = new[] { "WEAPON_PISTOL" }, ArmsCost = 1 };
            }
        }

        public static int MinLevel(LoadoutType type)
        {
            switch (type)
            {
                case LoadoutType.SMG: return 2;
                case LoadoutType.Shotgun: return 2;
                case LoadoutType.Rifle: return 4;
                case LoadoutType.Marksman: return 6;
                case LoadoutType.Heavy: return 8;
                default: return 1;
            }
        }
    }

    public sealed class DifficultyProfile
    {
        public float Income, Upkeep, RivalAggression, RecruitBonus, Suspicion, LoyaltyDrift, StartingCash;

        public static DifficultyProfile For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return new DifficultyProfile { Income = 1.3f, Upkeep = 0.8f, RivalAggression = 0.7f, RecruitBonus = 10, Suspicion = 0.7f, LoyaltyDrift = 0.7f, StartingCash = 1.5f };
                case Difficulty.Hard: return new DifficultyProfile { Income = 0.85f, Upkeep = 1.15f, RivalAggression = 1.25f, RecruitBonus = -8, Suspicion = 1.25f, LoyaltyDrift = 1.2f, StartingCash = 0.75f };
                case Difficulty.Brutal: return new DifficultyProfile { Income = 0.7f, Upkeep = 1.3f, RivalAggression = 1.5f, RecruitBonus = -15, Suspicion = 1.5f, LoyaltyDrift = 1.4f, StartingCash = 0.5f };
                default: return new DifficultyProfile { Income = 1f, Upkeep = 1f, RivalAggression = 1f, RecruitBonus = 0, Suspicion = 1f, LoyaltyDrift = 1f, StartingCash = 1f };
            }
        }
    }

    public static class FactionTypeInfo
    {
        public static string[] Tiers(FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return new[] { "Crew", "Family", "Borgata", "Syndicate", "The Commission" };
                case FactionType.Militia: return new[] { "Cell", "Movement", "Front", "Liberation Army", "National Force" };
                default: return new[] { "Street Crew", "Set", "Gang", "Syndicate", "Street Empire" };
            }
        }

        public static string TierName(FactionState f)
        {
            var tiers = Tiers(f.Type);
            int idx = MathEx.Clamp((f.Level - 1) / 4, 0, tiers.Length - 1);
            return tiers[idx];
        }

        public static string TypeName(FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return "Mafia Family";
                case FactionType.Militia: return "Organization / Militia";
                default: return "Street Gang";
            }
        }

        public static string TypeBlurb(FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia:
                    return "A hierarchical crime family. Earn through businesses and protection, build Influence, negotiate with rival families and manage internal politics. Takeover path: become the shadow power behind the state.";
                case FactionType.Militia:
                    return "A structured organization with units and a command chain. Grow in secret, manage Suspicion, win public support and seize strategic sites. Has the strongest path to take over the state.";
                default:
                    return "A street gang built on territory and reputation. Fight rival sets block by block, run side activities, keep the crew loyal. Taking over the state is possible but slow and hard.";
            }
        }

        public static string SuspicionLabel(FactionType type) => type == FactionType.Militia ? "Suspicion" : "Heat";

        public static float TakeoverSpeed(FactionType type)
        {
            switch (type)
            {
                case FactionType.Militia: return 1.3f;
                case FactionType.Mafia: return 1.0f;
                default: return 0.7f;
            }
        }

        public static float RecruitBase(FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return 25;
                case FactionType.Militia: return 30;
                default: return 35;
            }
        }

        public static long XpForLevel(int level)
        {
            // XP needed to go from level-1 to level. Level 2 = 500, grows ~1.55 power.
            if (level <= 1) return 0;
            return (long)(500 * System.Math.Pow(level - 1, 1.55));
        }

        public static Personality RandomPersonality(Rng rng, FactionType type)
        {
            Personality[] weighted;
            switch (type)
            {
                case FactionType.Mafia:
                    weighted = new[] { Personality.Calm, Personality.Calm, Personality.Intelligent, Personality.Intelligent, Personality.Ambitious, Personality.Ambitious, Personality.Loyal, Personality.Loyal, Personality.Sarcastic, Personality.Aggressive, Personality.Nervous, Personality.Disciplined, Personality.Rebellious, Personality.Cowardly };
                    break;
                case FactionType.Militia:
                    weighted = new[] { Personality.Disciplined, Personality.Disciplined, Personality.Disciplined, Personality.Loyal, Personality.Loyal, Personality.Calm, Personality.Intelligent, Personality.Aggressive, Personality.Ambitious, Personality.Nervous, Personality.Rebellious, Personality.Sarcastic, Personality.Cowardly };
                    break;
                default:
                    weighted = new[] { Personality.Aggressive, Personality.Aggressive, Personality.Sarcastic, Personality.Sarcastic, Personality.Rebellious, Personality.Rebellious, Personality.Loyal, Personality.Loyal, Personality.Ambitious, Personality.Nervous, Personality.Calm, Personality.Cowardly, Personality.Intelligent, Personality.Disciplined };
                    break;
            }
            return rng.Pick(weighted);
        }
    }

    public sealed class BusinessDef
    {
        public BusinessKind Kind;
        public string Name;
        public int Price;
        public int DailyIncome;
        public float HeatPerDay;
    }

    public static class BusinessCatalog
    {
        public static readonly BusinessDef[] All =
        {
            new BusinessDef { Kind = BusinessKind.Bodega, Name = "Corner Bodega", Price = 15000, DailyIncome = 900, HeatPerDay = 0.1f },
            new BusinessDef { Kind = BusinessKind.BarberShop, Name = "Barber Shop", Price = 20000, DailyIncome = 1100, HeatPerDay = 0.1f },
            new BusinessDef { Kind = BusinessKind.Laundromat, Name = "Laundromat", Price = 30000, DailyIncome = 1500, HeatPerDay = 0.15f },
            new BusinessDef { Kind = BusinessKind.Restaurant, Name = "Italian Restaurant", Price = 45000, DailyIncome = 2200, HeatPerDay = 0.1f },
            new BusinessDef { Kind = BusinessKind.TowingCompany, Name = "Towing Company", Price = 50000, DailyIncome = 2400, HeatPerDay = 0.2f },
            new BusinessDef { Kind = BusinessKind.ConstructionFirm, Name = "Construction Firm", Price = 80000, DailyIncome = 3800, HeatPerDay = 0.2f },
            new BusinessDef { Kind = BusinessKind.Nightclub, Name = "Nightclub", Price = 120000, DailyIncome = 5500, HeatPerDay = 0.35f },
            new BusinessDef { Kind = BusinessKind.CarDealership, Name = "Car Dealership", Price = 140000, DailyIncome = 6000, HeatPerDay = 0.25f },
            new BusinessDef { Kind = BusinessKind.ShippingCompany, Name = "Shipping Company", Price = 200000, DailyIncome = 8500, HeatPerDay = 0.4f },
            new BusinessDef { Kind = BusinessKind.Casino, Name = "Card Room & Casino", Price = 300000, DailyIncome = 12500, HeatPerDay = 0.5f },
        };

        public static BusinessDef Get(BusinessKind kind) => All.First(b => b.Kind == kind);
    }

    public sealed class HqUpgradeDef
    {
        public HqUpgrade Upgrade;
        public string Name;
        public string Effect;
        public int BaseCost;
        public int MaxLevel;
    }

    public static class HqUpgradeCatalog
    {
        public static readonly HqUpgradeDef[] All =
        {
            new HqUpgradeDef { Upgrade = HqUpgrade.Armory, Name = "Armory", Effect = "+Arms storage, cheaper loadouts, +guard weapons", BaseCost = 15000, MaxLevel = 3 },
            new HqUpgradeDef { Upgrade = HqUpgrade.Barracks, Name = "Barracks", Effect = "+6 roster capacity per level, +morale", BaseCost = 20000, MaxLevel = 3 },
            new HqUpgradeDef { Upgrade = HqUpgrade.Garage, Name = "Garage", Effect = "+2 faction vehicles per level, faster repairs", BaseCost = 18000, MaxLevel = 3 },
            new HqUpgradeDef { Upgrade = HqUpgrade.Comms, Name = "Comms Room", Effect = "+Intel per day, earlier attack warnings", BaseCost = 16000, MaxLevel = 3 },
            new HqUpgradeDef { Upgrade = HqUpgrade.Fortifications, Name = "Fortifications", Effect = "+Defense vs attacks and raids", BaseCost = 25000, MaxLevel = 3 },
            new HqUpgradeDef { Upgrade = HqUpgrade.Infirmary, Name = "Infirmary", Effect = "Wounded recover faster, fewer deaths", BaseCost = 14000, MaxLevel = 3 },
        };

        public static HqUpgradeDef Get(HqUpgrade u) => All.First(x => x.Upgrade == u);

        public static int Cost(HqUpgrade u, int currentLevel) => Get(u).BaseCost * (currentLevel + 1);
    }

    public static class GovernmentCatalog
    {
        public static List<Ministry> DefaultMinistries()
        {
            return new List<Ministry>
            {
                new Ministry { Id = "defense", Name = "Ministry of Defense", Description = "Raises force readiness and defends state territory." },
                new Ministry { Id = "interior", Name = "Ministry of the Interior", Description = "Keeps order; raises stability." },
                new Ministry { Id = "treasury", Name = "Treasury", Description = "Improves tax collection." },
                new Ministry { Id = "intelligence", Name = "State Intelligence", Description = "Suppresses insurgency, gathers intel." },
                new Ministry { Id = "justice", Name = "Ministry of Justice", Description = "Builds legitimacy." },
                new Ministry { Id = "information", Name = "Ministry of Information", Description = "Improves public support." },
                new Ministry { Id = "publicworks", Name = "Public Works", Description = "Raises support at a treasury cost." },
                new Ministry { Id = "foreign", Name = "Foreign Relations", Description = "Calms remaining factions' hostility." },
            };
        }

        public static readonly (string id, string name, string effect)[] Policies =
        {
            ("martial_law", "Martial Law", "+Stability, +Readiness, -Support, -Legitimacy"),
            ("curfew", "Curfew", "-Insurgency, -Support"),
            ("amnesty", "General Amnesty", "+Support, +Legitimacy, +Insurgency"),
            ("public_works", "Public Works Program", "+Support, -Treasury"),
            ("free_trade", "Open Trade Zones", "+Treasury, +Insurgency"),
            ("state_media", "State Media", "+Support, -Legitimacy"),
            ("conscription", "Conscription", "+Readiness, -Support"),
        };

        public static string RulerTitleFor(FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return "Chairman";
                case FactionType.Militia: return "Supreme Commander";
                default: return "President";
            }
        }

        public static GovernmentType DefaultTypeFor(FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return GovernmentType.Syndicate;
                case FactionType.Militia: return GovernmentType.Junta;
                default: return GovernmentType.People;
            }
        }
    }

    public static class FlagCatalog
    {
        /// <summary>Flag pole props present in the base game / DLCs. Validated at runtime with IS_MODEL_IN_CDIMAGE.</summary>
        public static readonly string[] WorldFlagModels =
        {
            "prop_flag_sa", "prop_flag_ls", "prop_flag_us", "prop_flag_uk", "prop_flag_canada", "prop_flag_mexico",
            "prop_flag_japan", "prop_flag_france", "prop_flag_german", "prop_flag_ireland", "prop_flag_scotland",
            "prop_flag_eu", "prop_flag_russia", "prop_flag_sweden", "prop_flag_brazil", "prop_flag_lsfd", "prop_flag_lsservices",
            "prop_flag_sheriff", "prop_flag_sapd", "apa_prop_flag_italy", "apa_prop_flag_spain", "apa_prop_flag_china",
            "apa_prop_flag_jamaica", "apa_prop_flag_southafrica", "apa_prop_flag_nigeria", "apa_prop_flag_turkey",
            "apa_prop_flag_poland", "apa_prop_flag_netherlands", "apa_prop_flag_portugal", "apa_prop_flag_puertorico",
            "apa_prop_flag_argentina", "apa_prop_flag_columbia",
        };

        public static string WorldFlagLabel(string model)
        {
            if (string.IsNullOrEmpty(model)) return "None";
            string s = model.Replace("apa_prop_flag_", "").Replace("prop_flag_", "");
            switch (s)
            {
                case "sa": return "San Andreas";
                case "ls": return "Los Santos";
                case "us": return "United States";
                case "uk": return "United Kingdom";
                case "eu": return "European Union";
                case "lsfd": return "LS Fire Dept.";
                case "lsservices": return "LS Services";
                case "sapd": return "SA Police";
                case "german": return "Germany";
                case "columbia": return "Colombia";
                case "puertorico": return "Puerto Rico";
                case "southafrica": return "South Africa";
                default: return char.ToUpperInvariant(s[0]) + s.Substring(1);
            }
        }

        public static readonly string[] Palette =
        {
            "#B01E23", "#E74C3C", "#D35400", "#F39C12", "#F1C40F", "#27AE60", "#1E8449", "#16A085", "#00BCD4", "#2E86DE",
            "#1F3A93", "#8E44AD", "#6C3483", "#E91E63", "#F2F2F2", "#BDC3C7", "#7F8C8D", "#34495E", "#1E1E1E", "#000000",
            "#6D4C41", "#C8A165", "#556B2F", "#3B3B2E",
        };

        public static FlagDesign DefaultFor(FactionType type, string primary, string secondary)
        {
            switch (type)
            {
                case FactionType.Mafia:
                    return new FlagDesign { Pattern = FlagPattern.Bordered, Color1 = secondary, Color2 = primary, Color3 = "#C8A165", Emblem = EmblemType.Crown, EmblemColor = "#C8A165", WorldFlagModel = "apa_prop_flag_italy" };
                case FactionType.Militia:
                    return new FlagDesign { Pattern = FlagPattern.CenterBand, Color1 = "#3B3B2E", Color2 = primary, Color3 = secondary, Emblem = EmblemType.Star, EmblemColor = "#F2F2F2", WorldFlagModel = "prop_flag_sa" };
                default:
                    return new FlagDesign { Pattern = FlagPattern.HorizontalBicolor, Color1 = primary, Color2 = secondary, Color3 = "#F2F2F2", Emblem = EmblemType.Spade, EmblemColor = "#F2F2F2", WorldFlagModel = "prop_flag_ls" };
            }
        }
    }
}
