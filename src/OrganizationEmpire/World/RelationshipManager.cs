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

        // ------------------------------------------------------------------ living world groups

        private readonly Dictionary<string, RelationshipGroup> _worldGroups = new Dictionary<string, RelationshipGroup>();
        private readonly Dictionary<string, Relationship> _appliedWorld = new Dictionary<string, Relationship>();
        private RelationshipGroup? _locals;

        /// <summary>
        /// Group for a faction's people living in the world (crews, battle fighters, leaders). Unlike the war-patrol
        /// group they only attack the player when that faction is actually at war with the player.
        /// </summary>
        public RelationshipGroup WorldGroup(string rivalId)
        {
            string key = rivalId ?? "unknown";
            if (_worldGroups.TryGetValue(key, out var g)) return g;
            g = GTA.World.AddRelationshipGroup("OE_WORLD_" + key.ToUpperInvariant());
            g.SetRelationshipBetweenGroups(g, Relationship.Companion, false);
            _worldGroups[key] = g;
            return g;
        }

        /// <summary>Neighborhood people defending unclaimed turf against an invading faction.</summary>
        public RelationshipGroup Locals
        {
            get
            {
                if (_locals == null)
                {
                    var g = GTA.World.AddRelationshipGroup("OE_LOCALS");
                    g.SetRelationshipBetweenGroups(g, Relationship.Companion, false);
                    _locals = g;
                }
                return _locals.Value;
            }
        }

        public void MakeEnemies(RelationshipGroup a, RelationshipGroup b)
        {
            a.SetRelationshipBetweenGroups(b, Relationship.Hate, true);
        }

        /// <summary>Applies living-world diplomacy (faction vs faction, faction vs player) to the world groups.</summary>
        public void SyncWorld(WorldState world, bool hasFaction)
        {
            var lw = world.Living;
            foreach (var kv in _worldGroups)
            {
                var r = world.GetRival(kv.Key);
                if (r == null) continue;
                var toPlayer = hasFaction && r.Active ? ToWorldRelationship(r.Relation) : Relationship.Neutral;
                ApplyWorld(kv.Key + ">player", kv.Value, PlayerGroup, toPlayer);
                ApplyWorld(kv.Key + ">faction", kv.Value, Faction, toPlayer);
                foreach (var other in _worldGroups)
                {
                    if (string.CompareOrdinal(other.Key, kv.Key) <= 0) continue;
                    var rel = lw?.Get(kv.Key, other.Key);
                    ApplyWorld(kv.Key + ">" + other.Key, kv.Value, other.Value, rel == null ? Relationship.Neutral : ToWorldRelationship(rel.Status));
                }
            }
        }

        private void ApplyWorld(string key, RelationshipGroup a, RelationshipGroup b, Relationship rel)
        {
            if (_appliedWorld.TryGetValue(key, out var applied) && applied == rel) return;
            a.SetRelationshipBetweenGroups(b, rel, true);
            _appliedWorld[key] = rel;
        }

        private static Relationship ToWorldRelationship(RelationStatus s)
        {
            switch (s)
            {
                case RelationStatus.War: return Relationship.Hate;
                case RelationStatus.Rivalry: return Relationship.Dislike;
                case RelationStatus.Alliance: return Relationship.Like;
                case RelationStatus.Peace: return Relationship.Respect;
                default: return Relationship.Neutral;
            }
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
