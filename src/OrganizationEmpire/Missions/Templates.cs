using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.World;

namespace OrganizationEmpire.Missions
{
    /// <summary>Go to a location, eliminate the defenders, then hold the area.</summary>
    internal sealed class AssaultMission : MissionRuntime
    {
        private enum Phase { Travel, Fight, Hold }
        private Phase _phase;
        private Vector3 _target;
        private int _holdUntil;
        private int _awaySince;
        private bool _reinforced;

        public AssaultMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d) { }

        public override void Start()
        {
            _target = TargetPoint();
            SetObjectiveBlip(_target, BlipSprite.TurfWar, Def.Title, true, BlipColor.Red);
            Objective = "Go to " + (Territory?.Name ?? "the target area") + ".";
            if (Def.TimeLimitSeconds > 0) SetDeadlineSeconds(Def.TimeLimitSeconds);
        }

        public override void Tick(int now)
        {
            float d = Player.Position.DistanceTo(_target);
            switch (_phase)
            {
                case Phase.Travel:
                    Detail = (int)d + " m";
                    if (d < 140f)
                    {
                        int n = ScaledCount(Def.Hostiles);
                        for (int i = 0; i < n; i++) SpawnHostile(_target + new Vector3(Rng.Range(-22, 22), Rng.Range(-22, 22), 0), false);
                        _phase = Phase.Fight;
                        Objective = "Eliminate the " + (Rival?.Name ?? "enemy") + " defenders.";
                        ClearObjectiveBlip();
                    }
                    break;
                case Phase.Fight:
                    if (d < 80f) Engage(Hostiles);
                    int alive = UpdateHostiles();
                    Detail = alive + " hostile(s) left";
                    if (!_reinforced && alive <= Hostiles.Count / 2 && Difficulty >= 3)
                    {
                        _reinforced = true;
                        Vector3 from = Nat.SafePedSpot(GtaUtil.BehindCamera(70f, 0.8f, Rng));
                        for (int i = 0; i < 2 + Difficulty / 2; i++) SpawnHostile(from, true);
                        Mod.Ui.Notify.Push("Enemy reinforcements incoming!", EventSeverity.Warning);
                    }
                    if (alive == 0 && Hostiles.Count > 0)
                    {
                        _phase = Phase.Hold;
                        _holdUntil = now + 30000;
                        Objective = "Hold the area.";
                        SetObjectiveBlip(_target, BlipSprite.CaptureFlag, "Hold here", false, BlipColor.Green);
                    }
                    CheckAway(now, d);
                    break;
                case Phase.Hold:
                    UpdateHostiles();
                    if (d > 60f) { Detail = "Get back in the area!"; _holdUntil = Math.Max(_holdUntil, now + 5000); }
                    else
                    {
                        Progress = 1f - (_holdUntil - now) / 30000f;
                        Detail = Math.Max(0, (_holdUntil - now) / 1000) + " s";
                    }
                    if (now >= _holdUntil && AliveHostiles == 0) Succeed();
                    CheckAway(now, d);
                    break;
            }
            if (DeadlinePassed) Fail("Out of time");
        }

        private void CheckAway(int now, float d)
        {
            if (d > 400f)
            {
                if (_awaySince == 0) _awaySince = now;
                else if (now - _awaySince > 30000) Fail("You abandoned the operation");
                Detail = "Return to the area!";
            }
            else _awaySince = 0;
        }
    }

    /// <summary>Hold a position against waves of attackers.</summary>
    internal sealed class DefendMission : MissionRuntime
    {
        private Vector3 _point;
        private int _wave;
        private int _nextWaveAt;
        private bool _arrived;
        private int _awaySince;
        private int _overrunSince;
        private readonly bool _isFinal;
        private readonly int _holdSeconds;
        private int _holdUntil;

        public DefendMission(ModContext m, MissionOffer o, MissionDefinition d, Vector3 point, bool isFinal = false, int holdSeconds = 0) : base(m, o, d)
        {
            _point = point;
            _isFinal = isFinal;
            _holdSeconds = holdSeconds;
        }

        public override void Start()
        {
            if (_point == Vector3.Zero)
            {
                var hq = Mod.Faction.Headquarters;
                _point = Def.Target == TargetRule.Headquarters && hq != null ? hq.Position.ToVector3() : TargetPoint();
            }
            SetObjectiveBlip(_point, BlipSprite.Castle, Def.Title, true, BlipColor.Green);
            Objective = "Get to the position before the attack begins.";
            SetDeadlineSeconds(Offer.Urgent ? 360 : 600);
        }

        public override void Tick(int now)
        {
            float d = Player.Position.DistanceTo(_point);
            if (!_arrived)
            {
                Detail = (int)d + " m";
                if (d < 45f)
                {
                    _arrived = true;
                    DeadlineAt = 0;
                    _nextWaveAt = now + 12000;
                    Objective = "Prepare your defenses. Attack incoming.";
                    ClearObjectiveBlip();
                    SetObjectiveBlip(_point, BlipSprite.Castle, "Defend", false, BlipColor.Green);
                }
                else if (DeadlinePassed) Fail("You didn't arrive in time");
                return;
            }

            int alive = UpdateHostiles();
            int waves = Math.Max(1, Def.Waves + (Difficulty >= 4 ? 1 : 0));
            if (_wave < waves)
            {
                if (alive == 0 && now >= _nextWaveAt)
                {
                    _wave++;
                    SpawnWave();
                    Objective = "Hold the position - wave " + _wave + "/" + waves + ".";
                }
                else if (alive == 0)
                {
                    Detail = "Next wave in " + Math.Max(0, (_nextWaveAt - now) / 1000) + " s";
                }
                else
                {
                    Detail = alive + " attacker(s)";
                    _nextWaveAt = now + 8000;
                }
            }
            else if (alive == 0)
            {
                if (_holdSeconds > 0)
                {
                    if (_holdUntil == 0) { _holdUntil = now + _holdSeconds * 1000; Objective = "Hold the area until the enemy surrenders."; }
                    Progress = 1f - (_holdUntil - now) / (_holdSeconds * 1000f);
                    Detail = Math.Max(0, (_holdUntil - now) / 1000) + " s";
                    if (now >= _holdUntil) Succeed();
                }
                else Succeed();
            }
            else Detail = alive + " attacker(s)";

            // Failure: abandoning the position or being overrun.
            if (d > 160f)
            {
                if (_awaySince == 0) _awaySince = now;
                else if (now - _awaySince > 20000) Fail("You abandoned the position");
                Detail = "Return to the position!";
            }
            else _awaySince = 0;
            int atPoint = Hostiles.Count(h => GtaUtil.Alive(h) && h.Position.DistanceTo(_point) < 12f);
            bool defenders = d < 25f || Mod.Members.Deployed.Any(rt => rt.Ped.Position.DistanceTo(_point) < 20f);
            if (atPoint >= 3 && !defenders)
            {
                if (_overrunSince == 0) _overrunSince = now;
                else if (now - _overrunSince > 25000) Fail("The position was overrun");
            }
            else _overrunSince = 0;
        }

        private void SpawnWave()
        {
            int n = ScaledCount(Def.Hostiles + (_isFinal ? _wave : 0));
            Vector3 from = Nat.SafePedSpot(_point + (GtaUtil.BehindCamera(95f, 1.2f, Rng) - Player.Position));
            bool useVehicle = _isFinal && Rival == null && _wave >= 2 && Mod.Entities.CanSpawn(SpawnCategory.Mission, 2);
            if (useVehicle)
            {
                // Parked state vehicle as cover/visual for the assault force.
                SpawnVehicle(Rng.Pick(Core.Data.WorldFactory.StateForceVehicles), from + new Vector3(4, 0, 0), Rng.Range(0, 360));
            }
            for (int i = 0; i < n; i++)
            {
                var p = SpawnHostile(from, true);
                if (p != null) Nat.GoToAnyMeans(p, _point, 2f);
            }
            Engage(Hostiles);
            Mod.Ui.Notify.Push("Wave " + _wave + " incoming!", EventSeverity.Warning, 2500);
        }
    }

    /// <summary>Collect a vehicle and drive it to the drop-off. Optional ambush on the way.</summary>
    internal sealed class DeliveryMission : MissionRuntime
    {
        private Vehicle _cargo;
        private Vector3 _dropoff;
        private bool _pickedUp;
        private bool _ambushed;
        private float _routeLength;
        private Blip _cargoBlip;

        public DeliveryMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d) { }

        public override void Start()
        {
            var spots = MissionLocations.Pickups.OrderBy(s => s.Pos.DistanceTo(Player.Position)).ToList();
            var spot = spots[Math.Min(spots.Count - 1, 1 + Rng.Next(0, 3))];
            string model = Def.Id == "gang_arms" ? "burrito3" : Def.Id == "mil_cache" ? "rumpo" : "speedo";
            _cargo = SpawnVehicle(model, spot.Pos, 0f);
            if (_cargo == null) { Fail("Could not prepare the vehicle"); return; }
            _cargo.IsPersistent = true;
            _cargoBlip = AddEntityBlip(_cargo, BlipSprite.Package, BlipColor.Yellow, "Cargo", true);
            var hq = Mod.Faction.Headquarters;
            _dropoff = hq != null ? hq.Position.ToVector3() : Nat.SafePedSpot(Player.Position);
            Objective = "Collect the vehicle at " + spot.Name + ".";
            if (Def.TimeLimitSeconds > 0) SetDeadlineSeconds(Def.TimeLimitSeconds);
            else SetDeadlineSeconds(900);
        }

        public override void Tick(int now)
        {
            if (Finished) return;
            if (!GtaUtil.Valid(_cargo) || _cargo.IsDead || _cargo.EngineHealth < 0) { Fail("The cargo was destroyed"); return; }
            UpdateHostiles();
            bool inside = Player.IsInVehicle(_cargo);
            if (!_pickedUp)
            {
                Detail = (int)Player.Position.DistanceTo(_cargo.Position) + " m";
                if (inside)
                {
                    _pickedUp = true;
                    _routeLength = _cargo.Position.DistanceTo(_dropoff);
                    RemoveBlip(_cargoBlip);
                    SetObjectiveBlip(_dropoff, BlipSprite.Garage, "Drop-off", true, BlipColor.Green);
                    Objective = "Deliver the cargo to " + (Mod.Faction.Headquarters?.Name ?? "the drop-off") + ".";
                }
            }
            else
            {
                float left = _cargo.Position.DistanceTo(_dropoff);
                Progress = _routeLength > 0 ? 1f - left / _routeLength : 0f;
                Detail = (int)left + " m";
                if (!inside) Detail = "Get back in the vehicle!";
                if (!_ambushed && Def.Hostiles > 0 && Progress > 0.3f) SpawnAmbush();
                if (left < 10f && _cargo.Speed < 2f)
                {
                    if (inside) Player.Task.LeaveVehicle();
                    Succeed();
                }
            }
            if (DeadlinePassed) Fail("Out of time");
        }

        private void SpawnAmbush()
        {
            _ambushed = true;
            string vm = Rival != null && Rival.VehicleModels.Count > 0 ? Rng.Pick(Rival.VehicleModels) : "cavalcade";
            Vector3 from = GtaUtil.BehindCamera(90f, 0.3f, Rng);
            var car = SpawnVehicle(vm, from, Player.Heading);
            if (car == null) return;
            int n = Math.Min(car.PassengerCapacity + 1, ScaledCount(Def.Hostiles));
            for (int i = 0; i < n; i++)
            {
                var p = SpawnHostile(car.Position + new Vector3(0, 0, 1), false);
                if (p == null) break;
                p.SetIntoVehicle(car, i == 0 ? VehicleSeat.Driver : (VehicleSeat)(i - 1));
            }
            var driver = car.Driver;
            if (GtaUtil.Alive(driver)) driver.Task.VehicleChase(Player);
            foreach (var p in car.Occupants.Where(o => o != driver)) Nat.CombatAround(p, 150f);
            Mod.Ui.Notify.Push("You're being followed!", EventSeverity.Warning);
        }

        public override void Cleanup()
        {
            if (Success && GtaUtil.Valid(_cargo))
            {
                if (!Player.IsInVehicle(_cargo)) { Mod.Entities.Release(_cargo, !_cargo.IsOnScreen); Owned.Remove(_cargo); }
            }
            base.Cleanup();
        }
    }

    /// <summary>Visit several locations and spend a few seconds at each (collections, aid deliveries).</summary>
    internal sealed class CollectMission : MissionRuntime
    {
        private readonly List<MissionLocations.Spot> _stops = new List<MissionLocations.Spot>();
        private readonly List<Blip> _stopBlips = new List<Blip>();
        private int _current = -1;
        private int _standingSince;
        private readonly bool _friendly;
        private readonly HashSet<int> _done = new HashSet<int>();

        public CollectMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d)
        {
            _friendly = d.Id == "mil_hearts" || d.Id == "gang_block_party";
        }

        public override void Start()
        {
            var ordered = MissionLocations.Businesses.OrderBy(s => s.Pos.DistanceTo(Player.Position)).Take(Math.Max(Def.Stops + 2, 5)).ToList();
            Rng.Shuffle(ordered);
            _stops.AddRange(ordered.Take(Math.Max(1, Def.Stops)));
            for (int i = 0; i < _stops.Count; i++)
            {
                var b = AddBlip(_stops[i].Pos, _friendly ? BlipSprite.Health : BlipSprite.DollarSign, BlipColor.Green, (_friendly ? "Delivery: " : "Collect: ") + _stops[i].Name);
                b.NumberLabel = i + 1;
                _stopBlips.Add(b);
            }
            Objective = (_friendly ? "Deliver aid to " : "Collect from ") + _stops.Count + " locations.";
            SetDeadlineSeconds(Def.TimeLimitSeconds > 0 ? Def.TimeLimitSeconds : 1200);
        }

        public override void Tick(int now)
        {
            UpdateHostiles();
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _stops.Count; i++)
            {
                if (_done.Contains(i)) continue;
                float d = Player.Position.DistanceTo(_stops[i].Pos);
                if (d < best) { best = d; nearest = i; }
            }
            if (nearest < 0) { if (AliveHostiles == 0) Succeed(); else Detail = "Deal with the trouble first!"; return; }
            Detail = _done.Count + "/" + _stops.Count + " done - nearest " + (int)best + " m";
            if (best < 5f && !Player.IsInVehicle())
            {
                if (_current != nearest) { _current = nearest; _standingSince = now; }
                Progress = (now - _standingSince) / 5000f;
                Detail = (_friendly ? "Handing out supplies" : "Collecting") + "...";
                if (now - _standingSince > 5000)
                {
                    _done.Add(nearest);
                    RemoveBlip(_stopBlips[nearest]);
                    Progress = -1;
                    Nat.Sound("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    if (!_friendly && Rng.Chance(0.25f + Difficulty * 0.05f))
                    {
                        Vector3 from = Nat.SafePedSpot(_stops[nearest].Pos + new Vector3(10, 10, 0));
                        for (int i = 0; i < 1 + Difficulty / 2; i++) SpawnHostile(from, true);
                        Mod.Ui.Notify.Push("Someone decided to resist!", EventSeverity.Warning);
                    }
                }
            }
            else
            {
                _current = -1;
                Progress = -1;
            }
            if (DeadlinePassed) Fail("Out of time");
        }
    }

    /// <summary>Protect an allied vehicle until it reaches its destination.</summary>
    internal sealed class EscortMission : MissionRuntime
    {
        private Vehicle _vip;
        private Ped _driver;
        private Vector3 _destination;
        private float _total;
        private int _ambushes;
        private int _awaySince;
        private int _nextDriveAt;

        public EscortMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d) { }

        public override void Start()
        {
            string model = Mod.Faction.Type == FactionType.Mafia ? "schafter2" : Mod.Faction.Type == FactionType.Militia ? "mesa3" : "baller";
            Vector3 start = Player.Position + Player.ForwardVector * 15f;
            _vip = SpawnVehicle(model, start, Player.Heading);
            if (_vip == null) { Fail("Could not prepare the convoy"); return; }
            _vip.MaxHealth = 2000;
            _vip.Health = 2000;
            _vip.EngineHealth = 1500;
            var uni = Core.Data.UniformCatalog.Get(Mod.Faction.Identity.UniformPreset) ?? Core.Data.UniformCatalog.DefaultFor(Mod.Faction.Type);
            _driver = Mod.Entities.SpawnPed(Rng.Pick(uni.Models), _vip.Position + new Vector3(0, 0, 2), 0, SpawnCategory.Mission);
            if (_driver == null) { Fail("No driver available"); return; }
            Owned.Add(_driver);
            _driver.RelationshipGroup = Mod.Relations.Allies;
            _driver.SetIntoVehicle(_vip, VehicleSeat.Driver);
            _driver.BlockPermanentEvents = true;
            _driver.MaxHealth = 400; _driver.Health = 400;
            AddEntityBlip(_vip, BlipSprite.VIP, BlipColor.Blue, "Convoy");
            var hq = Mod.Faction.Headquarters;
            Vector3 dest = hq != null && hq.Position.ToVector3().DistanceTo(start) > 800f ? hq.Position.ToVector3() : Nat.SafePedSpot(GtaUtil.BehindCamera(1600f, 1.5f, Rng));
            if (Nat.StreetNodeWithHeading(dest, out Vector3 node, out _)) dest = node;
            _destination = dest;
            _total = Math.Max(1f, _vip.Position.DistanceTo(_destination));
            SetObjectiveBlip(_destination, BlipSprite.RaceFinish, "Convoy destination", false, BlipColor.Blue);
            Objective = "Escort the convoy to its destination. Get in your own vehicle and stay close.";
            SetDeadlineSeconds(900);
        }

        public override void Tick(int now)
        {
            if (Finished) return;
            if (!GtaUtil.Valid(_vip) || _vip.IsDead) { Fail("The convoy was destroyed"); return; }
            if (!GtaUtil.Alive(_driver)) { Fail("The driver was killed"); return; }
            UpdateHostiles();
            float left = _vip.Position.DistanceTo(_destination);
            Progress = 1f - left / _total;
            Detail = (int)left + " m to go";
            float d = Player.Position.DistanceTo(_vip.Position);
            if (now >= _nextDriveAt)
            {
                _nextDriveAt = now + 6000;
                if (d < 120f) Nat.DriveTo(_driver, _vip, _destination, 15f, 10f);
                else _driver.Task.StandStill(3000); // wait for the escort
            }
            if (_ambushes < 1 + Difficulty / 2 && Progress > 0.2f + _ambushes * 0.3f) SpawnAmbush();
            if (left < 18f) Succeed();
            if (d > 260f)
            {
                if (_awaySince == 0) _awaySince = now;
                else if (now - _awaySince > 25000) Fail("You left the convoy unprotected");
            }
            else _awaySince = 0;
            if (DeadlinePassed) Fail("Out of time");
        }

        private void SpawnAmbush()
        {
            _ambushes++;
            Vector3 ahead = _vip.Position + _vip.ForwardVector * 120f;
            string vm = Rival != null && Rival.VehicleModels.Count > 0 ? Rng.Pick(Rival.VehicleModels) : "cavalcade";
            var car = SpawnVehicle(vm, ahead, _vip.Heading + 180f);
            int n = ScaledCount(Def.Hostiles);
            for (int i = 0; i < n; i++)
            {
                var p = SpawnHostile(ahead + new Vector3(Rng.Range(-6, 6), Rng.Range(-6, 6), 0), true);
                if (p != null && car != null && i < 2) p.SetIntoVehicle(car, i == 0 ? VehicleSeat.Driver : VehicleSeat.Passenger);
            }
            if (car != null && GtaUtil.Alive(car.Driver)) car.Driver.Task.VehicleChase(_driver);
            Mod.Ui.Notify.Push("Ambush ahead of the convoy!", EventSeverity.Warning);
        }
    }

    /// <summary>Recruit a number of people before time runs out.</summary>
    internal sealed class RecruitMission : MissionRuntime
    {
        private int _recruited;
        public RecruitMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d) { }

        public override void Start()
        {
            Objective = "Recruit " + Def.Stops + " new members (look at someone and press " + Mod.Config.Hotkeys.Interact + ").";
            SetDeadlineSeconds(Def.TimeLimitSeconds > 0 ? Def.TimeLimitSeconds : 600);
        }

        public override void OnRecruited()
        {
            _recruited++;
            Mod.Ui.Notify.Push("Recruits: " + _recruited + "/" + Def.Stops, EventSeverity.Good, 2500);
        }

        public override void Tick(int now)
        {
            Progress = _recruited / (float)Math.Max(1, Def.Stops);
            Detail = _recruited + "/" + Def.Stops + " recruited";
            if (_recruited >= Def.Stops) Succeed();
            else if (DeadlinePassed) Fail("Out of time");
        }
    }

    /// <summary>Visit observation points without drawing police attention.</summary>
    internal sealed class ReconMission : MissionRuntime
    {
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<Blip> _pointBlips = new List<Blip>();
        private int _index;
        private int _standingSince;

        public ReconMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d) { }

        public override void Start()
        {
            Vector3 origin = Player.Position;
            for (int i = 0; i < Math.Max(2, Def.Stops); i++)
            {
                double a = Rng.Range(0, (float)(Math.PI * 2));
                float dist = 300f + i * 250f;
                _points.Add(Nat.SafePedSpot(new Vector3(origin.X + (float)Math.Cos(a) * dist, origin.Y + (float)Math.Sin(a) * dist, origin.Z)));
            }
            ShowPoint();
            SetDeadlineSeconds(Def.TimeLimitSeconds > 0 ? Def.TimeLimitSeconds : 900);
        }

        private void ShowPoint()
        {
            SetObjectiveBlip(_points[_index], BlipSprite.Camera, "Observation point " + (_index + 1), true, BlipColor.Yellow);
            Objective = "Observe from point " + (_index + 1) + "/" + _points.Count + ". Avoid police attention.";
        }

        public override void Tick(int now)
        {
            if (Game.Player.WantedLevel > 0) { Fail("Compromised - the police are onto you"); return; }
            float d = Player.Position.DistanceTo(_points[_index]);
            Detail = (int)d + " m";
            if (d < 20f)
            {
                if (_standingSince == 0) _standingSince = now;
                Progress = (now - _standingSince) / 6000f;
                Detail = "Observing...";
                if (now - _standingSince > 6000)
                {
                    _standingSince = 0;
                    Progress = -1;
                    _index++;
                    Nat.Sound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
                    if (_index >= _points.Count) { Succeed(); return; }
                    ShowPoint();
                }
            }
            else { _standingSince = 0; Progress = -1; }
            if (DeadlinePassed) Fail("Out of time");
        }
    }

    /// <summary>Meet a rival boss (or an official) and choose how the conversation ends.</summary>
    internal sealed class MeetingMission : MissionRuntime
    {
        private Ped _boss;
        private readonly List<Ped> _guards = new List<Ped>();
        private Vector3 _spot;
        private bool _choiceShown;
        private bool _hostile;
        private readonly bool _official;

        public MeetingMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d)
        {
            _official = d.Id == "maf_councilman";
        }

        public override void Start()
        {
            _spot = TargetPoint(400f);
            string bossModel = _official ? "a_m_m_business_01" : Rival != null && Rival.PedModels.Count > 0 ? Rival.PedModels.Last() : "g_m_m_armboss_01";
            _boss = Mod.Entities.SpawnPed(bossModel, _spot, Rng.Range(0, 360), SpawnCategory.Mission);
            if (_boss == null) { Fail("The meeting fell through"); return; }
            Owned.Add(_boss);
            _boss.RelationshipGroup = Mod.Relations.Allies;
            _boss.BlockPermanentEvents = true;
            Nat.Scenario(_boss, "WORLD_HUMAN_STAND_IMPATIENT");
            int guards = _official ? 2 : Math.Min(4, Def.Hostiles + Difficulty / 2);
            for (int i = 0; i < guards; i++)
            {
                string gm = _official ? "s_m_m_highsec_01" : EnemyModel();
                var g = Mod.Entities.SpawnPed(gm, Nat.SafePedSpot(_spot + new Vector3(Rng.Range(-4, 4), Rng.Range(-4, 4), 0)), 0, SpawnCategory.Mission);
                if (g == null) continue;
                g.RelationshipGroup = Mod.Relations.Allies;
                Nat.GiveWeapon(g, _official ? "WEAPON_PISTOL" : EnemyWeapon(), 200, false);
                g.Task.GuardCurrentPosition();
                _guards.Add(g);
                Owned.Add(g);
            }
            SetObjectiveBlip(_spot, BlipSprite.ChatBubble, "Meeting", true, BlipColor.Yellow);
            Objective = "Meet " + (_official ? "the councilman's representative" : "the " + (Rival?.Name ?? "rival") + " boss") + ".";
            SetDeadlineSeconds(900);
        }

        public override void Tick(int now)
        {
            if (Finished) return;
            if (_hostile)
            {
                int alive = UpdateHostiles();
                Detail = alive + " hostile(s)";
                if (!GtaUtil.Alive(_boss) && alive == 0) Succeed();
                return;
            }
            if (!GtaUtil.Alive(_boss)) { TurnHostile(); return; }
            float d = Player.Position.DistanceTo(_boss.Position);
            Detail = (int)d + " m";
            if (_guards.Any(g => GtaUtil.Valid(g) && (g.IsDead || g.HasBeenDamagedBy(Player)))) { TurnHostile(); return; }
            if (!_choiceShown && d < 5f && !Player.IsInVehicle())
            {
                _choiceShown = true;
                _boss.Task.TurnTo(Player);
                ShowChoices();
            }
            if (DeadlinePassed) Fail("You missed the meeting");
        }

        private void ShowChoices()
        {
            var options = new List<UI.PickOption>();
            if (_official)
            {
                options.Add(new UI.PickOption("Offer the family's help", "The councilman owes the family a favor. +Influence, weakens the state."));
                options.Add(new UI.PickOption("Make a cash contribution (" + TextUtil.Money(15000) + ")", "Money talks. Guaranteed, but costs cash."));
                options.Add(new UI.PickOption("Lean on him", "Intimidation works, but raises heat."));
            }
            else
            {
                float peace = Rival != null ? WarSystem.AcceptanceChance(Mod.Sim, Rival, Rival.Relation == RelationStatus.War ? RelationStatus.Ceasefire : RelationStatus.Peace) : 0.5f;
                options.Add(new UI.PickOption("Negotiate (" + TextUtil.Pct(peace * 100) + ")", "Propose an end to hostilities. Uses your reputation and power."));
                options.Add(new UI.PickOption("Demand tribute", "Show strength and demand a cut. Chance based on relative power."));
                options.Add(new UI.PickOption("Ambush them", "End this the old-fashioned way. Kill the boss and his guards."));
            }
            Mod.Ui.ShowPicker(_official ? "The Councilman's Favor" : "Sit-down with " + (Rival?.Name ?? "the rival"), options, Choose, () => Fail("You walked out"));
        }

        private void Choose(int idx)
        {
            var f = Mod.Faction;
            if (_official)
            {
                if (idx == 1)
                {
                    var r = EconomySystem.Spend(f, 15000, "contribution");
                    if (!r.Ok) { Mod.Ui.Notify.Push(r.Message, EventSeverity.Bad); _choiceShown = false; return; }
                }
                if (idx == 2) SuspicionSystem.OnPublicIncident(Mod.Sim, 8f);
                Nat.Speech(_boss, "GENERIC_THANKS");
                Succeed();
                return;
            }
            switch (idx)
            {
                case 0:
                    var proposal = Rival != null && Rival.Relation == RelationStatus.War ? RelationStatus.Ceasefire : RelationStatus.Peace;
                    var res = Rival != null ? WarSystem.Propose(Mod.Sim, Rival, proposal) : OpResult.Success();
                    Mod.FlushNotifications();
                    if (res.Ok) Succeed();
                    else { Mod.Ui.Notify.Push(res.Message + " They're reaching for their guns!", EventSeverity.Bad); TurnHostile(); }
                    break;
                case 1:
                    float power = ProgressionSystem.Power(Mod.Sim);
                    float chance = MathEx.Clamp(0.3f + (power - (Rival?.Strength ?? 50)) * 0.01f, 0.1f, 0.85f);
                    if (Rng.Chance(chance))
                    {
                        long tribute = 3000 + (long)((Rival?.Wealth ?? 40) * 120);
                        f.Resources.Cash += tribute;
                        if (Rival != null) Rival.Hostility = MathEx.Pct(Rival.Hostility + 10);
                        Mod.Ui.Notify.Push("They paid " + TextUtil.Money(tribute) + " to keep the peace.", EventSeverity.Good);
                        Succeed();
                    }
                    else { Mod.Ui.Notify.Push("They laughed in your face - and reached for their guns.", EventSeverity.Bad); TurnHostile(); }
                    break;
                default:
                    TurnHostile();
                    break;
            }
        }

        private void TurnHostile()
        {
            if (_hostile) return;
            _hostile = true;
            Objective = "Eliminate the boss and his guards.";
            var group = EnemyGroup;
            foreach (var p in _guards.Concat(new[] { _boss }).Where(GtaUtil.Alive))
            {
                p.RelationshipGroup = group;
                p.BlockPermanentEvents = false;
                Nat.ConfigureHostile(p, 30 + Difficulty * 5, 20, true);
                if (p != _boss) Nat.GiveWeapon(p, EnemyWeapon(), 300, true);
                Owned.Remove(p);
                Hostiles.Add(p);
                var b = p.AddBlip();
                b.Color = BlipColor.Red;
                b.Scale = p == _boss ? 0.9f : 0.7f;
            }
            Engage(Hostiles);
            ClearObjectiveBlip();
        }
    }

    /// <summary>Eliminate a protected target; it flees when alerted.</summary>
    internal sealed class AssassinationMission : MissionRuntime
    {
        private Ped _target;
        private Vector3 _spot;
        private bool _alerted;

        public AssassinationMission(ModContext m, MissionOffer o, MissionDefinition d) : base(m, o, d) { }

        public override void Start()
        {
            _spot = TargetPoint(500f);
            _target = SpawnHostile(_spot, false);
            if (_target == null) { Fail("The target was nowhere to be found"); return; }
            _target.MaxHealth = 300; _target.Health = 300;
            try { _target.AttachedBlip.Sprite = BlipSprite.Enemy; _target.AttachedBlip.Scale = 1f; _target.AttachedBlip.Name = "Target"; } catch { /* ignore */ }
            Nat.Scenario(_target, "WORLD_HUMAN_SMOKING");
            int guards = ScaledCount(Def.Hostiles);
            for (int i = 0; i < guards; i++) SpawnHostile(_spot + new Vector3(Rng.Range(-8, 8), Rng.Range(-8, 8), 0), false);
            SetObjectiveBlip(_spot, BlipSprite.Enemy, "Target area", true, BlipColor.Red);
            Objective = "Find and eliminate the " + (Rival?.Name ?? "rival") + " target.";
            SetDeadlineSeconds(900);
        }

        public override void Tick(int now)
        {
            UpdateHostiles();
            if (!GtaUtil.Valid(_target)) { Fail("The target disappeared"); return; }
            if (_target.IsDead)
            {
                Objective = "Target down. Get out of there.";
                if (Player.Position.DistanceTo(_spot) > 120f || AliveHostiles == 0) Succeed();
                Detail = AliveHostiles + " guard(s) remaining";
                return;
            }
            float d = Player.Position.DistanceTo(_target.Position);
            Detail = (int)d + " m";
            if (d < 120f && ObjectiveBlip != null) ClearObjectiveBlip();
            if (!_alerted && (d < 30f || Hostiles.Any(h => GtaUtil.Valid(h) && (h.IsDead || h.IsInCombat)) || _target.HasBeenDamagedBy(Player)))
            {
                _alerted = true;
                Engage(Hostiles.Where(h => h != _target));
                _target.Task.FleeFrom(Player);
                Mod.Ui.Notify.Push("The target is running!", EventSeverity.Warning);
            }
            if (_alerted && d > 320f) Fail("The target escaped");
            if (DeadlinePassed) Fail("Out of time");
        }
    }
}
