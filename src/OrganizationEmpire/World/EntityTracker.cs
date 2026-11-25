using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.World
{
    internal enum SpawnCategory { Member, Mission, Rival, Vehicle, Prop, Temp, World }

    /// <summary>
    /// Every entity this mod creates or adopts goes through here: per-category and global caps prevent runaway spawning,
    /// and everything is cleaned up when the script stops.
    /// </summary>
    internal sealed class EntityTracker
    {
        private readonly Func<ModConfig> _config;
        private readonly Dictionary<int, KeyValuePair<Entity, SpawnCategory>> _tracked = new Dictionary<int, KeyValuePair<Entity, SpawnCategory>>();

        public EntityTracker(Func<ModConfig> config) { _config = config; }

        public int Total => _tracked.Count;

        public int Count(SpawnCategory c)
        {
            int n = 0;
            foreach (var kv in _tracked) if (kv.Value.Value == c) n++;
            return n;
        }

        private int CategoryCap(SpawnCategory c)
        {
            var s = _config().Spawning;
            switch (c)
            {
                case SpawnCategory.Member: return 30 + s.MaxHqGuards;
                case SpawnCategory.Mission: return s.MaxMissionHostiles + 8;
                case SpawnCategory.Rival: return s.MaxRivalPatrolPeds;
                case SpawnCategory.World: return s.MaxBattlePeds + s.MaxAmbientCrewPeds + 4;
                case SpawnCategory.Vehicle: return s.MaxActiveFactionVehicles + 6;
                case SpawnCategory.Prop: return 12;
                default: return 10;
            }
        }

        public bool CanSpawn(SpawnCategory c, int count = 1)
        {
            Prune();
            return _tracked.Count + count <= _config().Spawning.GlobalEntityCap && Count(c) + count <= CategoryCap(c);
        }

        public void Track(Entity e, SpawnCategory c)
        {
            if (e == null || !e.Exists()) return;
            _tracked[e.Handle] = new KeyValuePair<Entity, SpawnCategory>(e, c);
        }

        public bool IsTracked(Entity e) => e != null && _tracked.ContainsKey(e.Handle);

        public Ped SpawnPed(string modelName, Vector3 pos, float heading, SpawnCategory c)
        {
            if (!CanSpawn(c)) return null;
            var model = Nat.ParseModel(modelName);
            if (!Nat.LoadModel(model))
            {
                Log.Warn("Ped model unavailable: " + modelName);
                return null;
            }
            var ped = GTA.World.CreatePed(model, pos, heading);
            model.MarkAsNoLongerNeeded();
            if (ped == null || !ped.Exists()) return null;
            ped.IsPersistent = true;
            Track(ped, c);
            return ped;
        }

        public Vehicle SpawnVehicle(string modelName, Vector3 pos, float heading, SpawnCategory c)
        {
            if (!CanSpawn(c)) return null;
            var model = Nat.ParseModel(modelName);
            if (!Nat.LoadModel(model) || !model.IsVehicle)
            {
                Log.Warn("Vehicle model unavailable: " + modelName);
                return null;
            }
            var v = GTA.World.CreateVehicle(model, pos, heading);
            model.MarkAsNoLongerNeeded();
            if (v == null || !v.Exists()) return null;
            v.IsPersistent = true;
            Track(v, c);
            return v;
        }

        public Prop SpawnProp(string modelName, Vector3 pos, Vector3 rotation, SpawnCategory c)
        {
            if (!CanSpawn(c)) return null;
            var model = Nat.ParseModel(modelName);
            if (!Nat.LoadModel(model)) return null;
            var p = GTA.World.CreateProp(model, pos, rotation, false, true);
            model.MarkAsNoLongerNeeded();
            if (p == null || !p.Exists()) return null;
            p.IsPersistent = true;
            Track(p, c);
            return p;
        }

        /// <summary>Stops tracking; deletes the entity or hands it back to the game's population manager.</summary>
        public void Release(Entity e, bool delete)
        {
            if (e == null) return;
            _tracked.Remove(e.Handle);
            try
            {
                if (!e.Exists()) return;
                foreach (var b in e.AttachedBlips) b.Delete();
                if (delete) e.Delete();
                else
                {
                    e.IsPersistent = false;
                    e.MarkAsNoLongerNeeded();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Release failed: " + ex.Message);
            }
        }

        public void Prune()
        {
            if (_tracked.Count == 0) return;
            var dead = new List<int>();
            foreach (var kv in _tracked)
            {
                var e = kv.Value.Key;
                if (e == null || !e.Exists()) dead.Add(kv.Key);
            }
            foreach (var h in dead) _tracked.Remove(h);
        }

        public void CleanupAll(bool deleteEverything)
        {
            foreach (var kv in _tracked.ToList())
            {
                var e = kv.Value.Key;
                bool isPlayerVehicle = e is Vehicle v && Game.Player.Character.IsInVehicle(v);
                Release(e, deleteEverything && !isPlayerVehicle);
            }
            _tracked.Clear();
        }
    }
}
