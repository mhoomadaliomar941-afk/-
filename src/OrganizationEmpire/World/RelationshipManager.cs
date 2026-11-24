using System.Collections.Generic;
using GTA;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.World
{
    /// <summary>
    /// Relationship groups: the player's faction, hostile groups per rival, the state forces, and the mapping of
    /// diplomatic status onto GTA's own ambient gang groups (so vanilla gang NPCs fight you when you are at war).
    /// All changes are runtime only and reset when the game restarts.
    /// </summary>
    internal sealed class RelationshipManager
    {
        public RelationshipGroup Faction { get; private set; }
        public RelationshipGroup StateForces { get; private set; }
        public RelationshipGroup Allies { get; private set; }
        private readonly Dictionary<string, RelationshipGroup> _rivalGroups = new Dictionary<string, RelationshipGroup>();
        private readonly Dictionary<string, RelationStatus> _appliedVanilla = new Dictionary<string, RelationStatus>();
        private int _playerPedHandle;

        public RelationshipGroup PlayerGroup => Game.Player.Character.RelationshipGroup;

        public void Initialize()
        {
            Faction = GTA.World.AddRelationshipGroup("OE_FACTION");
            StateForces = GTA.World.AddRelationshipGroup("OE_STATE");
            Allies = GTA.World.AddRelationshipGroup("OE_ALLIES");
            BindPlayer();
        }

        /// <summary>Call when the player ped changes (character switch, death, model change).</summary>
        public void BindPlayer()
        {
            var player = Game.Player.Character;
            _playerPedHandle = player.Handle;
            var pg = player.RelationshipGroup;
            Faction.SetRelationshipBetweenGroups(pg, Relationship.Companion, true);
            Faction.SetRelationshipBetweenGroups(Faction, Relationship.Companion, false);
            Allies.SetRelationshipBetweenGroups(pg, Relationship.Like, true);
            Allies.SetRelationshipBetweenGroups(Faction, Relationship.Like, true);
            StateForces.SetRelationshipBetweenGroups(pg, Relationship.Hate, true);
            StateForces.SetRelationshipBetweenGroups(Faction, Relationship.Hate, true);
            foreach (var g in _rivalGroups.Values)
            {
                g.SetRelationshipBetweenGroups(pg, Relationship.Hate, true);
                g.SetRelationshipBetweenGroups(Faction, Relationship.Hate, true);
            }
            _appliedVanilla.Clear();
        }

        public bool PlayerChanged() => Game.Player.Character.Handle != _playerPedHandle;

        /// <summary>Hostile group for spawned enemies of a rival (always hostile to the player's side).</summary>
        public RelationshipGroup RivalGroup(string rivalId)
        {
            if (rivalId == FactionIds.State) return StateForces;
            if (_rivalGroups.TryGetValue(rivalId ?? "unknown", out var g)) return g;
            g = GTA.World.AddRelationshipGroup("OE_RIVAL_" + (rivalId ?? "unknown").ToUpperInvariant());
            g.SetRelationshipBetweenGroups(PlayerGroup, Relationship.Hate, true);
            g.SetRelationshipBetweenGroups(Faction, Relationship.Hate, true);
            g.SetRelationshipBetweenGroups(g, Relationship.Companion, false);
            _rivalGroups[rivalId ?? "unknown"] = g;
            return g;
        }

        /// <summary>Maps diplomacy onto GTA's ambient gang relationship groups.</summary>
        public void SyncVanilla(WorldState world, bool hasFaction)
        {
            foreach (var r in world.Rivals)
            {
                if (string.IsNullOrEmpty(r.VanillaRelationshipGroup)) continue;
                RelationStatus target = hasFaction && r.Active ? r.Relation : RelationStatus.Neutral;
                if (_appliedVanilla.TryGetValue(r.Id, out var applied) && applied == target) continue;
                var vanilla = new RelationshipGroup(Game.GenerateHash(r.VanillaRelationshipGroup));
                var pg = PlayerGroup;
                // Clear whatever we set before, returning to the game's defaults.
                if (_appliedVanilla.TryGetValue(r.Id, out var previous))
                {
                    var prevRel = ToRelationship(previous);
                    if (prevRel.HasValue)
                    {
                        vanilla.ClearRelationshipBetweenGroups(pg, prevRel.Value, true);
                        vanilla.ClearRelationshipBetweenGroups(Faction, prevRel.Value, true);
                    }
                }
                var rel = ToRelationship(target);
                if (rel.HasValue)
                {
                    vanilla.SetRelationshipBetweenGroups(pg, rel.Value, true);
                    vanilla.SetRelationshipBetweenGroups(Faction, rel.Value, true);
                }
                _appliedVanilla[r.Id] = target;
                Log.Debug("Vanilla group " + r.VanillaRelationshipGroup + " -> " + target);
            }
        }

        private static Relationship? ToRelationship(RelationStatus s)
        {
            switch (s)
            {
                case RelationStatus.War: return Relationship.Hate;
                case RelationStatus.Rivalry: return Relationship.Dislike;
                case RelationStatus.Alliance: return Relationship.Like;
                case RelationStatus.Peace: return Relationship.Respect;
                default: return null;
            }
        }

        /// <summary>Restores vanilla relationships (script shutdown).</summary>
        public void Reset(WorldState world)
        {
            if (world == null) return;
            foreach (var r in world.Rivals)
            {
                if (string.IsNullOrEmpty(r.VanillaRelationshipGroup) || !_appliedVanilla.TryGetValue(r.Id, out var applied)) continue;
                var rel = ToRelationship(applied);
                if (!rel.HasValue) continue;
                var vanilla = new RelationshipGroup(Game.GenerateHash(r.VanillaRelationshipGroup));
                vanilla.ClearRelationshipBetweenGroups(PlayerGroup, rel.Value, true);
                vanilla.ClearRelationshipBetweenGroups(Faction, rel.Value, true);
            }
            _appliedVanilla.Clear();
        }
    }
}
