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
    /// Territory visualization (map/minimap areas colored by owner), current-zone tracking using GTA's own zone codes,
    /// self-correction of approximate territory centers, and presence-based influence.
    /// </summary>
    internal sealed class TerritoryController
    {
        private readonly ModContext _mod;
        private readonly Dictionary<string, Blip> _areaBlips = new Dictionary<string, Blip>();
        private readonly Dictionary<string, Blip> _iconBlips = new Dictionary<string, Blip>();
        private readonly Dictionary<string, string> _blipOwner = new Dictionary<string, string>();
        private int _nextZoneCheckAt, _nextPresenceAt, _nextBlipSyncAt;
        private bool _verified;

        public Territory Current { get; private set; }
        public string CurrentZoneCode { get; private set; }
        public string BannerText { get; private set; }
        public string BannerSub { get; private set; }
        public int BannerUntil { get; private set; }

        public TerritoryController(ModContext mod) { _mod = mod; }

        public void Update()
        {
            int now = Game.GameTime;
            var world = _mod.State.World;
            if (!_verified)
            {
                _verified = true;
                foreach (var t in world.Territories.Where(t => !t.CenterVerified))
                {
                    string code = GtaUtil.ZoneCode(t.Center.ToVector3());
                    if (t.Zones.Any(z => string.Equals(z, code, StringComparison.OrdinalIgnoreCase))) t.CenterVerified = true;
                    else Log.Debug("Territory " + t.Id + " center is in zone " + code + "; will self-correct when visited.");
                }
            }

            if (now >= _nextZoneCheckAt)
            {
                _nextZoneCheckAt = now + 1000;
                var player = GtaUtil.Player;
                string code = GtaUtil.ZoneCode(player.Position);
                if (code != CurrentZoneCode)
                {
                    CurrentZoneCode = code;
                    var t = world.FindByZone(code);
                    if (t != Current)
                    {
                        Current = t;
                        if (t != null)
                        {
                            if (!t.CenterVerified && !player.IsInAir)
                            {
                                t.Center = player.Position.ToVec3();
                                t.CenterVerified = true;
                                _blipOwner.Remove(t.Id); // force re-create at corrected position
                            }
                            if (_mod.HasFaction) ShowBanner(t);
                        }
                    }
                }
            }

            if (_mod.HasFaction && Current != null && now >= _nextPresenceAt)
            {
                _nextPresenceAt = now + 5000;
                var player = GtaUtil.Player;
                int present = _mod.Members.Deployed.Count(rt => rt.Ped.Position.DistanceTo(player.Position) < 50f);
                if (present > 0)
                {
                    float hours = 5f / Math.Max(10, _mod.Config.Gameplay.StrategicTickSeconds);
                    TerritorySystem.OnPresence(_mod.Sim, Current, present, hours);
                    if (TerritorySystem.UpdateOwnership(Current, out string prev)) TerritorySystem.OnOwnershipChanged(_mod.Sim, Current, prev);
                    _mod.FlushNotifications();
                }
            }

            if (now >= _nextBlipSyncAt)
            {
                _nextBlipSyncAt = now + 3000;
                SyncBlips();
            }
        }

        private void ShowBanner(Territory t)
        {
            var w = _mod.State.World;
            string owner = w.FactionName(t.OwnerId, _mod.Faction);
            string status;
            if (t.OwnerId == FactionIds.Player) status = "Your territory - control " + (int)t.InfluenceOf(FactionIds.Player) + "%";
            else
            {
                var r = w.GetRival(t.OwnerId);
                status = (t.OwnerId == null ? "Unclaimed" : owner + " territory") + (r != null && r.Relation != RelationStatus.Neutral ? " (" + r.Relation + ")" : "");
            }
            if (t.Contested) status += " - CONTESTED";
            BannerText = t.Name.ToUpperInvariant();
            BannerSub = status;
            BannerUntil = Game.GameTime + 4500;
        }

        public void SyncBlips()
        {
            var world = _mod.State.World;
            bool show = _mod.Config.World.ShowTerritoryOverlay && _mod.HasFaction;
            if (!show)
            {
                ClearBlips();
                return;
            }
            foreach (var t in world.Territories)
            {
                string ownerKey = (t.OwnerId ?? "none") + (t.Contested ? "*" : "");
                if (_blipOwner.TryGetValue(t.Id, out string prev) && prev == ownerKey && _areaBlips.ContainsKey(t.Id)) continue;
                RemoveBlips(t.Id);
                Rgb color = OwnerColor(t.OwnerId);
                var area = GTA.World.CreateBlip(t.Center.ToVector3(), t.Radius);
                area.Color = GtaUtil.NearestBlipColor(color);
                area.Alpha = t.OwnerId == null ? 40 : 90;
                area.IsShortRange = true;
                if (t.Contested) area.IsFlashing = true;
                _areaBlips[t.Id] = area;

                var icon = GTA.World.CreateBlip(t.Center.ToVector3());
                icon.Sprite = t.Type == TerritoryType.Strategic ? BlipSprite.CaptureFlag : BlipSprite.TurfWar;
                icon.Color = area.Color;
                icon.Scale = 0.6f;
                icon.IsShortRange = true;
                icon.Name = "Territory: " + t.Name + " (" + world.FactionName(t.OwnerId, _mod.Faction) + ")";
                _iconBlips[t.Id] = icon;
                _blipOwner[t.Id] = ownerKey;
            }
        }

        public Rgb OwnerColor(string ownerId)
        {
            var f = _mod.Faction;
            if (ownerId == FactionIds.Player) return f.IsGovernment ? f.Government.StateIdentity.Primary : f.Identity.Primary;
            if (ownerId == FactionIds.State) return new Rgb(60, 90, 200);
            var r = _mod.State.World.GetRival(ownerId);
            return r != null ? Rgb.Parse(r.Color, new Rgb(150, 150, 150)) : new Rgb(200, 200, 200);
        }

        private void RemoveBlips(string id)
        {
            if (_areaBlips.TryGetValue(id, out var a)) { try { a.Delete(); } catch { } _areaBlips.Remove(id); }
            if (_iconBlips.TryGetValue(id, out var i)) { try { i.Delete(); } catch { } _iconBlips.Remove(id); }
        }

        public void ClearBlips()
        {
            foreach (var id in _areaBlips.Keys.ToList()) RemoveBlips(id);
            foreach (var id in _iconBlips.Keys.ToList()) RemoveBlips(id);
            _blipOwner.Clear();
        }

        public void ForceRefresh()
        {
            _blipOwner.Clear();
            _nextBlipSyncAt = 0;
        }

        public void SetRoute(Territory t)
        {
            if (t == null) return;
            GTA.World.WaypointPosition = t.Center.ToVector3();
        }
    }
}
