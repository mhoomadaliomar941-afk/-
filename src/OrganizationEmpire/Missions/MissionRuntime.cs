using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.World;

namespace OrganizationEmpire.Missions
{
    internal static class MissionLocations
    {
        public struct Spot
        {
            public string Name;
            public Vector3 Pos;
            public Spot(string n, float x, float y, float z) { Name = n; Pos = new Vector3(x, y, z); }
        }

        public static readonly Spot[] Businesses =
        {
            new Spot("24/7, Innocence Blvd", 25.7f, -1345.5f, 29.5f),
            new Spot("LTD, Grove Street", -47.0f, -1757.0f, 29.4f),
            new Spot("Rob's Liquor, Prosperity St", -1487.5f, -378.0f, 40.2f),
            new Spot("Rob's Liquor, San Andreas Ave", -1222.0f, -907.0f, 12.3f),
            new Spot("Rob's Liquor, El Rancho Blvd", 1135.8f, -982.3f, 46.4f),
            new Spot("24/7, Clinton Ave", 373.6f, 326.0f, 103.6f),
            new Spot("LTD, Mirror Park", 1163.4f, -323.8f, 69.2f),
            new Spot("LTD, Little Seoul", -707.5f, -914.3f, 19.2f),
            new Spot("Barber, Davis", 136.8f, -1708.4f, 29.3f),
            new Spot("Barber, Mirror Park", 1212.8f, -472.9f, 66.2f),
            new Spot("Tattoo Parlor, Vinewood", 322.1f, 180.4f, 103.6f),
            new Spot("Ammu-Nation, Cypress Flats", 810.2f, -2157.3f, 29.6f),
            new Spot("24/7, Sandy Shores", 1961.5f, 3740.5f, 32.3f),
            new Spot("Liquor Ace, Sandy Shores", 1392.0f, 3604.0f, 34.9f),
            new Spot("LTD, Grapeseed", 1697.0f, 4923.0f, 42.0f),
            new Spot("24/7, Mount Chiliad", 1729.2f, 6414.1f, 35.0f),
        };

        public static readonly Spot[] Pickups =
        {
            new Spot("Elysian Island docks", 130f, -2820f, 6f),
            new Spot("Cypress Flats warehouse", 900f, -2250f, 30f),
            new Spot("La Mesa rail yard", 720f, -1080f, 22f),
            new Spot("Murrieta oil fields", 1430f, -1880f, 70f),
            new Spot("Banning lots", -160f, -2480f, 6f),
            new Spot("Sandy Shores airfield", 1720f, 3290f, 41f),
            new Spot("Senora junkyard", 2350f, 3130f, 48f),
            new Spot("Paleto sawmill", -560f, 5300f, 70f),
        };

        public static Vector3 FinalLocation(FactionType t)
        {
            switch (t)
            {
                case FactionType.Militia: return new Vector3(-540f, -210f, 37.6f);   // Rockford Hills city hall plaza
                case FactionType.Mafia: return new Vector3(-70f, -800f, 44.2f);       // Pillbox Hill financial district
                default: return new Vector3(195f, -935f, 30.6f);                      // Legion Square
            }
        }

        public static string FinalLocationName(FactionType t)
        {
            switch (t)
            {
                case FactionType.Militia: return "City Hall, Rockford Hills";
                case FactionType.Mafia: return "Maze Bank plaza, Pillbox Hill";
                default: return "Legion Square";
            }
        }
    }

    /// <summary>Base class for live missions: spawning through the entity budget, objective blips, kill counting, cleanup.</summary>
    internal abstract class MissionRuntime
    {
        protected readonly ModContext Mod;
        public readonly MissionOffer Offer;
        public readonly MissionDefinition Def;
        public readonly RivalFaction Rival;
        public readonly Territory Territory;
        protected readonly Rng Rng;

        public string Objective = "";
        public string Detail = "";
        public float Progress = -1;
        public int StartedAt;
        public int DeadlineAt;
        public bool Finished;
        public bool Success;
        public string FailReason;
        public int Kills;
        public readonly HashSet<string> Participants = new HashSet<string>();

        protected readonly List<Ped> Hostiles = new List<Ped>();
        protected readonly List<Entity> Owned = new List<Entity>();
        private readonly HashSet<int> _countedDead = new HashSet<int>();
        protected Blip ObjectiveBlip;
        private readonly List<Blip> _blips = new List<Blip>();

        protected MissionRuntime(ModContext mod, MissionOffer offer, MissionDefinition def)
        {
            Mod = mod;
            Offer = offer;
            Def = def;
            Rival = mod.State.World.GetRival(offer.RivalId);
            Territory = mod.State.World.GetTerritory(offer.TerritoryId);
            Rng = new Rng(offer.Seed == 0 ? Environment.TickCount : offer.Seed);
        }

        public abstract void Start();
        public abstract void Tick(int now);

        protected Ped Player => GtaUtil.Player;
        protected int Difficulty => Math.Max(1, Offer.Difficulty);

        protected int ScaledCount(int baseCount)
        {
            int n = baseCount + (Difficulty - 1);
            return MathEx.Clamp(n, 1, Mod.Config.Spawning.MaxMissionHostiles);
        }

        protected bool IsStateEnemy => Offer.RivalId == FactionIds.State || Def.Template == MissionTemplate.FinalCampaign;

        protected string EnemyModel()
        {
            if (IsStateEnemy) return Rng.Pick(WorldFactory.StateForcePeds);
            if (Rival != null && Rival.PedModels.Count > 0) return Rng.Pick(Rival.PedModels);
            return "g_m_y_mexgoon_01";
        }

        protected string EnemyWeapon()
        {
            if (IsStateEnemy) return Difficulty >= 3 ? "WEAPON_CARBINERIFLE" : "WEAPON_SMG";
            return Rival?.Weapon ?? "WEAPON_PISTOL";
        }

        protected RelationshipGroup EnemyGroup => Mod.Relations.RivalGroup(IsStateEnemy ? FactionIds.State : Offer.RivalId ?? "unknown");

        protected Ped SpawnHostile(Vector3 near, bool engage)
        {
            if (Hostiles.Count(h => GtaUtil.Alive(h)) >= Mod.Config.Spawning.MaxMissionHostiles) return null;
            Vector3 pos = Nat.SafePedSpot(near + new Vector3(Rng.Range(-4, 4), Rng.Range(-4, 4), 0));
            var ped = Mod.Entities.SpawnPed(EnemyModel(), pos, Rng.Range(0, 360), SpawnCategory.Mission);
            if (ped == null) return null;
            ped.RelationshipGroup = EnemyGroup;
            float acc = 18 + Difficulty * 6 + (IsStateEnemy ? 8 : 0);
            Nat.ConfigureHostile(ped, acc, IsStateEnemy ? 60 : Difficulty * 10, true);
            Nat.GiveWeapon(ped, EnemyWeapon(), 400, true);
            if (engage) Nat.CombatAround(ped, 120f);
            else
            {
                Nat.GuardSphere(ped, pos, 15f);
                ped.Task.GuardCurrentPosition();
            }
            var b = ped.AddBlip();
            b.Color = BlipColor.Red;
            b.Scale = 0.7f;
            b.IsShortRange = false;
            b.Name = Rival?.Name ?? "Hostile";
            Hostiles.Add(ped);
            return ped;
        }

        protected Vehicle SpawnVehicle(string model, Vector3 near, float heading, SpawnCategory category = SpawnCategory.Mission)
        {
            Vector3 pos = near;
            float h = heading;
            if (Nat.StreetNodeWithHeading(near, out Vector3 node, out float nodeHeading) && node.DistanceTo(near) < 120f) { pos = node; h = nodeHeading; }
            var v = Mod.Entities.SpawnVehicle(model, pos, h, category);
            if (v == null) return null;
            v.PlaceOnGround();
            Owned.Add(v);
            return v;
        }

        protected void Engage(IEnumerable<Ped> peds)
        {
            foreach (var p in peds) if (GtaUtil.Alive(p)) Nat.CombatAround(p, 150f);
        }

        /// <summary>Counts newly dead hostiles; returns living count.</summary>
        protected int UpdateHostiles()
        {
            int alive = 0;
            foreach (var h in Hostiles)
            {
                if (!GtaUtil.Valid(h)) continue;
                if (h.IsDead)
                {
                    if (_countedDead.Add(h.Handle))
                    {
                        Kills++;
                        try { h.AttachedBlip?.Delete(); } catch { /* ignore */ }
                        var killer = h.Killer as Ped;
                        var rt = killer != null ? Mod.Members.FindByPed(killer) : null;
                        if (rt != null) { rt.Member.Kills++; Participants.Add(rt.Member.Id); }
                    }
                }
                else alive++;
            }
            return alive;
        }

        protected int AliveHostiles => Hostiles.Count(h => GtaUtil.Alive(h));

        protected void SetObjectiveBlip(Vector3 pos, BlipSprite sprite, string name, bool route, BlipColor color = BlipColor.Yellow)
        {
            ClearObjectiveBlip();
            ObjectiveBlip = GTA.World.CreateBlip(pos);
            ObjectiveBlip.Sprite = sprite;
            ObjectiveBlip.Color = color;
            ObjectiveBlip.Name = name;
            ObjectiveBlip.ShowRoute = route;
        }

        protected void ClearObjectiveBlip()
        {
            try { ObjectiveBlip?.Delete(); } catch { /* ignore */ }
            ObjectiveBlip = null;
        }

        protected Blip AddBlip(Vector3 pos, BlipSprite sprite, BlipColor color, string name)
        {
            var b = GTA.World.CreateBlip(pos);
            b.Sprite = sprite;
            b.Color = color;
            b.Name = name;
            _blips.Add(b);
            return b;
        }

        protected Blip AddEntityBlip(Entity e, BlipSprite sprite, BlipColor color, string name, bool route = false)
        {
            var b = e.AddBlip();
            b.Sprite = sprite;
            b.Color = color;
            b.Name = name;
            b.ShowRoute = route;
            _blips.Add(b);
            return b;
        }

        protected void RemoveBlip(Blip b)
        {
            if (b == null) return;
            try { b.Delete(); } catch { /* ignore */ }
            _blips.Remove(b);
        }

        protected void Succeed()
        {
            if (Finished) return;
            Finished = true;
            Success = true;
        }

        protected void Fail(string reason)
        {
            if (Finished) return;
            Finished = true;
            Success = false;
            FailReason = reason;
        }

        /// <summary>Picks the mission's target point: the target territory (verified center) or near the player.</summary>
        protected Vector3 TargetPoint(float fallbackDistance = 350f)
        {
            if (Territory != null)
            {
                Vector3 c = Territory.Center.ToVector3();
                Vector3 offset = new Vector3(Rng.Range(-60, 60), Rng.Range(-60, 60), 0);
                return Nat.SafePedSpot(c + offset);
            }
            Vector3 p = Player.Position;
            double a = Rng.Range(0, (float)(Math.PI * 2));
            return Nat.SafePedSpot(new Vector3(p.X + (float)Math.Cos(a) * fallbackDistance, p.Y + (float)Math.Sin(a) * fallbackDistance, p.Z));
        }

        protected void SetDeadlineSeconds(int seconds)
        {
            DeadlineAt = seconds > 0 ? Game.GameTime + seconds * 1000 : 0;
        }

        public int SecondsLeft => DeadlineAt > 0 ? Math.Max(0, (DeadlineAt - Game.GameTime) / 1000) : -1;

        protected bool DeadlinePassed => DeadlineAt > 0 && Game.GameTime > DeadlineAt;

        public virtual void Cleanup()
        {
            ClearObjectiveBlip();
            foreach (var b in _blips.ToList()) RemoveBlip(b);
            foreach (var h in Hostiles)
            {
                if (!GtaUtil.Valid(h)) continue;
                try { h.AttachedBlip?.Delete(); } catch { /* ignore */ }
                bool delete = !h.IsOnScreen || h.IsDead && h.Position.DistanceTo(Player.Position) > 50;
                if (!h.IsDead && h.IsOnScreen) h.Task.FleeFrom(Player);
                Mod.Entities.Release(h, delete);
            }
            Hostiles.Clear();
            foreach (var e in Owned)
            {
                if (!GtaUtil.Valid(e)) continue;
                bool playerInside = e is Vehicle v && Player.IsInVehicle(v);
                Mod.Entities.Release(e, !playerInside && !e.IsOnScreen);
            }
            Owned.Clear();
        }

        public virtual void OnRecruited() { }

        public virtual string ContextForDialogue() => Def.Title + " - " + Objective;
    }
}
