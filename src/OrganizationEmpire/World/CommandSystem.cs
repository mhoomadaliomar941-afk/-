using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.World
{
    /// <summary>
    /// The only path by which members receive orders. Every command is validated here, whether it came from the menu,
    /// a hotkey, the quick-order popup or a confirmed suggestion from a conversation. The AI itself can never call this.
    /// </summary>
    internal sealed class CommandSystem
    {
        private readonly ModContext _mod;
        private Ped _lastAimed;
        private int _lastAimedAt;

        public CommandSystem(ModContext mod) { _mod = mod; }

        public static readonly OrderType[] PlayerIssuableOrders =
        {
            OrderType.Follow, OrderType.Stay, OrderType.Guard, OrderType.MoveTo, OrderType.ReturnToHQ, OrderType.EnterVehicle,
            OrderType.ExitVehicle, OrderType.Attack, OrderType.DefendPlayer, OrderType.DefendLocation, OrderType.Standby,
        };

        public static string Describe(OrderType o)
        {
            switch (o)
            {
                case OrderType.Follow: return "Follow me";
                case OrderType.Stay: return "Stay here";
                case OrderType.Guard: return "Guard this spot";
                case OrderType.MoveTo: return "Move to waypoint";
                case OrderType.ReturnToHQ: return "Return to HQ";
                case OrderType.EnterVehicle: return "Get in the vehicle";
                case OrderType.ExitVehicle: return "Get out of the vehicle";
                case OrderType.Attack: return "Attack my target";
                case OrderType.DefendPlayer: return "Defend me";
                case OrderType.DefendLocation: return "Defend this location";
                case OrderType.Standby: return "Stand down (go home)";
                default: return TextUtil.Humanize(o.ToString());
            }
        }

        /// <summary>Remembers the last ped the player aimed at, for "attack my target".</summary>
        public void Update()
        {
            var player = Game.Player;
            Entity e = null;
            try { e = player.TargetedEntity ?? player.LockedOnEntity; } catch { /* ignore */ }
            if (e is Ped p && GtaUtil.Alive(p) && !p.IsPlayer && !_mod.Members.IsMemberPed(p))
            {
                _lastAimed = p;
                _lastAimedAt = Game.GameTime;
            }
        }

        public Ped CurrentTarget()
        {
            if (GtaUtil.Alive(_lastAimed) && Game.GameTime - _lastAimedAt < 8000) return _lastAimed;
            return null;
        }

        private OpResult Validate(Member m, OrderType order, ref Vector3 pos, Ped target, ref Vehicle vehicle)
        {
            var f = _mod.Faction;
            if (f == null) return OpResult.Fail("No faction.");
            if (m == null) return OpResult.Fail("No member selected.");
            if (!m.IsAvailable) return OpResult.Fail(m.ShortName + " is " + m.Status.ToString().ToLowerInvariant() + ".");
            if (!PlayerIssuableOrders.Contains(order)) return OpResult.Fail("That order is not allowed.");
            switch (order)
            {
                case OrderType.ReturnToHQ:
                    if (f.Headquarters == null) return OpResult.Fail("You have no headquarters yet.");
                    break;
                case OrderType.MoveTo:
                    if (pos == Vector3.Zero) pos = GtaUtil.GroundedWaypoint();
                    if (pos == Vector3.Zero) return OpResult.Fail("Set a map waypoint first.");
                    break;
                case OrderType.Stay:
                case OrderType.Guard:
                case OrderType.DefendLocation:
                    if (pos == Vector3.Zero)
                    {
                        var rt = _mod.Members.Get(m);
                        pos = order == OrderType.Stay && rt != null ? rt.Ped.Position : GtaUtil.Player.Position;
                    }
                    break;
                case OrderType.Attack:
                    if (!GtaUtil.Alive(target)) return OpResult.Fail("No valid target. Aim at an enemy first.");
                    if (target.IsPlayer || _mod.Members.IsMemberPed(target)) return OpResult.Fail("You can't order an attack on your own people.");
                    if (target.RelationshipGroup == _mod.Relations.Allies) return OpResult.Fail("That target belongs to an ally.");
                    if (target.Position.DistanceTo(GtaUtil.Player.Position) > 150f) return OpResult.Fail("Target is too far away.");
                    break;
                case OrderType.EnterVehicle:
                    if (!GtaUtil.Valid(vehicle))
                    {
                        var player = GtaUtil.Player;
                        vehicle = player.IsInVehicle() ? player.CurrentVehicle : player.LastVehicle;
                    }
                    if (!GtaUtil.Valid(vehicle) || vehicle.IsDead) return OpResult.Fail("No usable vehicle nearby.");
                    if (vehicle.Position.DistanceTo(GtaUtil.Player.Position) > 80f) return OpResult.Fail("The vehicle is too far away.");
                    break;
            }
            return OpResult.Success();
        }

        public OpResult Order(Member m, OrderType order, Vector3 pos = default(Vector3), Ped target = null, Vehicle vehicle = null, bool remember = true)
        {
            var res = Validate(m, order, ref pos, target, ref vehicle);
            if (!res.Ok) return res;
            var rt = _mod.Members.Get(m);
            if (order == OrderType.Attack)
            {
                if (rt == null) return OpResult.Fail(m.ShortName + " is not here.");
                rt.OrderBeforeAttack = m.Order == OrderType.Attack ? OrderType.Follow : m.Order;
                rt.AttackTarget = target;
            }
            if (order == OrderType.EnterVehicle && rt != null) rt.TargetVehicle = vehicle;
            if (order == OrderType.ExitVehicle && rt == null) return OpResult.Fail(m.ShortName + " is not here.");
            m.Order = order;
            if (pos != Vector3.Zero) m.OrderPosition = pos.ToVec3();
            if (order == OrderType.Follow || order == OrderType.DefendPlayer)
            {
                if (rt == null && _mod.Members.Summon(m) == null)
                    return OpResult.Success(m.ShortName + " will join you when there is room (deployment limit).");
            }
            if (remember)
            {
                MemoryStore.AddEntry(_mod.State, m.Id, m.FullName, MemoryKind.Order, "The " + _mod.Faction.PlayerTitle.ToLowerInvariant() + " ordered: " + Describe(order).ToLowerInvariant() + ".",
                    0.15f, _mod.State.World.Day, _mod.Config.AI.MaxMemoryEntries);
            }
            return OpResult.Success(m.ShortName + ": " + Describe(order) + ".");
        }

        public OpResult OrderMany(IEnumerable<Member> members, OrderType order, Vector3 pos = default(Vector3), Ped target = null, Vehicle vehicle = null)
        {
            int ok = 0, fail = 0;
            string lastError = null;
            var list = members.Where(x => x != null).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 p = pos;
                if (p != Vector3.Zero && list.Count > 1 && (order == OrderType.Stay || order == OrderType.Guard || order == OrderType.MoveTo || order == OrderType.DefendLocation))
                {
                    // Spread members around the point so they don't stack.
                    double a = i * (System.Math.PI * 2 / list.Count);
                    p = new Vector3(pos.X + (float)System.Math.Cos(a) * 2.5f, pos.Y + (float)System.Math.Sin(a) * 2.5f, pos.Z);
                }
                var r = Order(list[i], order, p, target, vehicle, i == 0);
                if (r.Ok) ok++; else { fail++; lastError = r.Message; }
            }
            if (ok == 0) return OpResult.Fail(lastError ?? "Nobody could follow that order.");
            return OpResult.Success(Describe(order) + ": " + ok + " member(s)" + (fail > 0 ? ", " + fail + " could not (" + lastError + ")" : "") + ".");
        }

        public OpResult OrderSquad(Squad s, OrderType order, Vector3 pos = default(Vector3), Ped target = null)
        {
            if (s == null) return OpResult.Fail("No squad selected.");
            Vehicle v = null;
            if (order == OrderType.EnterVehicle && s.VehicleId != null) v = _mod.Vehicles.SpawnedFor(s.VehicleId);
            s.Order = order;
            if (pos != Vector3.Zero) s.OrderPosition = pos.ToVec3();
            var res = OrderMany(SquadSystem.Members(_mod.Faction, s).Where(m => m.IsAvailable), order, pos, target, v);
            return res.Ok ? OpResult.Success(s.Name + " - " + res.Message) : res;
        }

        /// <summary>Orders everyone currently with the player (followers and deployed members nearby).</summary>
        public OpResult OrderField(OrderType order)
        {
            var player = GtaUtil.Player;
            var near = _mod.Members.Deployed.Where(rt => rt.Ped.Position.DistanceTo(player.Position) < 120f).Select(rt => rt.Member).ToList();
            if (near.Count == 0) return OpResult.Fail("No members with you.");
            Vector3 pos = default(Vector3);
            if (order == OrderType.MoveTo) pos = GtaUtil.GroundedWaypoint();
            if (order == OrderType.Guard || order == OrderType.DefendLocation || order == OrderType.Stay) pos = player.Position;
            Ped target = order == OrderType.Attack ? CurrentTarget() : null;
            return OrderMany(near, order, pos, target);
        }

        public OpResult SummonSquad(Squad s)
        {
            if (s == null) return OpResult.Fail("No squad selected.");
            return OrderMany(SquadSystem.Members(_mod.Faction, s).Where(m => m.IsAvailable), OrderType.Follow);
        }

        public OpResult SendHome(Member m) => Order(m, OrderType.Standby);

        /// <summary>Assigns members to guard the HQ at posts around it.</summary>
        public OpResult AssignHqGuards(IEnumerable<Member> members)
        {
            var f = _mod.Faction;
            var hq = f.Headquarters;
            if (hq == null) return OpResult.Fail("You have no headquarters yet.");
            var list = members.Where(m => m.IsAvailable).Take(_mod.Config.Spawning.MaxHqGuards).ToList();
            hq.GuardMemberIds.Clear();
            Vector3 center = hq.Position.ToVector3();
            for (int i = 0; i < list.Count; i++)
            {
                double a = i * (System.Math.PI * 2 / System.Math.Max(1, list.Count)) + 0.4;
                var post = new Vector3(center.X + (float)System.Math.Cos(a) * 7f, center.Y + (float)System.Math.Sin(a) * 7f, center.Z);
                list[i].Order = OrderType.Guard;
                list[i].OrderPosition = post.ToVec3();
                list[i].Role = MemberRole.Guard;
                hq.GuardMemberIds.Add(list[i].Id);
            }
            return OpResult.Success(list.Count + " guard(s) posted at " + hq.Name + ".");
        }

        public OpResult CallToHq(IEnumerable<Member> members)
        {
            if (_mod.Faction.Headquarters == null) return OpResult.Fail("You have no headquarters yet.");
            return OrderMany(members.Where(m => m.IsAvailable), OrderType.ReturnToHQ);
        }

        public OpResult ChangeLoadout(Member m, LoadoutType type)
        {
            var r = MemberSystem.SetLoadout(_mod.Faction, m, type);
            if (r.Ok)
            {
                var rt = _mod.Members.Get(m);
                if (rt != null) _mod.Members.ApplyLoadout(rt);
            }
            return r;
        }
    }
}
