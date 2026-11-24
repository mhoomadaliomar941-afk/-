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
    internal sealed class MemberRuntime
    {
        public Member Member;
        public Ped Ped;
        public Blip Blip;
        public bool InPlayerGroup;
        public OrderType AppliedOrder = OrderType.None;
        public OrderType ArrivedFor = OrderType.None;
        public int NextTaskAt;
        public int NextStuckCheckAt;
        public Vector3 LastPos;
        public int StuckCount;
        public Ped AttackTarget;
        public OrderType OrderBeforeAttack = OrderType.Follow;
        public Vehicle TargetVehicle;
        public int SpawnedAt;
        public int DespawnAt;
        public bool Leaving;
    }

    /// <summary>
    /// Maps faction members to peds in the world. Decides who is physically present (streaming by distance and caps),
    /// executes validated orders with native AI tasks, keeps spacing via formations, recovers stuck members,
    /// and reports deaths back to the simulation.
    /// </summary>
    internal sealed class MemberManager
    {
        private const float StreamInDistance = 150f;
        private const float StreamOutDistance = 260f;
        private const int OrderIntervalMs = 700;

        private readonly ModContext _mod;
        private readonly Dictionary<string, MemberRuntime> _byMember = new Dictionary<string, MemberRuntime>();
        private int _nextDeploymentAt;
        private int _rr;
        private bool _stoodDown;
        public FormationType FieldFormation = FormationType.Default;

        public MemberManager(ModContext mod) { _mod = mod; }

        public IEnumerable<MemberRuntime> Deployed => _byMember.Values;
        public int DeployedCount => _byMember.Count;

        public MemberRuntime Get(Member m) => m != null && _byMember.TryGetValue(m.Id, out var rt) ? rt : null;
        public bool IsDeployed(Member m) => Get(m) != null;

        public MemberRuntime FindByPed(Ped p)
        {
            if (p == null) return null;
            foreach (var rt in _byMember.Values) if (rt.Ped != null && rt.Ped.Handle == p.Handle) return rt;
            return null;
        }

        public bool IsMemberPed(Entity e) => e is Ped p && FindByPed(p) != null;

        private static bool FollowsPlayer(OrderType o) => o == OrderType.Follow || o == OrderType.DefendPlayer || o == OrderType.EnterVehicle || o == OrderType.ExitVehicle || o == OrderType.Attack;
        private static bool PositionOrder(OrderType o) => o == OrderType.Stay || o == OrderType.Guard || o == OrderType.MoveTo || o == OrderType.DefendLocation;

        // ------------------------------------------------------------------ spawning

        /// <summary>Takes ownership of a world ped that just joined the faction.</summary>
        public MemberRuntime Adopt(Ped ped, Member m)
        {
            _mod.Entities.Track(ped, SpawnCategory.Member);
            Nat.SetMissionEntity(ped);
            ped.IsPersistent = true;
            ped.Task.ClearAll();
            var rt = new MemberRuntime { Member = m, Ped = ped, SpawnedAt = Game.GameTime };
            _byMember[m.Id] = rt;
            SetupPed(rt, false);
            return rt;
        }

        public MemberRuntime Spawn(Member m, Vector3 pos, float heading)
        {
            if (_byMember.ContainsKey(m.Id)) return _byMember[m.Id];
            if (_byMember.Count >= _mod.Config.Spawning.MaxDeployedMembers + _mod.Config.Spawning.MaxHqGuards) return null;
            var ped = _mod.Entities.SpawnPed(m.Model, pos, heading, SpawnCategory.Member);
            if (ped == null)
            {
                // Model may be from a DLC the user doesn't have; fall back to the faction uniform.
                var uni = UniformCatalog.Get(_mod.Faction.Identity.UniformPreset) ?? UniformCatalog.DefaultFor(_mod.Faction.Type);
                ped = _mod.Entities.SpawnPed(uni.Models[0], pos, heading, SpawnCategory.Member);
                if (ped == null) return null;
                m.Model = uni.Models[0];
                m.Appearance = new PedAppearance();
            }
            var rt = new MemberRuntime { Member = m, Ped = ped, SpawnedAt = Game.GameTime };
            _byMember[m.Id] = rt;
            SetupPed(rt, true);
            return rt;
        }

        private void SetupPed(MemberRuntime rt, bool applyAppearance)
        {
            var ped = rt.Ped;
            var m = rt.Member;
            var f = _mod.Faction;
            if (applyAppearance)
            {
                if (m.Appearance != null && m.Appearance.Captured) Nat.ApplyAppearance(ped, m.Appearance);
                else Function_RandomizeComponents(ped);
            }
            ped.RelationshipGroup = _mod.Relations.Faction;
            var loadout = LoadoutCatalog.Get(m.Loadout, f.Type);
            Nat.ConfigureMember(ped, m.Skill, loadout.Armor);
            ApplyLoadout(rt);
            if (_mod.Config.World.ShowMemberBlips) CreateBlip(rt);
            rt.AppliedOrder = OrderType.None;
            rt.ArrivedFor = OrderType.None;
            rt.LastPos = ped.Position;
        }

        private static void Function_RandomizeComponents(Ped ped)
        {
            GTA.Native.Function.Call(GTA.Native.Hash.SET_PED_RANDOM_COMPONENT_VARIATION, ped.Handle, 0);
        }

        public void ApplyLoadout(MemberRuntime rt)
        {
            var def = LoadoutCatalog.Get(rt.Member.Loadout, _mod.Faction.Type);
            var ped = rt.Ped;
            if (!GtaUtil.Alive(ped)) return;
            ped.Weapons.RemoveAll();
            for (int i = def.Weapons.Length - 1; i >= 0; i--) Nat.GiveWeapon(ped, def.Weapons[i], 500, i == 0);
            ped.Armor = def.Armor;
        }

        private void CreateBlip(MemberRuntime rt)
        {
            try
            {
                rt.Blip?.Delete();
                var b = rt.Ped.AddBlip();
                b.Scale = 0.62f;
                b.Color = GtaUtil.NearestBlipColor(_mod.Faction.Identity.Primary);
                b.Name = _mod.Faction.Identity.Tag + ": " + rt.Member.ShortName;
                b.IsShortRange = true;
                var squad = _mod.Faction.GetSquad(rt.Member.SquadId);
                if (squad != null && squad.LeaderId == rt.Member.Id) b.ShowsHeadingIndicator = true;
                rt.Blip = b;
            }
            catch (Exception ex) { Log.Debug("Blip failed: " + ex.Message); }
        }

        public void RefreshBlips()
        {
            foreach (var rt in _byMember.Values)
            {
                if (!GtaUtil.Alive(rt.Ped)) continue;
                if (_mod.Config.World.ShowMemberBlips) CreateBlip(rt);
                else { rt.Blip?.Delete(); rt.Blip = null; }
            }
        }

        public void Despawn(MemberRuntime rt, bool delete = true)
        {
            if (rt == null) return;
            _byMember.Remove(rt.Member.Id);
            try
            {
                rt.Blip?.Delete();
                if (GtaUtil.Valid(rt.Ped))
                {
                    if (rt.InPlayerGroup) GtaUtil.Player.PedGroup.Remove(rt.Ped);
                    _mod.Entities.Release(rt.Ped, delete);
                }
            }
            catch (Exception ex) { Log.Debug("Despawn failed: " + ex.Message); }
        }

        public void DespawnAll()
        {
            foreach (var rt in _byMember.Values.ToList()) Despawn(rt);
            _byMember.Clear();
        }

        private Vector3 SpawnPointNearPlayer(int index)
        {
            var player = GtaUtil.Player;
            float side = (index % 2 == 0 ? 1 : -1) * (1.5f + index * 0.7f);
            Vector3 p = player.Position - player.ForwardVector * (6f + index * 0.8f) + player.RightVector * side;
            return Nat.SafePedSpot(p);
        }

        /// <summary>Brings a member to the player right now (respecting caps).</summary>
        public MemberRuntime Summon(Member m)
        {
            var rt = Get(m);
            if (rt != null) return rt;
            if (!m.IsAvailable) return null;
            if (FollowerCount() >= _mod.Config.Spawning.MaxDeployedMembers) return null;
            var player = GtaUtil.Player;
            rt = Spawn(m, SpawnPointNearPlayer(_byMember.Count), player.Heading);
            if (rt != null && player.IsInVehicle())
            {
                var v = player.CurrentVehicle;
                var seat = FreeSeat(v);
                if (seat != VehicleSeat.None) rt.Ped.SetIntoVehicle(v, seat);
            }
            return rt;
        }

        public int FollowerCount() => _byMember.Values.Count(rt => FollowsPlayer(rt.Member.Order));

        public static VehicleSeat FreeSeat(Vehicle v)
        {
            if (!GtaUtil.Valid(v)) return VehicleSeat.None;
            int max = v.PassengerCapacity;
            for (int i = 0; i < max; i++)
            {
                var seat = (VehicleSeat)i;
                if (v.IsSeatFree(seat)) return seat;
            }
            return VehicleSeat.None;
        }

        // ------------------------------------------------------------------ per-tick update

        public void Update()
        {
            var f = _mod.Faction;
            if (f == null) { if (_byMember.Count > 0) DespawnAll(); return; }
            int now = Game.GameTime;

            CheckDeaths();

            if (now >= _nextDeploymentAt)
            {
                _nextDeploymentAt = now + 1500;
                UpdateDeployment(now);
            }

            // Orders, round-robin so cost stays flat no matter how many members are deployed.
            if (_byMember.Count > 0)
            {
                var list = _byMember.Values.ToList();
                int perTick = Math.Max(1, (list.Count + 5) / 6);
                for (int i = 0; i < perTick; i++)
                {
                    _rr = (_rr + 1) % list.Count;
                    var rt = list[_rr];
                    if (now >= rt.NextTaskAt - OrderIntervalMs || rt.AppliedOrder != rt.Member.Order)
                    {
                        try { Execute(rt, now); }
                        catch (Exception ex) { Log.Debug("Order execution failed for " + rt.Member.ShortName + ": " + ex.Message); }
                    }
                }
            }
        }

        private void CheckDeaths()
        {
            foreach (var rt in _byMember.Values.ToList())
            {
                if (!GtaUtil.Valid(rt.Ped))
                {
                    // Removed by the game (e.g. cleanup during a cutscene). The member is fine and will be streamed again.
                    rt.Blip?.Delete();
                    _byMember.Remove(rt.Member.Id);
                    continue;
                }
                if (rt.Ped.IsDead)
                {
                    rt.Blip?.Delete();
                    _byMember.Remove(rt.Member.Id);
                    _mod.Entities.Release(rt.Ped, false);
                    MemberSystem.OnKilled(_mod.Sim, rt.Member, _mod.Config.Gameplay.PermanentDeath);
                    if (rt.Member.Status == MemberStatus.Dead)
                        Core.AI.MemoryStore.Clear(_mod.State, rt.Member.Id);
                    _mod.FlushNotifications();
                    _mod.Missions?.OnMemberLost(rt.Member);
                }
            }
        }

        private void UpdateDeployment(int now)
        {
            var f = _mod.Faction;
            var player = GtaUtil.Player;
            if (!GtaUtil.Alive(player)) return;
            var cfg = _mod.Config;
            bool storyMission = Game.IsMissionActive && !(_mod.Missions?.IsActive ?? false);
            bool standDown = cfg.General.StandDownDuringStoryMissions && storyMission;
            if (standDown && !_stoodDown)
            {
                _stoodDown = true;
                if (_byMember.Count > 0)
                {
                    foreach (var rt in _byMember.Values.ToList()) Despawn(rt);
                    _mod.Ui.Notify.Push("Your people are lying low while you handle personal business.", EventSeverity.Info);
                }
            }
            else if (!standDown && _stoodDown) _stoodDown = false;
            if (_stoodDown) return;

            Vector3 ppos = player.Position;

            // Remove members that are no longer available
            foreach (var rt in _byMember.Values.ToList())
            {
                var m = rt.Member;
                if (!f.Members.Contains(m) || m.Status != MemberStatus.Active)
                {
                    Despawn(rt, !rt.Ped.IsOnScreen);
                    continue;
                }
                float d = rt.Ped.Position.DistanceTo(ppos);
                if (PositionOrder(m.Order) && d > StreamOutDistance && !rt.Ped.IsOnScreen)
                {
                    Despawn(rt);
                    continue;
                }
                if (m.Order == OrderType.Standby)
                {
                    if (!rt.Leaving)
                    {
                        rt.Leaving = true;
                        rt.DespawnAt = now + 20000;
                        if (rt.InPlayerGroup) { player.PedGroup.Remove(rt.Ped); rt.InPlayerGroup = false; }
                        rt.Ped.Task.WanderAround();
                    }
                    if (!rt.Ped.IsOnScreen || d > 60 || now > rt.DespawnAt) Despawn(rt);
                    continue;
                }
                rt.Leaving = false;
                // Followers left far behind (e.g. after the player respawned at a hospital) catch up.
                if (FollowsPlayer(m.Order) && d > 220 && !rt.Ped.IsOnScreen)
                {
                    if (player.IsInVehicle() && FreeSeat(player.CurrentVehicle) != VehicleSeat.None)
                        rt.Ped.SetIntoVehicle(player.CurrentVehicle, FreeSeat(player.CurrentVehicle));
                    else
                        rt.Ped.Position = SpawnPointNearPlayer(rt.Ped.Handle % 5);
                    rt.AppliedOrder = OrderType.None;
                }
            }

            // Stream in followers
            int maxFollowers = cfg.Spawning.MaxDeployedMembers;
            foreach (var m in f.Members.Where(x => x.Status == MemberStatus.Active && FollowsPlayer(x.Order) && !_byMember.ContainsKey(x.Id)).ToList())
            {
                if (FollowerCount() >= maxFollowers) break;
                if (!_mod.Entities.CanSpawn(SpawnCategory.Member)) break;
                Summon(m);
            }

            // Stream in position-based members (including headquarters guards)
            int positional = _byMember.Values.Count(rt => PositionOrder(rt.Member.Order));
            int maxPositional = cfg.Spawning.MaxDeployedMembers + cfg.Spawning.MaxHqGuards;
            foreach (var m in f.Members.Where(x => x.Status == MemberStatus.Active && PositionOrder(x.Order) && !_byMember.ContainsKey(x.Id)).ToList())
            {
                if (positional >= maxPositional || !_mod.Entities.CanSpawn(SpawnCategory.Member)) break;
                var target = m.OrderPosition.ToVector3();
                if (target == Vector3.Zero || target.DistanceTo(ppos) > StreamInDistance) continue;
                var rt = Spawn(m, Nat.SafePedSpot(target), 0f);
                if (rt != null) positional++;
            }
        }

        // ------------------------------------------------------------------ order execution

        private void SetGroup(MemberRuntime rt, bool inGroup)
        {
            var player = GtaUtil.Player;
            var group = player.PedGroup;
            if (inGroup && !rt.InPlayerGroup)
            {
                if (group.MemberCount >= 7) return;
                group.Add(rt.Ped, false);
                rt.Ped.NeverLeavesGroup = true;
                rt.InPlayerGroup = true;
                group.Formation = ToNativeFormation(FieldFormation);
                group.SeparationRange = 999f;
            }
            else if (!inGroup && rt.InPlayerGroup)
            {
                group.Remove(rt.Ped);
                rt.InPlayerGroup = false;
            }
        }

        public static Formation ToNativeFormation(FormationType f)
        {
            switch (f)
            {
                case FormationType.Circle: return Formation.SurroundFacingInwards;
                case FormationType.LineAbreast: return Formation.LineAbreast;
                case FormationType.SingleFile: return Formation.FollowInLine;
                case FormationType.Surround: return Formation.SurroundFacingAhead;
                default: return Formation.Loose;
            }
        }

        public void ApplyFormation(FormationType f)
        {
            FieldFormation = f;
            try { GtaUtil.Player.PedGroup.Formation = ToNativeFormation(f); } catch { /* ignore */ }
            foreach (var rt in _byMember.Values) rt.AppliedOrder = OrderType.None;
        }

        private Vector3 FormationOffset(int index, int count)
        {
            switch (FieldFormation)
            {
                case FormationType.LineAbreast: return new Vector3((index - count / 2f) * 2.4f, -2.5f, 0);
                case FormationType.SingleFile: return new Vector3(0, -3f - index * 2.2f, 0);
                case FormationType.Circle:
                case FormationType.Surround:
                    double a = index * (Math.PI * 2 / Math.Max(1, count));
                    return new Vector3((float)Math.Cos(a) * 4f, (float)Math.Sin(a) * 4f, 0);
                case FormationType.Loose: return new Vector3((index % 2 == 0 ? 1 : -1) * (2.5f + index * 0.6f), -4f - index * 1.8f, 0);
                default: return new Vector3((index % 2 == 0 ? 1.4f : -1.4f), -3f - (index / 2) * 2.2f, 0);
            }
        }

        private void Execute(MemberRuntime rt, int now)
        {
            var ped = rt.Ped;
            var m = rt.Member;
            if (!GtaUtil.Alive(ped) || rt.Leaving) return;
            var player = GtaUtil.Player;
            bool orderChanged = rt.AppliedOrder != m.Order;
            if (orderChanged)
            {
                rt.ArrivedFor = OrderType.None;
                rt.StuckCount = 0;
                if (!FollowsPlayer(m.Order) || m.Order == OrderType.Attack) SetGroup(rt, false);
                if (m.Order != OrderType.Attack) ped.Task.ClearAll();
            }
            bool inCombat = Nat.IsPedInCombat(ped);

            switch (m.Order)
            {
                case OrderType.Follow:
                case OrderType.DefendPlayer:
                    ExecuteFollow(rt, now, orderChanged, inCombat);
                    if (m.Order == OrderType.DefendPlayer && !inCombat && Nat.IsPedInCombat(player))
                    {
                        Nat.CombatAround(ped, 60f);
                        rt.NextTaskAt = now + 4000;
                    }
                    break;

                case OrderType.EnterVehicle:
                    ExecuteEnterVehicle(rt, now, orderChanged);
                    break;

                case OrderType.ExitVehicle:
                    if (ped.IsInVehicle()) { if (orderChanged) ped.Task.LeaveVehicle(); }
                    else { m.Order = OrderType.Stay; m.OrderPosition = ped.Position.ToVec3(); }
                    break;

                case OrderType.Attack:
                    if (!GtaUtil.Alive(rt.AttackTarget))
                    {
                        m.Order = rt.OrderBeforeAttack == OrderType.Attack ? OrderType.Follow : rt.OrderBeforeAttack;
                        rt.AttackTarget = null;
                    }
                    else if (orderChanged || !inCombat || now >= rt.NextTaskAt)
                    {
                        ped.Task.FightAgainst(rt.AttackTarget);
                        rt.NextTaskAt = now + 5000;
                    }
                    break;

                case OrderType.Stay:
                case OrderType.Guard:
                case OrderType.MoveTo:
                case OrderType.DefendLocation:
                    ExecutePosition(rt, now, orderChanged, inCombat);
                    break;

                case OrderType.ReturnToHQ:
                    ExecuteReturnToHq(rt, now, orderChanged, inCombat);
                    break;
            }
            rt.AppliedOrder = m.Order;
        }

        private void ExecuteFollow(MemberRuntime rt, int now, bool orderChanged, bool inCombat)
        {
            var ped = rt.Ped;
            var player = GtaUtil.Player;
            SetGroup(rt, true);
            if (rt.InPlayerGroup)
            {
                if (!player.PedGroup.Contains(ped)) rt.InPlayerGroup = false;
                rt.NextTaskAt = now + 3000;
                return; // the game's group AI handles following, vehicles and combat
            }
            // Overflow followers beyond the 7-member group limit.
            if (inCombat && !orderChanged) { rt.NextTaskAt = now + 2000; return; }
            if (player.IsInVehicle())
            {
                var v = player.CurrentVehicle;
                if (!ped.IsInVehicle(v) && !ped.IsGettingIntoVehicle && ped.Position.DistanceTo(v.Position) < 40f && FreeSeat(v) != VehicleSeat.None)
                {
                    ped.Task.EnterVehicle(v, VehicleSeat.Any, 10000, 2f);
                }
                rt.NextTaskAt = now + 4000;
                return;
            }
            if (ped.IsInVehicle())
            {
                ped.Task.LeaveVehicle();
                rt.NextTaskAt = now + 3000;
                return;
            }
            var overflow = _byMember.Values.Where(x => !x.InPlayerGroup && (x.Member.Order == OrderType.Follow || x.Member.Order == OrderType.DefendPlayer)).ToList();
            int idx = overflow.IndexOf(rt);
            float d = ped.Position.DistanceTo(player.Position);
            if (orderChanged || d > 5f)
            {
                Nat.FollowOffset(ped, player, FormationOffset(Math.Max(0, idx) + 4, overflow.Count + 4), d > 18 ? 3f : 2f);
            }
            rt.NextTaskAt = now + 5000;
        }

        private void ExecuteEnterVehicle(MemberRuntime rt, int now, bool orderChanged)
        {
            var ped = rt.Ped;
            var v = rt.TargetVehicle;
            if (!GtaUtil.Valid(v) || v.IsDead)
            {
                v = GtaUtil.Player.IsInVehicle() ? GtaUtil.Player.CurrentVehicle : GtaUtil.Player.LastVehicle;
                rt.TargetVehicle = v;
            }
            if (!GtaUtil.Valid(v)) { rt.Member.Order = OrderType.Follow; return; }
            if (ped.IsInVehicle(v)) { rt.NextTaskAt = now + 3000; return; }
            if (FreeSeat(v) == VehicleSeat.None && !ped.IsGettingIntoVehicle)
            {
                rt.Member.Order = OrderType.Follow;
                return;
            }
            if (orderChanged || (!ped.IsGettingIntoVehicle && now >= rt.NextTaskAt))
            {
                SetGroup(rt, false);
                ped.Task.EnterVehicle(v, VehicleSeat.Any, 15000, 2f);
                rt.NextTaskAt = now + 8000;
            }
        }

        private bool IsHqGuard(Member m)
        {
            var hq = _mod.Faction.Headquarters;
            return hq != null && hq.GuardMemberIds.Contains(m.Id);
        }

        private void ExecutePosition(MemberRuntime rt, int now, bool orderChanged, bool inCombat)
        {
            var ped = rt.Ped;
            var m = rt.Member;
            Vector3 target = m.OrderPosition.ToVector3();
            if (target == Vector3.Zero) { m.OrderPosition = ped.Position.ToVec3(); target = ped.Position; }
            if (ped.IsInVehicle() && ped.Position.DistanceTo(target) < 60f) { ped.Task.LeaveVehicle(); rt.NextTaskAt = now + 3000; return; }
            if (inCombat && !orderChanged) { rt.NextTaskAt = now + 2500; return; }

            float d = ped.Position.DistanceTo(target);
            if (d > 3.5f)
            {
                rt.ArrivedFor = OrderType.None;
                if (orderChanged || now >= rt.NextTaskAt)
                {
                    Nat.NavTo(ped, target, d > 25 ? 2f : 1.2f);
                    rt.NextTaskAt = now + 6000;
                }
                CheckStuck(rt, now, target);
                return;
            }

            bool needReissue = rt.ArrivedFor != m.Order || now >= rt.NextTaskAt;
            if (!needReissue) return;
            bool hqGuard = IsHqGuard(m);
            var hq = _mod.Faction.Headquarters;
            switch (m.Order)
            {
                case OrderType.Stay:
                case OrderType.MoveTo:
                    if (rt.ArrivedFor != m.Order) ped.Task.StandStill(-1);
                    break;
                case OrderType.Guard:
                    if (hqGuard && hq.GuardBehavior == GuardBehavior.Patrol)
                    {
                        Nat.WanderInArea(ped, hq.Position.ToVector3(), 18f);
                    }
                    else
                    {
                        Nat.GuardSphere(ped, target, hqGuard && hq.GuardBehavior == GuardBehavior.Alert ? 25f : 10f);
                        if (rt.ArrivedFor != m.Order) ped.Task.GuardCurrentPosition();
                    }
                    break;
                case OrderType.DefendLocation:
                    Nat.GuardSphere(ped, target, 20f);
                    Nat.CombatAround(ped, 45f);
                    break;
            }
            rt.ArrivedFor = m.Order;
            rt.NextTaskAt = now + (m.Order == OrderType.DefendLocation ? 6000 : 20000);
        }

        private void ExecuteReturnToHq(MemberRuntime rt, int now, bool orderChanged, bool inCombat)
        {
            var ped = rt.Ped;
            var hq = _mod.Faction.Headquarters;
            if (hq == null) { rt.Member.Order = OrderType.Follow; return; }
            Vector3 target = hq.Position.ToVector3();
            float d = ped.Position.DistanceTo(target);
            float playerDist = ped.Position.DistanceTo(GtaUtil.Player.Position);
            if (d < 8f || (!ped.IsOnScreen && playerDist > 120f))
            {
                // Arrived (or out of sight): the member is now at the HQ, off duty.
                rt.Member.Order = OrderType.Standby;
                Despawn(rt);
                return;
            }
            if (inCombat && !orderChanged) return;
            if (orderChanged || now >= rt.NextTaskAt)
            {
                Nat.NavTo(ped, target, 2f);
                rt.NextTaskAt = now + 8000;
            }
            CheckStuck(rt, now, target);
        }

        private void CheckStuck(MemberRuntime rt, int now, Vector3 goal)
        {
            if (now < rt.NextStuckCheckAt) return;
            rt.NextStuckCheckAt = now + 3000;
            var ped = rt.Ped;
            if (ped.IsInVehicle() || ped.IsRagdoll || ped.IsGettingUp) { rt.LastPos = ped.Position; return; }
            float moved = ped.Position.DistanceTo(rt.LastPos);
            rt.LastPos = ped.Position;
            if (moved > 0.8f) { rt.StuckCount = 0; return; }
            rt.StuckCount++;
            if (rt.StuckCount == 2)
            {
                Nat.GoToAnyMeans(ped, goal, 2f);
            }
            else if (rt.StuckCount >= 4 && !ped.IsOnScreen)
            {
                Vector3 toward = ped.Position + (goal - ped.Position).Normalized * Math.Min(30f, ped.Position.DistanceTo(goal));
                ped.Position = Nat.SafePedSpot(toward);
                rt.StuckCount = 0;
                rt.NextTaskAt = 0;
                Log.Debug(rt.Member.ShortName + " was stuck and has been repositioned.");
            }
        }

        public void OnPlayerChanged()
        {
            foreach (var rt in _byMember.Values)
            {
                rt.InPlayerGroup = false;
                rt.AppliedOrder = OrderType.None;
            }
        }
    }
}
