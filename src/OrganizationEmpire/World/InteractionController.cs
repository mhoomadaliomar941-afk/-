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
    /// Finds the NPC the player is looking at, decides whether it can be interacted with or recruited,
    /// and shows the interaction prompt. Mission-critical and protected NPCs are never eligible.
    /// </summary>
    internal sealed class InteractionController
    {
        private readonly ModContext _mod;
        private int _nextScanAt;
        private readonly Dictionary<int, int> _refusedUntil = new Dictionary<int, int>();

        public Ped Target { get; private set; }
        public MemberRuntime TargetMember { get; private set; }
        public string IneligibleReason { get; private set; }

        public InteractionController(ModContext mod) { _mod = mod; }

        public void Update()
        {
            int now = Game.GameTime;
            if (now < _nextScanAt) return;
            _nextScanAt = now + 150;
            Target = null;
            TargetMember = null;
            IneligibleReason = null;
            var player = GtaUtil.Player;
            if (!GtaUtil.Alive(player) || player.IsInVehicle() || _mod.Ui.IsCapturingInput || Game.IsCutsceneActive) return;

            Ped candidate = null;
            try
            {
                if (Game.Player.TargetedEntity is Ped tp && tp.Position.DistanceTo(player.Position) < 12f) candidate = tp;
            }
            catch { /* ignore */ }
            if (candidate == null)
            {
                float best = 0.55f;
                foreach (var p in GTA.World.GetNearbyPeds(player, 3.5f))
                {
                    if (!GtaUtil.Alive(p) || p.IsPlayer) continue;
                    Vector3 to = p.Position - player.Position;
                    to.Z = 0;
                    if (to.Length() < 0.1f) continue;
                    float dot = Vector3.Dot(to.Normalized, player.ForwardVector);
                    if (dot > best) { best = dot; candidate = p; }
                }
            }
            if (candidate == null || !GtaUtil.Alive(candidate)) return;
            Target = candidate;
            TargetMember = _mod.Members.FindByPed(candidate);
            if (TargetMember == null) IneligibleReason = RecruitBlockReason(candidate);
        }

        /// <summary>Returns null if the ped may be recruited, otherwise a reason.</summary>
        public string RecruitBlockReason(Ped p)
        {
            if (_mod.Faction == null) return "Create your faction first (menu).";
            if (!p.IsHuman) return "Can't recruit that.";
            if (p.IsPlayer) return "That's you.";
            if (Game.IsMissionActive && !(_mod.Missions?.IsActive ?? false)) return "Not during a story mission.";
            if (_mod.Entities.IsTracked(p)) return "Busy.";
            if (Nat.IsMissionEntity(p) || p.IsPersistent) return "This person is involved in something important.";
            var pop = p.PopulationType;
            if (pop == EntityPopulationType.Mission || pop == EntityPopulationType.Permanent || pop == EntityPopulationType.Replay || pop == EntityPopulationType.Tool)
                return "This person can't be recruited.";
            if (p.IsInVehicle() && p.CurrentVehicle.Speed > 1f) return "They're driving.";
            if (p.IsInCombat || p.IsFleeing) return "They're not in a talking mood.";
            if (_refusedUntil.TryGetValue(p.Handle, out int until) && Game.GameTime < until) return "They already turned you down.";
            return null;
        }

        public string ProfileKey(Ped p) => p.Handle.ToString("X") + "_" + unchecked((uint)p.Model.Hash).ToString("X");

        /// <summary>Stable profile for a world NPC (cached while the ped exists).</summary>
        public NpcProfile ProfileFor(Ped p)
        {
            string key = ProfileKey(p);
            if (_mod.State.KnownNpcs.TryGetValue(key, out var existing)) return existing;
            string rivalId = null;
            int pedModel = p.Model.Hash;
            int relHash = Nat.RelationshipGroupHash(p);
            foreach (var r in _mod.State.World.Rivals.Where(x => x.Active))
            {
                if (r.PedModels.Any(mn => Nat.ParseModel(mn).Hash == pedModel) ||
                    (!string.IsNullOrEmpty(r.VanillaRelationshipGroup) && Game.GenerateHash(r.VanillaRelationshipGroup) == relHash))
                {
                    rivalId = r.Id;
                    break;
                }
            }
            bool law = Nat.IsLawEnforcementType(Nat.PedType(p));
            var profile = RecruitmentSystem.CreateProfile(key, Rng.StableHash(key), p.Gender == Gender.Female, rivalId, law, _mod.Config.AI.DefaultPersonality);
            _mod.State.KnownNpcs[key] = profile;
            if (_mod.State.KnownNpcs.Count > 40)
            {
                foreach (var old in _mod.State.KnownNpcs.Keys.Take(_mod.State.KnownNpcs.Count - 40).ToList()) _mod.State.KnownNpcs.Remove(old);
            }
            return profile;
        }

        public RecruitResult TryRecruit(Ped p, RecruitApproach approach)
        {
            string block = RecruitBlockReason(p);
            if (block != null) return new RecruitResult { Message = block };
            var profile = ProfileFor(p);
            string model = Nat.ModelToString(p.Model);
            var appearance = Nat.CaptureAppearance(p);
            var res = RecruitmentSystem.Attempt(_mod.Sim, profile, approach, model, appearance);
            _mod.FlushNotifications();
            if (res.Success && res.Member != null)
            {
                var m = res.Member;
                bool uniform = _mod.Faction.Identity.ApplyUniformToRecruits;
                if (uniform)
                {
                    // Replace the ped with one in faction uniform, keeping identity.
                    var uni = Core.Data.UniformCatalog.Get(_mod.Faction.Identity.UniformPreset) ?? Core.Data.UniformCatalog.DefaultFor(_mod.Faction.Type);
                    m.Model = _mod.Rng.Pick(uni.Models);
                    m.Appearance = new PedAppearance();
                    Vector3 pos = p.Position;
                    float heading = p.Heading;
                    p.Delete();
                    _mod.Members.Spawn(m, pos, heading);
                }
                else
                {
                    _mod.Members.Adopt(p, m);
                }
                Nat.Speech(_mod.Members.Get(m)?.Ped ?? p, "GENERIC_THANKS");
                Core.AI.MemoryStore.AddEntry(_mod.State, m.Id, m.FullName, MemoryKind.Background,
                    "Joined " + _mod.Faction.Name + " after the " + _mod.Faction.PlayerTitle.ToLowerInvariant() + " approached me (" + approach.ToString().ToLowerInvariant() + ").",
                    0.8f, _mod.State.World.Day, _mod.Config.AI.MaxMemoryEntries);
                // Carry over anything said before joining.
                string civId = "npc:" + profile.Key;
                if (_mod.State.Memories.TryGetValue(civId, out var civMem))
                {
                    _mod.State.Memories.Remove(civId);
                    civMem.NpcId = m.Id;
                    _mod.State.Memories[m.Id] = civMem;
                }
                Nat.Sound("CHALLENGE_UNLOCKED", "HUD_AWARDS");
            }
            else
            {
                _refusedUntil[p.Handle] = Game.GameTime + _mod.Config.Gameplay.RecruitRetryCooldownSeconds * 1000;
                Nat.Speech(p, approach == RecruitApproach.Intimidate ? "GENERIC_FRIGHTENED_HIGH" : "GENERIC_INSULT_MED");
                if (approach == RecruitApproach.Intimidate && _mod.Rng.Chance(0.4f)) p.Task.FleeFrom(GtaUtil.Player);
                Nat.Sound("ERROR");
            }
            return res;
        }

        /// <summary>Draws the floating prompt over the targeted NPC.</summary>
        public void DrawPrompt(UI.Renderer r)
        {
            if (Target == null || _mod.Ui.IsOpen || !GtaUtil.Alive(Target)) return;
            if (!_mod.Config.General.ShowHints || !_mod.HasFaction) return;
            // Don't flash prompts at every pedestrian while running through a crowd.
            if (GtaUtil.Player.Speed > 3.5f && Game.Player.TargetedEntity == null) return;
            var head = Target.Position + new Vector3(0, 0, 1.05f);
            var screen = GTA.UI.Screen.WorldToScreen(head);
            if (screen.X <= 0 && screen.Y <= 0) return;
            float nx = screen.X / GTA.UI.Screen.Width;
            float ny = screen.Y / GTA.UI.Screen.Height;
            string name;
            string sub;
            if (TargetMember != null)
            {
                var m = TargetMember.Member;
                name = m.DisplayName;
                sub = (_mod.Faction.GetRank(m.RankId)?.Name ?? "") + " - " + TextUtil.Humanize(m.Role.ToString());
            }
            else
            {
                var prof = _mod.State.KnownNpcs.TryGetValue(ProfileKey(Target), out var pr) ? pr : null;
                name = prof != null ? prof.FullName : "Local";
                sub = IneligibleReason ?? "Can be recruited";
            }
            string keys = "[" + _mod.Config.Hotkeys.Interact + "] Interact   [" + _mod.Config.Hotkeys.Talk + "] Talk";
            r.WorldLabel(nx, ny, name, sub, keys, TargetMember != null);
        }
    }
}
