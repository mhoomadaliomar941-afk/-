using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.World
{
    /// <summary>
    /// Makes the living world physical. What the simulation decides happens in the street when you are there:
    ///  * rival-vs-rival battles near you are fought by real spawned fighters; who dies decides the battle, and
    ///    joining in counts as taking a side;
    ///  * every active battle in San Andreas is marked on the map so you can go and watch or intervene;
    ///  * rival crews hang out in their own turf, and recruit passers-by when their faction is recruiting;
    ///  * rival leaders can be found with bodyguards at their home turf (killing one triggers a real succession).
    /// Everything is capped (Spawning settings + global entity cap), paused during story missions and missions of
    /// this mod, and cleaned up when you leave.
    /// </summary>
    internal sealed class LivingWorldController
    {
        private static readonly string[] LocalModels = { "a_m_y_soucent_01", "a_m_m_soucent_02", "a_m_y_eastsa_02", "a_m_y_downtown_01", "a_m_y_stlat_01" };
        private static readonly string[] CrewScenarios = { "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_LEANING" };

        private sealed class Fighter
        {
            public Ped Ped;
            public bool Attacker;
            public bool Counted;
        }

        private sealed class LiveBattle
        {
            public string BattleId;
            public Vector3 Point;
            public readonly List<Fighter> Fighters = new List<Fighter>();
            public int WavesAttacker, WavesDefender;
            public Blip Area;
        }

        private sealed class Scene
        {
            public Ped Recruiter, Recruit;
            public string RivalId;
            public int Stage;
            public int Until;
        }

        private readonly ModContext _mod;
        private LiveBattle _live;
        private readonly Dictionary<string, Blip> _battleMarkers = new Dictionary<string, Blip>();
        private readonly List<Ped> _crew = new List<Ped>();
        private string _crewTerritory;
        private int _crewCooldownUntil;
        private Ped _leader;
        private readonly List<Ped> _guards = new List<Ped>();
        private string _leaderRivalId, _leaderName;
        private readonly Dictionary<string, int> _leaderVisits = new Dictionary<string, int>();
        private Scene _scene;
        private readonly HashSet<string> _staged = new HashSet<string>();
        private int _lastSceneToastAt;
        private int _nextSyncAt, _nextFastAt, _nextSlowAt;

        /// <summary>Set by the integration layer when another gang/war mod is running.</summary>
        public bool Coexistence { get; set; }

        public LivingWorldController(ModContext mod) { _mod = mod; }

        public bool HasLiveBattle => _live != null;

        public void Update()
        {
            int now = Game.GameTime;
            var cfg = _mod.Config.World;
            if (now >= _nextSyncAt)
            {
                _nextSyncAt = now + 3000;
                _mod.Relations.SyncWorld(_mod.State.World, _mod.HasFaction);
                UpdateMarkers();
            }
            bool blocked = Game.IsMissionActive || _mod.Missions.IsActive || !cfg.LivingWorld;
            if (blocked)
            {
                if (_live != null || _crew.Count > 0 || _leader != null || _scene != null) DespawnAll(false);
                return;
            }
            if (now >= _nextFastAt)
            {
                _nextFastAt = now + 1000;
                UpdateLiveBattle(now);
                UpdateLeader(now);
                UpdateScene(now);
            }
            if (now >= _nextSlowAt)
            {
                _nextSlowAt = now + 2500;
                MaybeStartLiveBattle();
                UpdateCrew(now);
                MaybeSpawnLeader(now);
                MaybeStageRecruitment(now);
            }
        }

        // ------------------------------------------------------------------ map markers

        private void UpdateMarkers()
        {
            var lw = _mod.State.World.Living;
            var active = new HashSet<string>();
            if (_mod.Config.World.LivingWorld && _mod.Config.World.ShowWorldEventsOnMap && lw != null)
            {
                foreach (var b in lw.ActiveBattles)
                {
                    var t = _mod.State.World.GetTerritory(b.TerritoryId);
                    if (t == null) continue;
                    active.Add(b.Id);
                    if (_battleMarkers.ContainsKey(b.Id)) continue;
                    try
                    {
                        var blip = GTA.World.CreateBlip(new Vector3(t.Center.X, t.Center.Y, t.Center.Z));
                        blip.Sprite = BlipSprite.TurfWar;
                        blip.Color = BlipColor.Red;
                        blip.Scale = 0.85f;
                        blip.IsShortRange = false;
                        blip.Name = Core.Localization.Loc.IsArabic ? "Street war" : "Street war: " + _mod.State.World.FactionName(b.AttackerId, _mod.Faction) + " vs " + _mod.State.World.FactionName(b.DefenderId, _mod.Faction);
                        blip.IsFlashing = true;
                        blip.FlashTimeLeft = 8000;
                        _battleMarkers[b.Id] = blip;
                    }
                    catch (Exception ex) { Log.Debug("Battle marker failed: " + ex.Message); }
                }
            }
            foreach (var id in _battleMarkers.Keys.Where(k => !active.Contains(k)).ToList())
            {
                try { if (_battleMarkers[id].Exists()) _battleMarkers[id].Delete(); } catch (Exception) { /* already gone */ }
                _battleMarkers.Remove(id);
            }
        }

        // ------------------------------------------------------------------ street battles

        private void MaybeStartLiveBattle()
        {
            if (_live != null || !_mod.Config.World.PhysicalBattles) return;
            var world = _mod.State.World;
            var lw = world.Living;
            if (lw == null) return;
            var player = GtaUtil.Player;
            var pos = new Vec3(player.Position.X, player.Position.Y, player.Position.Z);
            var current = _mod.Territories.Current;
            WorldBattle pick = null;
            float best = float.MaxValue;
            foreach (var b in lw.ActiveBattles)
            {
                var t = world.GetTerritory(b.TerritoryId);
                if (t == null) continue;
                float d = t.Center.DistanceTo2D(pos);
                bool inside = current != null && current.Id == t.Id;
                if (!inside && d > 420f) continue;
                if (inside) d = 0;
                if (d < best) { best = d; pick = b; }
            }
            if (pick == null) return;

            int cap = _mod.Config.Spawning.MaxBattlePeds;
            if (Coexistence) cap /= 2;
            if (cap < 2) return;
            int attackers = MathEx.Clamp((int)Math.Round(pick.AttackerForce / 3f), 2, Math.Max(1, cap / 2));
            int defenders = MathEx.Clamp((int)Math.Round(pick.DefenderForce / 3f), 1, Math.Max(1, cap - attackers));
            while (attackers + defenders > 2 && !_mod.Entities.CanSpawn(SpawnCategory.World, attackers + defenders))
            {
                if (attackers >= defenders) attackers--; else defenders--;
            }
            if (!_mod.Entities.CanSpawn(SpawnCategory.World, attackers + defenders)) return;

            var terr = world.GetTerritory(pick.TerritoryId);
            Vector3 point = best < 250f
                ? Nat.SafePedSpot(GtaUtil.BehindCamera(70f, 0.9f, _mod.Rng))
                : Nat.SafePedSpot(new Vector3(terr.Center.X + _mod.Rng.Range(-60, 60), terr.Center.Y + _mod.Rng.Range(-60, 60), terr.Center.Z));
            var live = new LiveBattle { BattleId = pick.Id, Point = point };
            _live = live;
            var att = world.GetRival(pick.AttackerId);
            var def = world.GetRival(pick.DefenderId);
            var ga = _mod.Relations.WorldGroup(pick.AttackerId);
            var gd = def != null ? _mod.Relations.WorldGroup(def.Id) : _mod.Relations.Locals;
            _mod.Relations.MakeEnemies(ga, gd);

            Vector3 approach = point + Direction(_mod.Rng) * 28f;
            SpawnFighters(live, att, true, attackers, approach, ga);
            SpawnFighters(live, def, false, defenders, point, gd);
            if (live.Fighters.Count == 0) { _live = null; return; }
            try
            {
                live.Area = GTA.World.CreateBlip(point, 60f);
                live.Area.Color = BlipColor.Red;
                live.Area.Alpha = 80;
            }
            catch (Exception) { live.Area = null; }
            _mod.Ui.Notify.Push(LivingWorldSystem.Fmt("Street war in {0}: {1} vs {2}!", terr.Name, att?.Name ?? "?", def?.Name ?? world.FactionName(pick.DefenderId, _mod.Faction)), EventSeverity.Warning, 6000);
        }

        private static Vector3 Direction(Rng rng)
        {
            float a = rng.Range(0f, (float)(Math.PI * 2));
            return new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 0);
        }

        private void SpawnFighters(LiveBattle live, RivalFaction faction, bool attacker, int count, Vector3 around, RelationshipGroup group)
        {
            for (int i = 0; i < count; i++)
            {
                string model = faction != null && faction.PedModels.Count > 0 ? _mod.Rng.Pick(faction.PedModels) : _mod.Rng.Pick(LocalModels);
                var pos = Nat.SafePedSpot(around + new Vector3(_mod.Rng.Range(-5, 5), _mod.Rng.Range(-5, 5), 0));
                var ped = _mod.Entities.SpawnPed(model, pos, _mod.Rng.Range(0, 360), SpawnCategory.World);
                if (ped == null) break;
                ped.RelationshipGroup = group;
                float strength = faction?.Strength ?? 30;
                Nat.ConfigureHostile(ped, 18 + strength * 0.25f, strength > 60 ? 25 : 0, attacker);
                Nat.GiveWeapon(ped, faction?.Weapon ?? "WEAPON_PISTOL", 250, true);
                if (!attacker) Nat.GuardSphere(ped, around, 18f);
                Nat.CombatAround(ped, 90f);
                try
                {
                    var b = ped.AddBlip();
                    b.Color = faction != null && Rgb.TryParse(faction.Color, out var c) ? GtaUtil.NearestBlipColor(c) : BlipColor.Grey;
                    b.Scale = 0.5f;
                    b.IsShortRange = true;
                    b.Name = faction?.Name ?? "Locals";
                }
                catch (Exception) { /* blip limit */ }
                live.Fighters.Add(new Fighter { Ped = ped, Attacker = attacker });
            }
        }

        private void UpdateLiveBattle(int now)
        {
            if (_live == null) return;
            var world = _mod.State.World;
            var b = world.Living?.Battles.FirstOrDefault(x => x.Id == _live.BattleId);
            int deadA = 0, deadD = 0;
            bool playerA = false, playerD = false;
            foreach (var f in _live.Fighters)
            {
                if (f.Counted) continue;
                if (!GtaUtil.Valid(f.Ped)) { f.Counted = true; continue; }
                if (!f.Ped.IsDead) continue;
                f.Counted = true;
                var killer = f.Ped.Killer as Ped;
                bool byPlayer = killer != null && (killer.IsPlayer || _mod.Members.FindByPed(killer) != null);
                if (f.Attacker) { deadA++; playerA |= byPlayer; } else { deadD++; playerD |= byPlayer; }
            }
            if (b != null && b.IsActive)
            {
                if (deadA > 0) LivingWorldSystem.ReportLiveCasualties(_mod.Sim, b.Id, true, deadA, playerA);
                if (deadD > 0 && b.IsActive) LivingWorldSystem.ReportLiveCasualties(_mod.Sim, b.Id, false, deadD, playerD);
                if (deadA + deadD > 0) _mod.FlushNotifications();
            }

            int aliveA = _live.Fighters.Count(f => f.Attacker && GtaUtil.Alive(f.Ped));
            int aliveD = _live.Fighters.Count(f => !f.Attacker && GtaUtil.Alive(f.Ped));
            if (b != null && b.IsActive)
            {
                if (aliveA <= 1 && _live.WavesAttacker < 2 && b.AttackerForce > b.AttackerStart * 0.3f)
                {
                    _live.WavesAttacker++;
                    SpawnFighters(_live, world.GetRival(b.AttackerId), true, 2 + _mod.Rng.Next(0, 2), _live.Point + Direction(_mod.Rng) * 45f, _mod.Relations.WorldGroup(b.AttackerId));
                }
                if (aliveD <= 1 && _live.WavesDefender < 2 && b.DefenderForce > b.DefenderStart * 0.3f)
                {
                    _live.WavesDefender++;
                    var def = world.GetRival(b.DefenderId);
                    SpawnFighters(_live, def, false, 2 + _mod.Rng.Next(0, 2), _live.Point + Direction(_mod.Rng) * 35f, def != null ? _mod.Relations.WorldGroup(def.Id) : _mod.Relations.Locals);
                }
            }
            float dist = GtaUtil.Player.Position.DistanceTo(_live.Point);
            bool over = b == null || !b.IsActive || dist > 450f || _live.Fighters.All(f => !GtaUtil.Alive(f.Ped));
            if (over) EndLiveBattle();
        }

        private void EndLiveBattle()
        {
            if (_live == null) return;
            foreach (var f in _live.Fighters)
            {
                if (!GtaUtil.Valid(f.Ped)) continue;
                bool far = f.Ped.Position.DistanceTo(GtaUtil.Player.Position) > 120f && !f.Ped.IsOnScreen;
                if (!f.Ped.IsDead) Nat.WanderInArea(f.Ped, _live.Point, 80f);
                _mod.Entities.Release(f.Ped, far);
            }
            try { if (_live.Area != null && _live.Area.Exists()) _live.Area.Delete(); } catch (Exception) { /* gone */ }
            _live = null;
        }

        // ------------------------------------------------------------------ crews in their own turf

        private RivalFaction CurrentOwner(out Territory t)
        {
            t = _mod.Territories.Current;
            if (t == null || t.OwnerId == null) return null;
            var r = _mod.State.World.GetRival(t.OwnerId);
            return r != null && r.Active ? r : null;
        }

        private void UpdateCrew(int now)
        {
            _crew.RemoveAll(p =>
            {
                if (!GtaUtil.Valid(p)) return true;
                bool far = p.Position.DistanceTo(GtaUtil.Player.Position) > 170f && !p.IsOnScreen;
                if (far || p.IsDead && p.Position.DistanceTo(GtaUtil.Player.Position) > 60f) { _mod.Entities.Release(p, far && !p.IsDead); return true; }
                return false;
            });
            var r = CurrentOwner(out var t);
            bool want = _mod.Config.World.AmbientFactionCrews && !Coexistence && _live == null && r != null && r.PedModels.Count > 0
                        && !(_mod.HasFaction && r.Relation == RelationStatus.War);   // at war the hostile patrols take over
            if (!want || (t != null && _crewTerritory != null && t.Id != _crewTerritory))
            {
                if (_crew.Count > 0 && (!want || t?.Id != _crewTerritory)) DespawnCrew();
                if (!want) return;
            }
            int target = Math.Min(_mod.Config.Spawning.MaxAmbientCrewPeds, 2 + r.Manpower / 40);
            if (_crew.Count >= target || now < _crewCooldownUntil || GtaUtil.Player.IsInVehicle() && GtaUtil.Player.Velocity.Length() > 12f) return;
            if (!_mod.Entities.CanSpawn(SpawnCategory.World, target - _crew.Count)) return;
            var spot = Nat.SafePedSpot(GtaUtil.BehindCamera(55f, 0.8f, _mod.Rng));
            var group = _mod.Relations.WorldGroup(r.Id);
            for (int i = _crew.Count; i < target; i++)
            {
                var ped = _mod.Entities.SpawnPed(_mod.Rng.Pick(r.PedModels), Nat.SafePedSpot(spot + new Vector3(_mod.Rng.Range(-3, 3), _mod.Rng.Range(-3, 3), 0)), _mod.Rng.Range(0, 360), SpawnCategory.World);
                if (ped == null) break;
                ped.RelationshipGroup = group;
                Nat.GiveWeapon(ped, r.Weapon, 60, false);
                Nat.Scenario(ped, _mod.Rng.Pick(CrewScenarios));
                _crew.Add(ped);
            }
            _crewTerritory = t.Id;
            _crewCooldownUntil = now + 20000;
        }

        private void DespawnCrew()
        {
            foreach (var p in _crew) _mod.Entities.Release(p, GtaUtil.Valid(p) && !p.IsOnScreen);
            _crew.Clear();
            _crewTerritory = null;
            _crewCooldownUntil = Game.GameTime + 60000;
        }

        // ------------------------------------------------------------------ rival leaders

        private void MaybeSpawnLeader(int now)
        {
            if (_leader != null || !_mod.Config.World.RivalLeadersInWorld || _live != null) return;
            var r = CurrentOwner(out var t);
            if (r == null || r.Leader == null || !r.Leader.Alive || !r.HomeTerritories.Contains(t.Id) || r.PedModels.Count == 0) return;
            if (_leaderVisits.TryGetValue(r.Id, out int until) && now < until) return;
            _leaderVisits[r.Id] = now + 600000; // at most one appearance per faction every 10 minutes
            if (!_mod.Rng.Chance(0.4f) || !_mod.Entities.CanSpawn(SpawnCategory.World, 3)) return;

            var spot = Nat.SafePedSpot(GtaUtil.BehindCamera(60f, 0.7f, _mod.Rng));
            var ped = _mod.Entities.SpawnPed(r.Leader.PedModel ?? r.PedModels[0], spot, _mod.Rng.Range(0, 360), SpawnCategory.World);
            if (ped == null) return;
            var group = _mod.Relations.WorldGroup(r.Id);
            ped.RelationshipGroup = group;
            ped.Armor = 60;
            ped.MaxHealth = 300;
            ped.Health = 300;
            Nat.GiveWeapon(ped, r.Weapon, 200, false);
            Nat.Scenario(ped, "WORLD_HUMAN_SMOKING");
            try
            {
                var b = ped.AddBlip();
                b.Sprite = BlipSprite.King;
                b.Color = Rgb.TryParse(r.Color, out var c) ? GtaUtil.NearestBlipColor(c) : BlipColor.Red;
                b.Scale = 0.8f;
                b.IsShortRange = true;
                b.Name = r.Leader.Title + " " + r.Leader.Name;
            }
            catch (Exception) { /* blip limit */ }
            _leader = ped;
            _leaderRivalId = r.Id;
            _leaderName = r.Leader.Name;
            for (int i = 0; i < 2; i++)
            {
                var g = _mod.Entities.SpawnPed(_mod.Rng.Pick(r.PedModels), Nat.SafePedSpot(spot + Direction(_mod.Rng) * 3f), _mod.Rng.Range(0, 360), SpawnCategory.World);
                if (g == null) break;
                g.RelationshipGroup = group;
                Nat.ConfigureHostile(g, 35, 40, false);
                Nat.GiveWeapon(g, r.Weapon, 200, true);
                Nat.GuardSphere(g, spot, 10f);
                Nat.Scenario(g, "WORLD_HUMAN_GUARD_STAND");
                _guards.Add(g);
            }
            _mod.Ui.Notify.Push(LivingWorldSystem.Fmt("{0}, {1} of {2}, is nearby with bodyguards.", r.Leader.Name, r.Leader.Title, r.Name), EventSeverity.Info, 5000);
        }

        private void UpdateLeader(int now)
        {
            if (_leader == null) return;
            var r = _mod.State.World.GetRival(_leaderRivalId);
            if (!GtaUtil.Valid(_leader)) { ReleaseLeader(false); return; }
            if (_leader.IsDead)
            {
                var killer = _leader.Killer as Ped;
                bool byPlayer = killer != null && (killer.IsPlayer || _mod.Members.FindByPed(killer) != null);
                if (r != null && r.Leader != null && r.Leader.Alive && r.Leader.Name == _leaderName)
                {
                    LivingWorldSystem.KillLeader(_mod.Sim, r, _mod.Territories.Current?.Id, byPlayer);
                    _mod.FlushNotifications();
                }
                ReleaseLeader(false);
                return;
            }
            bool far = _leader.Position.DistanceTo(GtaUtil.Player.Position) > 200f && !_leader.IsOnScreen;
            bool replaced = r == null || r.Leader == null || r.Leader.Name != _leaderName;
            if (far || replaced) ReleaseLeader(true);
        }

        private void ReleaseLeader(bool delete)
        {
            if (_leader != null) _mod.Entities.Release(_leader, delete && GtaUtil.Valid(_leader) && !_leader.IsOnScreen);
            foreach (var g in _guards) _mod.Entities.Release(g, delete && GtaUtil.Valid(g) && !g.IsOnScreen);
            _guards.Clear();
            _leader = null;
            _leaderRivalId = null;
        }

        // ------------------------------------------------------------------ recruitment scenes

        private void MaybeStageRecruitment(int now)
        {
            if (_scene != null || _live != null || Coexistence || !_mod.Config.World.AmbientFactionCrews) return;
            var r = CurrentOwner(out var t);
            if (r == null || r.PedModels.Count == 0) return;
            var notice = _mod.State.World.Living?.Recruitments.LastOrDefault(n => n.RivalId == r.Id && n.TerritoryId == t.Id && !_staged.Contains(n.RivalId + ":" + n.Tick));
            if (notice == null) return;
            _staged.Add(notice.RivalId + ":" + notice.Tick);
            if (!_mod.Entities.CanSpawn(SpawnCategory.World, 2)) return;
            var player = GtaUtil.Player;
            Ped civilian = null;
            foreach (var p in GTA.World.GetNearbyPeds(player, 70f))
            {
                if (!GtaUtil.Alive(p) || p.IsPlayer || !p.IsHuman || p.IsInVehicle() || Nat.IsMissionEntity(p) || _mod.Entities.IsTracked(p) || _mod.Members.IsMemberPed(p)) continue;
                var pop = p.PopulationType;
                if (pop != EntityPopulationType.RandomAmbient && pop != EntityPopulationType.RandomScenario) continue;
                if (Nat.IsLawEnforcementType(Nat.PedType(p))) continue;
                float d = p.Position.DistanceTo(player.Position);
                if (d < 15f) continue;
                civilian = p;
                break;
            }
            if (civilian == null) return;
            var recruiter = _mod.Entities.SpawnPed(_mod.Rng.Pick(r.PedModels), Nat.SafePedSpot(civilian.Position + Direction(_mod.Rng) * 9f), 0, SpawnCategory.World);
            if (recruiter == null) return;
            recruiter.RelationshipGroup = _mod.Relations.WorldGroup(r.Id);
            Nat.GiveWeapon(recruiter, r.Weapon, 40, false);
            recruiter.Task.GoTo(civilian, new Vector3(0, 1.2f, 0), 20000);
            _scene = new Scene { Recruiter = recruiter, Recruit = civilian, RivalId = r.Id, Stage = 0, Until = now + 20000 };
        }

        private void UpdateScene(int now)
        {
            var s = _scene;
            if (s == null) return;
            if (!GtaUtil.Alive(s.Recruiter) || !GtaUtil.Alive(s.Recruit) || s.Recruiter.Position.DistanceTo(GtaUtil.Player.Position) > 180f)
            {
                EndScene();
                return;
            }
            var r = _mod.State.World.GetRival(s.RivalId);
            switch (s.Stage)
            {
                case 0:
                    if (s.Recruiter.Position.DistanceTo(s.Recruit.Position) < 2.8f)
                    {
                        s.Recruit.Task.ClearAll();
                        s.Recruit.Task.ChatTo(s.Recruiter);
                        s.Recruiter.Task.ChatTo(s.Recruit);
                        s.Stage = 1;
                        s.Until = now + 7000;
                    }
                    else if (now > s.Until) EndScene();
                    break;
                case 1:
                    if (now < s.Until) break;
                    // The passer-by joins: from now on they belong to the faction (and to this mod's cleanup).
                    Nat.SetMissionEntity(s.Recruit);
                    _mod.Entities.Track(s.Recruit, SpawnCategory.World);
                    s.Recruit.RelationshipGroup = _mod.Relations.WorldGroup(s.RivalId);
                    Nat.GiveWeapon(s.Recruit, "WEAPON_PISTOL", 40, false);
                    s.Recruiter.Task.WanderAround(s.Recruiter.Position, 80f);
                    Nat.FollowOffset(s.Recruit, s.Recruiter, new Vector3(1f, -1.2f, 0), 1.2f);
                    if (r != null && now - _lastSceneToastAt > 180000 && s.Recruit.Position.DistanceTo(GtaUtil.Player.Position) < 60f)
                    {
                        _lastSceneToastAt = now;
                        _mod.Ui.Notify.Push(LivingWorldSystem.Fmt("{0} just recruited someone off the street.", r.Name), EventSeverity.Info, 4000);
                    }
                    s.Stage = 2;
                    s.Until = now + 45000;
                    break;
                default:
                    if (now > s.Until) EndScene();
                    break;
            }
        }

        private void EndScene()
        {
            if (_scene == null) return;
            if (GtaUtil.Valid(_scene.Recruiter)) _mod.Entities.Release(_scene.Recruiter, !_scene.Recruiter.IsOnScreen);
            if (_scene.Stage >= 2 && GtaUtil.Valid(_scene.Recruit)) _mod.Entities.Release(_scene.Recruit, false);
            _scene = null;
        }

        // ------------------------------------------------------------------ cleanup

        public void DespawnAll(bool includeMarkers)
        {
            EndLiveBattle();
            DespawnCrew();
            ReleaseLeader(true);
            EndScene();
            if (includeMarkers)
            {
                foreach (var b in _battleMarkers.Values) { try { if (b.Exists()) b.Delete(); } catch (Exception) { /* gone */ } }
                _battleMarkers.Clear();
            }
        }

        /// <summary>Recognizes people of the living world (for kill attribution).</summary>
        public RivalFaction FactionOf(Ped ped)
        {
            if (ped == null) return null;
            int rel = Nat.RelationshipGroupHash(ped);
            foreach (var r in _mod.State.World.Rivals)
                if (r.Active && _mod.Relations.WorldGroup(r.Id).Hash == rel) return r;
            return null;
        }
    }
}
