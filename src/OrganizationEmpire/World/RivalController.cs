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
    /// Live rival presence: capped hostile patrols in enemy territory during wars, kill attribution for
    /// ambient gang NPCs (territory influence, war score), and reversible runtime suppression of vanilla gang models
    /// for factions the player removed from the simulation. No game files are modified.
    /// </summary>
    internal sealed class RivalController
    {
        private sealed class Patrol
        {
            public string RivalId;
            public List<Ped> Peds = new List<Ped>();
            public int SpawnedAt;
        }

        private readonly ModContext _mod;
        private readonly List<Patrol> _patrols = new List<Patrol>();
        private readonly HashSet<int> _countedDead = new HashSet<int>();
        private readonly HashSet<int> _suppressed = new HashSet<int>();
        private int _nextPatrolCheckAt, _nextKillScanAt, _nextSuppressionAt, _nextRelSyncAt;
        private int _patrolCooldownUntil;

        public RivalController(ModContext mod) { _mod = mod; }

        public void Update()
        {
            int now = Game.GameTime;
            if (now >= _nextRelSyncAt)
            {
                _nextRelSyncAt = now + 3000;
                _mod.Relations.SyncVanilla(_mod.State.World, _mod.HasFaction);
            }
            if (now >= _nextSuppressionAt)
            {
                _nextSuppressionAt = now + 10000;
                ApplySuppression();
            }
            if (!_mod.HasFaction) { DespawnAll(); return; }
            if (now >= _nextKillScanAt)
            {
                _nextKillScanAt = now + 1200;
                ScanKills();
            }
            if (now >= _nextPatrolCheckAt)
            {
                _nextPatrolCheckAt = now + 3000;
                UpdatePatrols(now);
            }
        }

        private void UpdatePatrols(int now)
        {
            var player = GtaUtil.Player;
            foreach (var p in _patrols.ToList())
            {
                p.Peds.RemoveAll(ped =>
                {
                    if (!GtaUtil.Valid(ped)) return true;
                    float d = ped.Position.DistanceTo(player.Position);
                    if ((ped.IsDead && d > 60) || (d > 200 && !ped.IsOnScreen))
                    {
                        _mod.Entities.Release(ped, !ped.IsDead && !ped.IsOnScreen);
                        return true;
                    }
                    return false;
                });
                if (p.Peds.Count == 0) _patrols.Remove(p);
            }

            if (!_mod.Config.World.RivalPatrols || _mod.Missions.IsActive || Game.IsMissionActive || now < _patrolCooldownUntil) return;
            var t = _mod.Territories.Current;
            if (t == null || t.OwnerId == null) return;
            var rival = _mod.State.World.GetRival(t.OwnerId);
            if (rival == null || !rival.Active || rival.Relation != RelationStatus.War) return;
            int alive = _patrols.Sum(p => p.Peds.Count(x => GtaUtil.Alive(x)));
            int cap = _mod.Config.Spawning.MaxRivalPatrolPeds;
            if (alive >= cap) return;
            float chance = 0.25f * _mod.Config.World.RivalActivity * (0.5f + rival.Strength / 100f);
            if (!_mod.Rng.Chance(chance)) return;
            SpawnPatrol(rival, Math.Min(cap - alive, 2 + _mod.Rng.Next(0, 3)));
            _patrolCooldownUntil = now + 60000 + _mod.Rng.Next(0, 60000);
        }

        private void SpawnPatrol(RivalFaction rival, int count)
        {
            if (count <= 0 || rival.PedModels.Count == 0) return;
            Vector3 basePos = Nat.SafePedSpot(GtaUtil.BehindCamera(85f, 0.7f, _mod.Rng));
            var patrol = new Patrol { RivalId = rival.Id, SpawnedAt = Game.GameTime };
            var group = _mod.Relations.RivalGroup(rival.Id);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = Nat.SafePedSpot(basePos + new Vector3(_mod.Rng.Range(-3, 3), _mod.Rng.Range(-3, 3), 0));
                var ped = _mod.Entities.SpawnPed(_mod.Rng.Pick(rival.PedModels), pos, _mod.Rng.Range(0, 360), SpawnCategory.Rival);
                if (ped == null) break;
                ped.RelationshipGroup = group;
                Nat.ConfigureHostile(ped, 25 + rival.Strength * 0.2f, rival.Strength > 60 ? 30 : 0, true);
                Nat.GiveWeapon(ped, rival.Weapon, 300, true);
                Nat.CombatAround(ped, 90f);
                var b = ped.AddBlip();
                b.Color = BlipColor.Red;
                b.Scale = 0.6f;
                b.Name = rival.Name;
                b.IsShortRange = true;
                patrol.Peds.Add(ped);
            }
            if (patrol.Peds.Count > 0)
            {
                _patrols.Add(patrol);
                _mod.Ui.Notify.Push(rival.Name + " crew spotted nearby!", EventSeverity.Warning);
            }
        }

        public void DespawnAll()
        {
            foreach (var p in _patrols) foreach (var ped in p.Peds) _mod.Entities.Release(ped, true);
            _patrols.Clear();
        }

        /// <summary>Credits kills of rival NPCs (spawned or ambient vanilla gang peds) made by the player or members.</summary>
        private void ScanKills()
        {
            var player = GtaUtil.Player;
            var world = _mod.State.World;
            if (_countedDead.Count > 500) _countedDead.Clear();
            foreach (var ped in GTA.World.GetNearbyPeds(player, 80f))
            {
                if (!ped.IsDead || _countedDead.Contains(ped.Handle) || ped.IsPlayer) continue;
                var killer = ped.Killer as Ped;
                if (killer == null) continue;
                bool byPlayer = killer.IsPlayer;
                var memberRt = byPlayer ? null : _mod.Members.FindByPed(killer);
                if (!byPlayer && memberRt == null) continue;
                _countedDead.Add(ped.Handle);
                if (_mod.Members.IsMemberPed(ped)) continue;
                var rival = RivalOf(ped);
                if (memberRt != null) memberRt.Member.Kills++;
                if (rival == null) continue;
                var t = world.FindByZone(GtaUtil.ZoneCode(ped.Position));
                WarSystem.OnEnemiesKilled(_mod.Sim, rival, 1);
                if (t != null) TerritorySystem.OnCombatInTerritory(_mod.Sim, t, rival.Id, 1);
                SuspicionSystem.OnPublicIncident(_mod.Sim, 0.4f);
                _mod.FlushNotifications();
            }
        }

        public RivalFaction RivalOf(Ped ped)
        {
            int model = ped.Model.Hash;
            int rel = Nat.RelationshipGroupHash(ped);
            foreach (var r in _mod.State.World.Rivals)
            {
                if (!r.Active) continue;
                if (_mod.Relations.RivalGroup(r.Id).Hash == rel) return r;
                if (!string.IsNullOrEmpty(r.VanillaRelationshipGroup) && Game.GenerateHash(r.VanillaRelationshipGroup) == rel) return r;
                foreach (var mn in r.PedModels) if (Nat.ParseModel(mn).Hash == model) return r;
            }
            return null;
        }

        /// <summary>Suppresses GTA ambient spawns of disabled rivals' gang models (runtime only, reversible).</summary>
        public void ApplySuppression()
        {
            var want = new HashSet<int>();
            if (_mod.Config.World.SuppressVanillaPedsOfDisabledRivals)
            {
                foreach (var r in _mod.State.World.Rivals.Where(r => !r.Enabled || r.Eliminated))
                {
                    // Only suppress dedicated gang models, never generic civilians or law enforcement.
                    if (r.Kind == RivalKind.Paramilitary) continue;
                    foreach (var mn in r.PedModels.Where(x => x.StartsWith("g_", StringComparison.OrdinalIgnoreCase) || x.StartsWith("a_m_m_hillbilly", StringComparison.OrdinalIgnoreCase)))
                        want.Add(Nat.ParseModel(mn).Hash);
                }
            }
            foreach (int h in _suppressed.Where(h => !want.Contains(h)).ToList())
            {
                Nat.SetModelSuppressed(h, false);
                _suppressed.Remove(h);
            }
            foreach (int h in want.Where(h => !_suppressed.Contains(h)))
            {
                Nat.SetModelSuppressed(h, true);
                _suppressed.Add(h);
            }
            if (_suppressed.Count == 0) return;
            // Quietly remove already-spawned ambient peds of suppressed models that are off screen.
            foreach (var ped in GTA.World.GetNearbyPeds(GtaUtil.Player, 150f))
            {
                if (!_suppressed.Contains(ped.Model.Hash) || ped.IsOnScreen || _mod.Entities.IsTracked(ped) || Nat.IsMissionEntity(ped)) continue;
                var pop = ped.PopulationType;
                if (pop == EntityPopulationType.RandomAmbient || pop == EntityPopulationType.RandomScenario || pop == EntityPopulationType.RandomPatrol)
                    ped.Delete();
            }
        }

        public void ReleaseSuppression()
        {
            foreach (int h in _suppressed) Nat.SetModelSuppressed(h, false);
            _suppressed.Clear();
        }
    }
}
