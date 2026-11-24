using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Persistence;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.World;

namespace OrganizationEmpire.Missions
{
    /// <summary>Starts, runs, finishes and cleans up the active mission; applies rewards through the simulation.</summary>
    internal sealed class MissionController
    {
        private readonly ModContext _mod;
        private int _membersLost;
        private int _savedMaxWanted = -1;

        public MissionRuntime Active { get; private set; }
        public bool IsActive => Active != null;

        public MissionController(ModContext mod) { _mod = mod; }

        public static MissionRuntime Create(ModContext mod, MissionOffer offer, MissionDefinition def)
        {
            switch (def.Template)
            {
                case MissionTemplate.Assault: return new AssaultMission(mod, offer, def);
                case MissionTemplate.Defend: return new DefendMission(mod, offer, def, GTA.Math.Vector3.Zero);
                case MissionTemplate.Delivery: return new DeliveryMission(mod, offer, def);
                case MissionTemplate.Collect: return new CollectMission(mod, offer, def);
                case MissionTemplate.Escort: return new EscortMission(mod, offer, def);
                case MissionTemplate.Recruit: return new RecruitMission(mod, offer, def);
                case MissionTemplate.Recon: return new ReconMission(mod, offer, def);
                case MissionTemplate.Meeting: return new MeetingMission(mod, offer, def);
                case MissionTemplate.Assassination: return new AssassinationMission(mod, offer, def);
                case MissionTemplate.FinalCampaign:
                    return new DefendMission(mod, offer, def, MissionLocations.FinalLocation(mod.Faction.Type), true, 45);
                default: return null;
            }
        }

        public OpResult Start(MissionOffer offer)
        {
            if (IsActive) return OpResult.Fail("Finish or abort the current mission first.");
            if (!_mod.HasFaction) return OpResult.Fail("No faction.");
            if (Game.IsMissionActive) return OpResult.Fail("Finish the story mission first.");
            var player = GtaUtil.Player;
            if (!GtaUtil.Alive(player)) return OpResult.Fail("You can't start a mission right now.");
            var def = MissionCatalog.Get(offer.DefinitionId);
            if (def == null) return OpResult.Fail("Unknown mission.");
            if (!offer.Urgent && !MissionCatalog.IsEligible(_mod.Sim, def, out string reason)) return OpResult.Fail(reason);

            if (def.Template == MissionTemplate.FinalCampaign)
            {
                var backup = _mod.Saves.Save(_mod.State, SaveManager.PreTakeoverSlot);
                Log.Info("Pre-takeover backup: " + backup.Message);
            }

            var rt = Create(_mod, offer, def);
            if (rt == null) return OpResult.Fail("This mission type is not supported.");
            _membersLost = 0;
            Active = rt;
            rt.StartedAt = Game.GameTime;
            foreach (var m in _mod.Members.Deployed) rt.Participants.Add(m.Member.Id);
            try { rt.Start(); }
            catch (Exception ex)
            {
                Log.Error("Mission start failed", ex);
                Abort("Mission failed to start", false);
                return OpResult.Fail("Mission failed to start (see log).");
            }
            if (rt.Finished && !rt.Success)
            {
                string why = rt.FailReason;
                Abort(why, false);
                return OpResult.Fail(why);
            }
            if (def.Template == MissionTemplate.FinalCampaign)
            {
                _savedMaxWanted = Game.MaxWantedLevel;
                Game.Player.WantedLevel = 0;
                Game.MaxWantedLevel = 0;
                // Rally the troops.
                foreach (var m in _mod.Faction.ActiveMembers.OrderByDescending(x => x.Skill).Take(_mod.Config.Spawning.MaxDeployedMembers))
                    if (!_mod.Members.IsDeployed(m)) _mod.Commands.Order(m, OrderType.Follow, remember: false);
            }
            _mod.Ui.ShowMissionIntro(def.Title, def.Briefing);
            Nat.Sound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
            Log.Info("Mission started: " + def.Id);
            return OpResult.Success("Mission started: " + def.Title);
        }

        public void Update()
        {
            var rt = Active;
            if (rt == null) return;
            if (!_mod.HasFaction) { Abort("No faction", false); return; }
            var player = GtaUtil.Player;
            if (player.IsDead) rt_Fail(rt, "You were wasted");
            else if (Function_IsBeingArrested()) rt_Fail(rt, "You were busted");
            if (!rt.Finished)
            {
                foreach (var m in _mod.Members.Deployed) if (m.Ped.Position.DistanceTo(player.Position) < 100f) rt.Participants.Add(m.Member.Id);
                rt.Tick(Game.GameTime);
                if (Game.IsMissionActive && !rt.Finished) rt_Fail(rt, "A story mission started");
            }
            if (rt.Finished) Finish(rt);
        }

        private static bool Function_IsBeingArrested() => GTA.Native.Function.Call<bool>(GTA.Native.Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, true);

        private static void rt_Fail(MissionRuntime rt, string reason)
        {
            if (rt.Finished) return;
            rt.Finished = true;
            rt.Success = false;
            rt.FailReason = reason;
        }

        private void Finish(MissionRuntime rt)
        {
            Active = null;
            var outcome = new MissionOutcome
            {
                Success = rt.Success,
                Kills = rt.Kills,
                MembersLost = _membersLost,
                ParticipantIds = rt.Participants.ToList(),
                FailureReason = rt.FailReason,
            };
            List<string> lines;
            try { lines = MissionRewards.Apply(_mod.Sim, rt.Offer, outcome); }
            catch (Exception ex) { Log.Error("Applying mission results failed", ex); lines = new List<string>(); }
            try { rt.Cleanup(); } catch (Exception ex) { Log.Error("Mission cleanup failed", ex); }
            RestoreWanted();
            foreach (var id in outcome.ParticipantIds)
            {
                var m = _mod.Faction.GetMember(id);
                if (m != null) Core.AI.MemoryStore.AddEntry(_mod.State, m.Id, m.FullName, MemoryKind.Mission,
                    (rt.Success ? "Took part in a successful operation: " : "Was part of a failed operation: ") + rt.Def.Title + ".",
                    rt.Success ? 0.6f : 0.5f, _mod.State.World.Day, _mod.Config.AI.MaxMemoryEntries);
            }
            if (outcome.Kills > 0) lines.Insert(0, outcome.Kills + " hostile(s) neutralized");
            if (_membersLost > 0) lines.Add(_membersLost + " member(s) down");
            _mod.FlushNotifications();
            _mod.Ui.ShowMissionResult(rt.Def.Title, rt.Success, rt.FailReason, lines);
            Nat.Sound(rt.Success ? "Mission_Pass_Notify" : "ScreenFlash", rt.Success ? "DLC_HEISTS_GENERAL_FRONTEND_SOUNDS" : "MissionFailedSounds");
            Log.Info("Mission " + rt.Def.Id + " finished: " + (rt.Success ? "success" : "failed (" + rt.FailReason + ")"));
            if (rt.Success && rt.Def.Template == MissionTemplate.FinalCampaign) _mod.Ui.OpenGovernmentFormation();
        }

        public void Abort(string reason, bool countAsFailure)
        {
            var rt = Active;
            if (rt == null) return;
            if (countAsFailure)
            {
                rt_Fail(rt, reason);
                Finish(rt);
                return;
            }
            Active = null;
            try { rt.Cleanup(); } catch (Exception ex) { Log.Error("Mission cleanup failed", ex); }
            RestoreWanted();
            Log.Info("Mission aborted: " + reason);
        }

        private void RestoreWanted()
        {
            if (_savedMaxWanted >= 0)
            {
                Game.MaxWantedLevel = _savedMaxWanted;
                _savedMaxWanted = -1;
            }
        }

        public void OnMemberLost(Member m)
        {
            if (IsActive) _membersLost++;
        }

        public void OnRecruited()
        {
            Active?.OnRecruited();
        }
    }
}
