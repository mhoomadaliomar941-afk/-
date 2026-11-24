using System;
using System.Drawing;
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
    /// <summary>
    /// Headquarters presence in the world: map blip, a physical flag pole prop (an in-game flag model - GTA cannot
    /// texture props with a custom image at runtime), a ground marker and floating faction label, and guard streaming.
    /// </summary>
    internal sealed class HeadquartersController
    {
        private readonly ModContext _mod;
        private Blip _blip;
        private Prop _flag;
        private string _flagModel;
        private Vector3 _blipPos;
        private int _nextUpdateAt;

        public HeadquartersController(ModContext mod) { _mod = mod; }

        public OpResult EstablishHere(string name)
        {
            var f = _mod.Faction;
            if (f == null) return OpResult.Fail("No faction.");
            var player = GtaUtil.Player;
            if (player.IsInVehicle()) return OpResult.Fail("Get out of the vehicle to set up the headquarters.");
            if (Game.IsMissionActive && !_mod.Missions.IsActive) return OpResult.Fail("Not during a story mission.");
            Vector3 pos = player.Position;
            string zone = GtaUtil.ZoneCode(pos);
            bool moving = f.Headquarters != null;
            var old = f.Headquarters;
            f.Headquarters = new HeadquartersState
            {
                Name = string.IsNullOrWhiteSpace(name) ? (old?.Name ?? f.Name + " HQ") : name.Trim(),
                Position = pos.ToVec3(),
                Heading = player.Heading,
                ZoneCode = zone,
                EstablishedDay = _mod.State.World.Day,
                Upgrades = old?.Upgrades ?? new System.Collections.Generic.Dictionary<HqUpgrade, int>(),
                GuardBehavior = old?.GuardBehavior ?? GuardBehavior.Static,
                ShowFlag = old?.ShowFlag ?? true,
            };
            if (moving)
            {
                // Moving costs a little morale and resets guard posts.
                foreach (var m in f.ActiveMembers) m.Morale = MathEx.Pct(m.Morale - 2);
                var guards = old.GuardMemberIds.Select(f.GetMember).Where(m => m != null).ToList();
                _mod.Commands.AssignHqGuards(guards);
            }
            var t = _mod.State.World.FindByZone(zone);
            if (t != null && t.Type != TerritoryType.Strategic)
            {
                TerritorySystem.AddInfluence(t, FactionIds.Player, 10);
                if (TerritorySystem.UpdateOwnership(t, out string prev)) TerritorySystem.OnOwnershipChanged(_mod.Sim, t, prev);
            }
            _mod.Sim.Report("Headquarters", (moving ? "Headquarters moved to " : "Headquarters established in ") + GtaUtil.ZoneName(pos) + ".", EventSeverity.Good);
            _mod.FlushNotifications();
            Refresh(true);
            return OpResult.Success("Headquarters set.");
        }

        public void Refresh(bool force)
        {
            if (force)
            {
                DeleteBlip();
                DeleteFlag();
                _nextUpdateAt = 0;
            }
        }

        private void DeleteBlip()
        {
            try { _blip?.Delete(); } catch { /* ignore */ }
            _blip = null;
        }

        private void DeleteFlag()
        {
            if (_flag != null) _mod.Entities.Release(_flag, true);
            _flag = null;
            _flagModel = null;
        }

        public void Cleanup()
        {
            DeleteBlip();
            DeleteFlag();
        }

        public void Update()
        {
            var f = _mod.Faction;
            var hq = f?.Headquarters;
            if (hq == null) { if (_blip != null || _flag != null) Cleanup(); return; }
            int now = Game.GameTime;
            Vector3 pos = hq.Position.ToVector3();
            var player = GtaUtil.Player;
            float dist = player.Position.DistanceTo(pos);

            if (now >= _nextUpdateAt)
            {
                _nextUpdateAt = now + 2000;
                EnsureBlip(f, hq, pos);
                EnsureFlag(f, hq, pos, dist);
            }

            // Marker + floating label when close.
            if (dist < 45f && !_mod.Ui.IsOpen)
            {
                var color = f.IsGovernment ? f.Government.StateIdentity.Primary.ToColor() : f.Identity.Primary.ToColor();
                GTA.World.DrawMarker(MarkerType.HorizontalCircleSkinny, pos - new Vector3(0, 0, 0.95f), Vector3.Zero, Vector3.Zero,
                    new Vector3(3.5f, 3.5f, 1f), Color.FromArgb(120, color.R, color.G, color.B));
                var screen = GTA.UI.Screen.WorldToScreen(pos + new Vector3(0, 0, 2.2f));
                if (screen.X > 0 || screen.Y > 0)
                {
                    string title = f.IsGovernment ? f.Government.GovernmentName : f.Name;
                    _mod.Ui.Renderer.WorldBanner(screen.X / GTA.UI.Screen.Width, screen.Y / GTA.UI.Screen.Height, title.ToUpperInvariant(), hq.Name, color);
                }
            }
        }

        private void EnsureBlip(FactionState f, HeadquartersState hq, Vector3 pos)
        {
            if (_blip != null && _blip.Exists() && _blipPos == pos) return;
            DeleteBlip();
            _blip = GTA.World.CreateBlip(pos);
            switch (f.Type)
            {
                case FactionType.Mafia: _blip.Sprite = BlipSprite.Office; break;
                case FactionType.Militia: _blip.Sprite = BlipSprite.Bunker; break;
                default: _blip.Sprite = BlipSprite.Safehouse; break;
            }
            if (f.IsGovernment) _blip.Sprite = BlipSprite.Castle;
            _blip.Color = GtaUtil.NearestBlipColor(f.Identity.Primary);
            _blip.Name = (f.IsGovernment ? f.Government.GovernmentName : f.Name) + " - " + hq.Name;
            _blip.IsShortRange = false;
            _blip.Scale = 1.0f;
            _blipPos = pos;
        }

        private void EnsureFlag(FactionState f, HeadquartersState hq, Vector3 pos, float dist)
        {
            bool want = _mod.Config.World.ShowHeadquartersFlag && hq.ShowFlag && dist < 300f;
            var flag = f.IsGovernment ? f.Government.StateIdentity.Flag : f.Identity.Flag;
            string model = flag.WorldFlagModel;
            if (!want || string.IsNullOrEmpty(model) || model == "none")
            {
                if (_flag != null && dist > 350f || !want) DeleteFlag();
                return;
            }
            if (_flag != null && _flag.Exists() && _flagModel == model) return;
            DeleteFlag();
            var m = Nat.ParseModel(model);
            if (!m.IsInCdImage)
            {
                Log.Warn("Flag model not present in this game build: " + model);
                return;
            }
            // Place it a few meters beside the HQ point, facing the HQ heading.
            float h = hq.Heading * (float)Math.PI / 180f;
            Vector3 spot = pos + new Vector3((float)Math.Cos(h) * 3f, (float)Math.Sin(h) * 3f, 0);
            float groundZ = GTA.World.GetGroundHeight(spot);
            if (groundZ > 0) spot.Z = groundZ;
            _flag = _mod.Entities.SpawnProp(model, spot, new Vector3(0, 0, hq.Heading), SpawnCategory.Prop);
            if (_flag != null)
            {
                _flag.IsPositionFrozen = true;
                _flagModel = model;
            }
        }

        public bool PlayerNearHq(float radius)
        {
            var hq = _mod.Faction?.Headquarters;
            return hq != null && GtaUtil.Player.Position.DistanceTo(hq.Position.ToVector3()) < radius;
        }
    }
}
