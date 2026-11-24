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

namespace OrganizationEmpire.World
{
    internal sealed class SpawnedVehicle
    {
        public FactionVehicle Def;
        public Vehicle Vehicle;
        public Blip Blip;
        public Ped TempDriver;
        public MemberRuntime MemberDriver;
        public bool Delivering;
        public int DeliveryStartedAt;
    }

    /// <summary>Faction garage: register, spawn, call (with driver), color with faction identity, store, track destruction.</summary>
    internal sealed class VehicleController
    {
        private readonly ModContext _mod;
        private readonly Dictionary<string, SpawnedVehicle> _spawned = new Dictionary<string, SpawnedVehicle>();
        private int _nextCheckAt;

        public VehicleController(ModContext mod) { _mod = mod; }

        public IEnumerable<SpawnedVehicle> Spawned => _spawned.Values;

        public Vehicle SpawnedFor(string vehicleId) => vehicleId != null && _spawned.TryGetValue(vehicleId, out var s) && GtaUtil.Valid(s.Vehicle) ? s.Vehicle : null;

        public bool IsSpawned(FactionVehicle v) => SpawnedFor(v?.Id) != null;

        public int MaxGarage()
        {
            var f = _mod.Faction;
            int garage = f?.Headquarters?.Level(HqUpgrade.Garage) ?? 0;
            return 4 + f.Level / 3 + garage * 2;
        }

        public OpResult RegisterCurrent()
        {
            var f = _mod.Faction;
            var player = GtaUtil.Player;
            if (!player.IsInVehicle()) return OpResult.Fail("Get in the vehicle you want to register.");
            if (f.Vehicles.Count >= MaxGarage()) return OpResult.Fail("Garage full (" + MaxGarage() + "). Upgrade the HQ Garage or level up.");
            var v = player.CurrentVehicle;
            if (v.IsDead) return OpResult.Fail("That vehicle is wrecked.");
            var fv = new FactionVehicle
            {
                Id = Ids.New("veh"),
                Model = Nat.ModelToString(v.Model),
                DisplayName = SafeDisplayName(v),
                UseFactionColors = true,
                Plate = MakePlate(f),
                Seats = v.PassengerCapacity + 1,
            };
            if (v.Mods.IsPrimaryColorCustom) fv.PrimaryColor = ToHex(v.Mods.CustomPrimaryColor);
            if (v.Mods.IsSecondaryColorCustom) fv.SecondaryColor = ToHex(v.Mods.CustomSecondaryColor);
            f.Vehicles.Add(fv);
            var sv = new SpawnedVehicle { Def = fv, Vehicle = v };
            _mod.Entities.Track(v, SpawnCategory.Vehicle);
            v.IsPersistent = true;
            _spawned[fv.Id] = sv;
            ApplyIdentity(sv);
            return OpResult.Success(fv.DisplayName + " added to the faction garage.");
        }

        private static string ToHex(System.Drawing.Color c) => new Rgb(c.R, c.G, c.B).ToHex();

        private static string SafeDisplayName(Vehicle v)
        {
            try
            {
                string label = v.DisplayName;
                string loc = Game.GetLocalizedString(label);
                return string.IsNullOrEmpty(loc) || loc == "NULL" ? label : loc;
            }
            catch { return "Vehicle"; }
        }

        private static string MakePlate(FactionState f)
        {
            string tag = new string((f.Identity.Tag ?? "OE").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (tag.Length == 0) tag = "OE";
            if (tag.Length > 4) tag = tag.Substring(0, 4);
            return (tag + " " + (f.Vehicles.Count + 1).ToString("000")).Substring(0, Math.Min(8, tag.Length + 4));
        }

        public void ApplyIdentity(SpawnedVehicle sv)
        {
            var f = _mod.Faction;
            var v = sv.Vehicle;
            if (!GtaUtil.Valid(v)) return;
            try
            {
                var identity = f.IsGovernment ? f.Government.StateIdentity : f.Identity;
                if (sv.Def.UseFactionColors)
                {
                    v.Mods.CustomPrimaryColor = identity.Primary.ToColor();
                    v.Mods.CustomSecondaryColor = identity.Secondary.ToColor();
                }
                else
                {
                    if (Rgb.TryParse(sv.Def.PrimaryColor, out var p)) v.Mods.CustomPrimaryColor = p.ToColor();
                    if (Rgb.TryParse(sv.Def.SecondaryColor, out var s)) v.Mods.CustomSecondaryColor = s.ToColor();
                }
                if (!string.IsNullOrEmpty(sv.Def.Plate)) v.Mods.LicensePlate = sv.Def.Plate;
                sv.Blip?.Delete();
                sv.Blip = v.AddBlip();
                sv.Blip.Sprite = v.Model.IsBike ? BlipSprite.PersonalVehicleBike : BlipSprite.PersonalVehicleCar;
                sv.Blip.Color = GtaUtil.NearestBlipColor(identity.Primary);
                sv.Blip.Name = "Faction: " + sv.Def.DisplayName;
                sv.Blip.IsShortRange = true;
                sv.Blip.Scale = 0.75f;
            }
            catch (Exception ex) { Log.Debug("ApplyIdentity failed: " + ex.Message); }
        }

        public OpResult ColorCurrentVehicle()
        {
            var player = GtaUtil.Player;
            if (!player.IsInVehicle()) return OpResult.Fail("Get in a vehicle first.");
            var identity = _mod.Faction.IsGovernment ? _mod.Faction.Government.StateIdentity : _mod.Faction.Identity;
            player.CurrentVehicle.Mods.CustomPrimaryColor = identity.Primary.ToColor();
            player.CurrentVehicle.Mods.CustomSecondaryColor = identity.Secondary.ToColor();
            return OpResult.Success("Painted in faction colors.");
        }

        /// <summary>Spawns the vehicle at the nearest road near the player.</summary>
        public OpResult SpawnNearPlayer(FactionVehicle fv)
        {
            if (fv == null) return OpResult.Fail("No vehicle selected.");
            if (fv.Destroyed) return OpResult.Fail(fv.DisplayName + " is being repaired (" + (int)Math.Ceiling(fv.RepairHoursLeft) + "h).");
            if (IsSpawned(fv)) return OpResult.Fail(fv.DisplayName + " is already out.");
            if (_spawned.Count >= _mod.Config.Spawning.MaxActiveFactionVehicles) return OpResult.Fail("Too many faction vehicles out (" + _mod.Config.Spawning.MaxActiveFactionVehicles + ").");
            var player = GtaUtil.Player;
            Vector3 near = player.Position + player.ForwardVector * 8f;
            Vector3 pos;
            float heading;
            if (!Nat.StreetNodeWithHeading(near, out pos, out heading)) { pos = GTA.World.GetNextPositionOnStreet(near, true); heading = player.Heading; }
            if (pos.DistanceTo(player.Position) > 60f) { pos = player.Position + player.RightVector * 4f; heading = player.Heading; }
            var v = _mod.Entities.SpawnVehicle(fv.Model, pos, heading, SpawnCategory.Vehicle);
            if (v == null) return OpResult.Fail("Could not spawn " + fv.DisplayName + " (model unavailable or spawn limit reached).");
            v.PlaceOnGround();
            var sv = new SpawnedVehicle { Def = fv, Vehicle = v };
            _spawned[fv.Id] = sv;
            ApplyIdentity(sv);
            return OpResult.Success(fv.DisplayName + " is ready.");
        }

        /// <summary>Has the vehicle driven to the player by its assigned driver (or a faction driver).</summary>
        public OpResult CallWithDriver(FactionVehicle fv)
        {
            if (fv == null) return OpResult.Fail("No vehicle selected.");
            if (fv.Destroyed) return OpResult.Fail(fv.DisplayName + " is being repaired.");
            if (IsSpawned(fv))
            {
                var existing = _spawned[fv.Id];
                return DriveExistingToPlayer(existing);
            }
            if (_spawned.Count >= _mod.Config.Spawning.MaxActiveFactionVehicles) return OpResult.Fail("Too many faction vehicles out.");
            var player = GtaUtil.Player;
            Vector3 origin = GtaUtil.BehindCamera(140f, 0.6f, _mod.Rng);
            if (!Nat.StreetNodeWithHeading(origin, out Vector3 pos, out float heading)) return OpResult.Fail("No road found nearby.");
            var v = _mod.Entities.SpawnVehicle(fv.Model, pos, heading, SpawnCategory.Vehicle);
            if (v == null) return OpResult.Fail("Could not spawn " + fv.DisplayName + ".");
            v.PlaceOnGround();
            v.IsEngineRunning = true;
            var sv = new SpawnedVehicle { Def = fv, Vehicle = v };
            _spawned[fv.Id] = sv;
            ApplyIdentity(sv);

            var driverMember = _mod.Faction.GetMember(fv.DriverMemberId);
            if (driverMember != null && driverMember.IsAvailable && !_mod.Members.IsDeployed(driverMember))
            {
                var rt = _mod.Members.Spawn(driverMember, pos + new Vector3(0, 0, 1), heading);
                if (rt != null)
                {
                    rt.Ped.SetIntoVehicle(v, VehicleSeat.Driver);
                    driverMember.Order = OrderType.EnterVehicle;
                    rt.TargetVehicle = v;
                    rt.AppliedOrder = OrderType.EnterVehicle;
                    sv.MemberDriver = rt;
                }
            }
            if (sv.MemberDriver == null)
            {
                var uni = UniformCatalog.Get(_mod.Faction.Identity.UniformPreset) ?? UniformCatalog.DefaultFor(_mod.Faction.Type);
                var ped = _mod.Entities.SpawnPed(_mod.Rng.Pick(uni.Models), pos + new Vector3(0, 0, 1), heading, SpawnCategory.Temp);
                if (ped == null) return OpResult.Success(fv.DisplayName + " spawned nearby, but no driver was available.");
                ped.RelationshipGroup = _mod.Relations.Faction;
                ped.SetIntoVehicle(v, VehicleSeat.Driver);
                ped.BlockPermanentEvents = true;
                sv.TempDriver = ped;
            }
            return DriveExistingToPlayer(sv);
        }

        private OpResult DriveExistingToPlayer(SpawnedVehicle sv)
        {
            var driver = sv.MemberDriver?.Ped ?? sv.TempDriver ?? sv.Vehicle.Driver;
            if (!GtaUtil.Alive(driver)) return OpResult.Fail(sv.Def.DisplayName + " has no driver.");
            Nat.DriveTo(driver, sv.Vehicle, GtaUtil.Player.Position, 22f, 12f);
            sv.Delivering = true;
            sv.DeliveryStartedAt = Game.GameTime;
            return OpResult.Success(sv.Def.DisplayName + " is on the way.");
        }

        public OpResult Store(FactionVehicle fv)
        {
            if (!_spawned.TryGetValue(fv.Id, out var sv)) return OpResult.Fail(fv.DisplayName + " is already in the garage.");
            if (GtaUtil.Valid(sv.Vehicle) && GtaUtil.Player.IsInVehicle(sv.Vehicle)) return OpResult.Fail("Get out of the vehicle first.");
            Despawn(sv);
            return OpResult.Success(fv.DisplayName + " returned to the garage.");
        }

        public OpResult Remove(FactionVehicle fv)
        {
            if (_spawned.TryGetValue(fv.Id, out var sv))
            {
                sv.Blip?.Delete();
                _mod.Entities.Release(sv.Vehicle, false);
                _spawned.Remove(fv.Id);
            }
            _mod.Faction.Vehicles.Remove(fv);
            foreach (var s in _mod.Faction.Squads.Where(s => s.VehicleId == fv.Id)) s.VehicleId = null;
            return OpResult.Success(fv.DisplayName + " removed from the garage.");
        }

        private void Despawn(SpawnedVehicle sv)
        {
            _spawned.Remove(sv.Def.Id);
            sv.Blip?.Delete();
            if (sv.TempDriver != null) _mod.Entities.Release(sv.TempDriver, true);
            if (GtaUtil.Valid(sv.Vehicle))
            {
                bool occupiedByPlayer = GtaUtil.Player.IsInVehicle(sv.Vehicle);
                if (!occupiedByPlayer)
                {
                    foreach (var occ in sv.Vehicle.Occupants)
                    {
                        var rt = _mod.Members.FindByPed(occ);
                        if (rt != null) rt.Ped.Task.LeaveVehicle();
                    }
                }
                _mod.Entities.Release(sv.Vehicle, !occupiedByPlayer && !sv.Vehicle.IsOnScreen);
            }
        }

        public void DespawnAll()
        {
            foreach (var sv in _spawned.Values.ToList()) Despawn(sv);
        }

        public void Update()
        {
            int now = Game.GameTime;
            if (now < _nextCheckAt || _spawned.Count == 0) return;
            _nextCheckAt = now + 1000;
            var player = GtaUtil.Player;
            foreach (var sv in _spawned.Values.ToList())
            {
                var v = sv.Vehicle;
                if (!GtaUtil.Valid(v)) { _spawned.Remove(sv.Def.Id); sv.Blip?.Delete(); continue; }
                if (v.IsDead || v.IsConsideredDestroyed)
                {
                    sv.Def.Destroyed = true;
                    sv.Def.RepairHoursLeft = 12;
                    _mod.Ui.Notify.Push(sv.Def.DisplayName + " was destroyed. It will be repaired in about 12 hours.", EventSeverity.Bad);
                    sv.Blip?.Delete();
                    _spawned.Remove(sv.Def.Id);
                    _mod.Entities.Release(v, false);
                    if (sv.TempDriver != null) _mod.Entities.Release(sv.TempDriver, false);
                    continue;
                }
                float d = v.Position.DistanceTo(player.Position);
                if (sv.Delivering)
                {
                    if (d < 15f)
                    {
                        sv.Delivering = false;
                        if (sv.TempDriver != null && GtaUtil.Alive(sv.TempDriver))
                        {
                            sv.TempDriver.Task.LeaveVehicle(v, true);
                            sv.TempDriver.Task.WanderAround();
                            _mod.Entities.Release(sv.TempDriver, false);
                            sv.TempDriver = null;
                        }
                        _mod.Ui.Notify.Push(sv.Def.DisplayName + " has arrived.", EventSeverity.Good);
                    }
                    else if (now - sv.DeliveryStartedAt > 120000)
                    {
                        // Give up driving and drop it nearby out of sight.
                        sv.Delivering = false;
                        if (!v.IsOnScreen && Nat.StreetNodeWithHeading(player.Position + player.ForwardVector * 20f, out Vector3 p, out float h))
                        {
                            v.Position = p;
                            v.Heading = h;
                        }
                    }
                }
                else if (d > 450f && !v.IsOnScreen && !player.IsInVehicle(v))
                {
                    Despawn(sv);
                }
            }
        }
    }
}
