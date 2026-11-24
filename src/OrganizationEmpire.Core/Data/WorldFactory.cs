using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Data
{
    /// <summary>
    /// Builds the default world: territories keyed by GTA V map zone codes and the in-world factions of Los Santos and Blaine County.
    /// Centers are approximate and are verified/corrected at runtime against the live zone map.
    /// </summary>
    public static class WorldFactory
    {
        private sealed class T
        {
            public string Id, Name, Owner;
            public string[] Zones;
            public float X, Y, Z, Radius;
            public TerritoryType Type;
            public int Value;
        }

        private static readonly T[] TerritoryDefs =
        {
            // ---- South Los Santos
            new T { Id = "davis", Name = "Davis", Zones = new[] { "DAVIS" }, X = 85, Y = -1820, Z = 28, Radius = 330, Type = TerritoryType.Residential, Value = 1500, Owner = "ballas" },
            new T { Id = "strawberry", Name = "Strawberry", Zones = new[] { "STRAW" }, X = -40, Y = -1420, Z = 29, Radius = 300, Type = TerritoryType.Residential, Value = 1500, Owner = "families" },
            new T { Id = "chamberlain", Name = "Chamberlain Hills", Zones = new[] { "CHAMH" }, X = -170, Y = -1600, Z = 34, Radius = 260, Type = TerritoryType.Residential, Value = 1400, Owner = "families" },
            new T { Id = "rancho", Name = "Rancho", Zones = new[] { "RANCHO" }, X = 430, Y = -1760, Z = 29, Radius = 330, Type = TerritoryType.Residential, Value = 1500, Owner = "vagos" },
            new T { Id = "cypress", Name = "Cypress Flats", Zones = new[] { "CYPRE" }, X = 850, Y = -2150, Z = 30, Radius = 360, Type = TerritoryType.Industrial, Value = 2400, Owner = "vagos" },
            new T { Id = "elburro", Name = "El Burro Heights", Zones = new[] { "EBURO" }, X = 1380, Y = -1700, Z = 60, Radius = 380, Type = TerritoryType.Residential, Value = 1300, Owner = "marabunta" },
            new T { Id = "murrieta", Name = "Murrieta Heights", Zones = new[] { "MURRI" }, X = 1140, Y = -1350, Z = 35, Radius = 300, Type = TerritoryType.Industrial, Value = 2000, Owner = "marabunta" },
            new T { Id = "lamesa", Name = "La Mesa", Zones = new[] { "LMESA" }, X = 800, Y = -1100, Z = 28, Radius = 330, Type = TerritoryType.Industrial, Value = 2200, Owner = null },
            new T { Id = "banning", Name = "Banning", Zones = new[] { "BANNING" }, X = -50, Y = -2400, Z = 6, Radius = 320, Type = TerritoryType.Industrial, Value = 2000, Owner = null },
            new T { Id = "elysian", Name = "Elysian Island", Zones = new[] { "ELYSIAN" }, X = 200, Y = -2850, Z = 6, Radius = 450, Type = TerritoryType.Industrial, Value = 2600, Owner = "merryweather" },
            new T { Id = "port", Name = "Port of Los Santos", Zones = new[] { "TERMINA", "ZP_ORT" }, X = 1000, Y = -3000, Z = 6, Radius = 550, Type = TerritoryType.Strategic, Value = 5200, Owner = FactionIds.State },
            new T { Id = "airport", Name = "Los Santos International", Zones = new[] { "AIRP" }, X = -1150, Y = -2700, Z = 14, Radius = 700, Type = TerritoryType.Strategic, Value = 5500, Owner = FactionIds.State },

            // ---- Central / East Los Santos
            new T { Id = "missionrow", Name = "Mission Row", Zones = new[] { "SKID" }, X = 420, Y = -990, Z = 30, Radius = 230, Type = TerritoryType.Strategic, Value = 4200, Owner = FactionIds.State },
            new T { Id = "textile", Name = "Textile City", Zones = new[] { "TEXTI" }, X = 420, Y = -760, Z = 29, Radius = 200, Type = TerritoryType.Commercial, Value = 2600, Owner = "triads" },
            new T { Id = "legion", Name = "Legion Square", Zones = new[] { "LEGSQU" }, X = 195, Y = -935, Z = 30, Radius = 180, Type = TerritoryType.Commercial, Value = 3200, Owner = null },
            new T { Id = "pillbox", Name = "Pillbox Hill", Zones = new[] { "PBOX" }, X = -60, Y = -800, Z = 44, Radius = 260, Type = TerritoryType.Strategic, Value = 6000, Owner = FactionIds.State },
            new T { Id = "littleseoul", Name = "Little Seoul", Zones = new[] { "KOREAT" }, X = -650, Y = -950, Z = 21, Radius = 330, Type = TerritoryType.Commercial, Value = 2800, Owner = "kkangpae" },
            new T { Id = "mirrorpark", Name = "Mirror Park", Zones = new[] { "MIRR" }, X = 1100, Y = -550, Z = 57, Radius = 330, Type = TerritoryType.Residential, Value = 1800, Owner = null },
            new T { Id = "eastvinewood", Name = "East Vinewood", Zones = new[] { "EAST_V" }, X = 900, Y = -180, Z = 73, Radius = 330, Type = TerritoryType.Residential, Value = 1700, Owner = "lost" },
            new T { Id = "vinewood", Name = "Vinewood", Zones = new[] { "VINE", "DTVINE" }, X = 300, Y = 180, Z = 104, Radius = 360, Type = TerritoryType.Commercial, Value = 3300, Owner = null },
            new T { Id = "alta", Name = "Alta & Hawick", Zones = new[] { "ALTA", "HAWICK" }, X = 260, Y = -220, Z = 54, Radius = 300, Type = TerritoryType.Residential, Value = 2000, Owner = null },
            new T { Id = "burton", Name = "Burton", Zones = new[] { "BURTON" }, X = -420, Y = -100, Z = 38, Radius = 280, Type = TerritoryType.Commercial, Value = 3000, Owner = "armenian" },
            new T { Id = "rockford", Name = "Rockford Hills", Zones = new[] { "ROCKF" }, X = -840, Y = -160, Z = 37, Radius = 400, Type = TerritoryType.Strategic, Value = 5800, Owner = FactionIds.State },

            // ---- West Los Santos
            new T { Id = "vespucci", Name = "Vespucci", Zones = new[] { "VESP", "VCANA" }, X = -1120, Y = -1300, Z = 5, Radius = 420, Type = TerritoryType.Residential, Value = 1900, Owner = "aztecas" },
            new T { Id = "delperro", Name = "Del Perro", Zones = new[] { "DELPE", "DELBE" }, X = -1500, Y = -600, Z = 34, Radius = 420, Type = TerritoryType.Commercial, Value = 3000, Owner = null },
            new T { Id = "morningwood", Name = "Morningwood", Zones = new[] { "MORN" }, X = -1350, Y = -300, Z = 40, Radius = 300, Type = TerritoryType.Residential, Value = 2200, Owner = null },

            // ---- Blaine County
            new T { Id = "sandy", Name = "Sandy Shores", Zones = new[] { "SANDY" }, X = 1850, Y = 3700, Z = 33, Radius = 520, Type = TerritoryType.Rural, Value = 1100, Owner = "lost" },
            new T { Id = "stabcity", Name = "Stab City", Zones = new[] { "SLAB" }, X = 70, Y = 3700, Z = 39, Radius = 250, Type = TerritoryType.Rural, Value = 700, Owner = "lost" },
            new T { Id = "grapeseed", Name = "Grapeseed", Zones = new[] { "GRAPES" }, X = 1900, Y = 4800, Z = 45, Radius = 600, Type = TerritoryType.Rural, Value = 1000, Owner = "oneil" },
            new T { Id = "harmony", Name = "Harmony", Zones = new[] { "HARMO" }, X = 550, Y = 2700, Z = 42, Radius = 450, Type = TerritoryType.Rural, Value = 900, Owner = "madrazo" },
            new T { Id = "senora", Name = "Grand Senora Desert", Zones = new[] { "DESRT" }, X = 1500, Y = 2250, Z = 60, Radius = 900, Type = TerritoryType.Rural, Value = 1100, Owner = "madrazo" },
            new T { Id = "paleto", Name = "Paleto Bay", Zones = new[] { "PALETO" }, X = -200, Y = 6300, Z = 31, Radius = 520, Type = TerritoryType.Rural, Value = 1300, Owner = null },
            new T { Id = "chaparral", Name = "Great Chaparral", Zones = new[] { "GREATC" }, X = -600, Y = 2000, Z = 130, Radius = 700, Type = TerritoryType.Rural, Value = 800, Owner = null },
            new T { Id = "powerstation", Name = "Palmer-Taylor Power Station", Zones = new[] { "PALMPOW" }, X = 2700, Y = 1550, Z = 24, Radius = 450, Type = TerritoryType.Strategic, Value = 4600, Owner = FactionIds.State },
            new T { Id = "zancudo", Name = "Fort Zancudo", Zones = new[] { "ARMYB" }, X = -2200, Y = 3100, Z = 32, Radius = 800, Type = TerritoryType.Strategic, Value = 6500, Owner = FactionIds.State },
            new T { Id = "bolingbroke", Name = "Bolingbroke Penitentiary", Zones = new[] { "JAIL" }, X = 1700, Y = 2580, Z = 45, Radius = 320, Type = TerritoryType.Strategic, Value = 3800, Owner = FactionIds.State },
            new T { Id = "noose", Name = "N.O.O.S.E. Headquarters", Zones = new[] { "NOOSE" }, X = 2500, Y = -400, Z = 93, Radius = 320, Type = TerritoryType.Strategic, Value = 5000, Owner = FactionIds.State },
        };

        public static List<RivalFaction> CreateDefaultRivals()
        {
            return new List<RivalFaction>
            {
                Rival("ballas", "Ballas", RivalKind.StreetGang, "#8E44AD", 45, 0.65f,
                    new[] { "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01" },
                    new[] { "buccanee", "voodoo", "baller" }, "AMBIENT_GANG_BALLAS", "WEAPON_MICROSMG",
                    "Purple-clad street set entrenched in Davis."),
                Rival("families", "The Families", RivalKind.StreetGang, "#27AE60", 40, 0.5f,
                    new[] { "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01" },
                    new[] { "emperor", "primo", "chino" }, "AMBIENT_GANG_FAMILY", "WEAPON_PISTOL",
                    "Old-school green set holding Forum Drive and Chamberlain Hills."),
                Rival("vagos", "Los Santos Vagos", RivalKind.StreetGang, "#F1C40F", 50, 0.6f,
                    new[] { "g_m_y_mexgang_01", "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03" },
                    new[] { "tornado", "peyote", "buccanee" }, "AMBIENT_GANG_MEXICAN", "WEAPON_MICROSMG",
                    "Yellow-flagged crew running Rancho and Cypress Flats."),
                Rival("marabunta", "Marabunta Grande", RivalKind.StreetGang, "#2E86DE", 42, 0.6f,
                    new[] { "g_m_y_salvaboss_01", "g_m_y_salvagoon_01", "g_m_y_salvagoon_02", "g_m_y_salvagoon_03" },
                    new[] { "faction", "manana" }, "AMBIENT_GANG_SALVA", "WEAPON_PISTOL",
                    "Blue-ink gang along the river in El Burro and Murrieta."),
                Rival("aztecas", "Varrios Los Aztecas", RivalKind.StreetGang, "#00BCD4", 35, 0.55f,
                    new[] { "g_m_y_azteca_01" },
                    new[] { "chino", "emperor" }, null, "WEAPON_PISTOL",
                    "Small but fierce set holding pieces of Vespucci."),
                Rival("lost", "The Lost MC", RivalKind.Biker, "#A04000", 55, 0.6f,
                    new[] { "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_lost_03" },
                    new[] { "daemon", "hexer", "gburrito" }, "AMBIENT_GANG_LOST", "WEAPON_SAWNOFFSHOTGUN",
                    "Outlaw biker club with chapters in East Vinewood and Blaine County."),
                Rival("kkangpae", "Kkangpae", RivalKind.StreetGang, "#E74C3C", 38, 0.45f,
                    new[] { "g_m_y_korean_01", "g_m_y_korean_02", "g_m_y_korlieut_01" },
                    new[] { "sultan", "futo" }, null, "WEAPON_PISTOL",
                    "Korean street syndicate controlling Little Seoul."),
                Rival("triads", "Wei Cheng Triads", RivalKind.Mafia, "#C0392B", 60, 0.4f,
                    new[] { "g_m_m_chigoon_01", "g_m_m_chigoon_02", "g_m_m_chiboss_01" },
                    new[] { "fugitive", "washington" }, "AMBIENT_GANG_WEICHENG", "WEAPON_SMG",
                    "Disciplined triad with business interests across downtown."),
                Rival("armenian", "Armenian Mob", RivalKind.Mafia, "#7F8C8D", 50, 0.45f,
                    new[] { "g_m_m_armgoon_01", "g_m_m_armlieut_01", "g_m_y_armgoon_02", "g_m_m_armboss_01" },
                    new[] { "schafter2", "oracle" }, null, "WEAPON_PISTOL",
                    "Car dealerships, repo rackets and favors in Burton."),
                Rival("madrazo", "Madrazo Cartel", RivalKind.Cartel, "#D35400", 70, 0.5f,
                    new[] { "g_m_m_mexboss_01", "g_m_m_mexboss_02", "a_m_m_mexcntry_01" },
                    new[] { "cavalcade", "granger" }, null, "WEAPON_ASSAULTRIFLE",
                    "Wealthy cartel operating from the desert ranches."),
                Rival("oneil", "O'Neil Brothers", RivalKind.Biker, "#6D4C41", 30, 0.55f,
                    new[] { "a_m_m_hillbilly_01", "a_m_m_hillbilly_02" },
                    new[] { "bodhi2", "rebel" }, "AMBIENT_GANG_HILLBILLY", "WEAPON_PUMPSHOTGUN",
                    "Paranoid backwoods clan farming Grapeseed."),
                Rival("merryweather", "Merryweather Security", RivalKind.Paramilitary, "#34495E", 75, 0.35f,
                    new[] { "s_m_y_blackops_01", "s_m_y_blackops_02", "s_m_y_blackops_03" },
                    new[] { "mesa3", "crusader" }, null, "WEAPON_CARBINERIFLE",
                    "Private military contractor guarding the docks for the highest bidder."),
            };
        }

        private static RivalFaction Rival(string id, string name, RivalKind kind, string color, float strength, float aggression,
            string[] peds, string[] vehicles, string vanillaGroup, string weapon, string description)
        {
            return new RivalFaction
            {
                Id = id,
                Name = name,
                Kind = kind,
                Color = color,
                Strength = strength,
                Wealth = strength,
                Aggression = aggression,
                Hostility = 15,
                PedModels = peds.ToList(),
                VehicleModels = vehicles.ToList(),
                VanillaRelationshipGroup = vanillaGroup,
                Weapon = weapon,
                Description = description,
            };
        }

        public static List<Territory> CreateDefaultTerritories()
        {
            var list = new List<Territory>();
            foreach (var d in TerritoryDefs)
            {
                var t = new Territory
                {
                    Id = d.Id,
                    Name = d.Name,
                    Zones = d.Zones.ToList(),
                    Center = new Vec3(d.X, d.Y, d.Z),
                    Radius = d.Radius,
                    Type = d.Type,
                    BaseValue = d.Value,
                    OwnerId = d.Owner,
                };
                if (d.Owner != null) t.Influence[d.Owner] = d.Owner == FactionIds.State ? 85 : 70;
                list.Add(t);
            }
            return list;
        }

        public static WorldState CreateDefaultWorld(int seed)
        {
            var world = new WorldState
            {
                Seed = seed,
                Territories = CreateDefaultTerritories(),
                Rivals = CreateDefaultRivals(),
                State = new StateAuthority(),
            };
            foreach (var r in world.Rivals)
            {
                r.HomeTerritories = world.Territories.Where(t => t.OwnerId == r.Id).Select(t => t.Id).ToList();
            }
            return world;
        }

        /// <summary>Restores the default rival roster (used by Settings &gt; World &gt; Restore default factions).</summary>
        public static void RestoreDefaultRivals(WorldState world)
        {
            var defaults = CreateDefaultRivals();
            foreach (var d in defaults)
            {
                var existing = world.GetRival(d.Id);
                if (existing == null)
                {
                    d.HomeTerritories = CreateDefaultTerritories().Where(t => t.OwnerId == d.Id).Select(t => t.Id).ToList();
                    world.Rivals.Add(d);
                    existing = d;
                }
                existing.Enabled = true;
                if (existing.Eliminated)
                {
                    existing.Eliminated = false;
                    existing.Strength = d.Strength * 0.6f;
                }
                foreach (var tid in existing.HomeTerritories)
                {
                    var t = world.GetTerritory(tid);
                    if (t != null && t.OwnerId == null)
                    {
                        Systems.TerritorySystem.AddInfluence(t, existing.Id, 40);
                        Systems.TerritorySystem.UpdateOwnership(t);
                    }
                }
            }
        }

        /// <summary>State security forces used in raids and campaign missions.</summary>
        public static readonly string[] StateForcePeds = { "s_m_y_swat_01", "s_m_y_cop_01", "s_m_y_sheriff_01", "s_m_y_marine_01", "s_m_y_marine_03" };
        public static readonly string[] StateForceVehicles = { "police3", "sheriff2", "fbi2", "riot" };
    }
}
