using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.World
{
    internal static class GtaUtil
    {
        private static readonly KeyValuePair<BlipColor, Rgb>[] BlipPalette =
        {
            new KeyValuePair<BlipColor, Rgb>(BlipColor.White, new Rgb(240, 240, 240)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Red, new Rgb(224, 50, 50)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.RedDark, new Rgb(140, 30, 30)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Green, new Rgb(114, 204, 114)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.GreenDark, new Rgb(50, 120, 60)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Blue, new Rgb(93, 182, 229)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.BlueDark, new Rgb(44, 80, 160)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.BlueLight, new Rgb(140, 200, 240)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Yellow, new Rgb(240, 200, 80)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Orange, new Rgb(234, 142, 80)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Purple, new Rgb(156, 90, 190)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Pink, new Rgb(240, 120, 180)),
            new KeyValuePair<BlipColor, Rgb>(BlipColor.Grey, new Rgb(140, 140, 140)),
        };

        public static BlipColor NearestBlipColor(Rgb c)
        {
            BlipColor best = BlipColor.White;
            int bestD = int.MaxValue;
            foreach (var kv in BlipPalette)
            {
                int d = kv.Value.DistanceSq(c);
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            return best;
        }

        public static Ped Player => Game.Player.Character;

        public static bool PlayerReady()
        {
            var p = Game.Player.Character;
            return p != null && p.Exists() && !Game.IsLoading;
        }

        /// <summary>A point roughly behind the camera, used to spawn things out of sight.</summary>
        public static Vector3 BehindCamera(float distance, float spread, Rng rng)
        {
            Vector3 dir = GameplayCamera.Direction;
            dir.Z = 0;
            if (dir.Length() < 0.01f) dir = Player.ForwardVector;
            dir.Normalize();
            float angle = (float)(Math.Atan2(dir.Y, dir.X) + Math.PI + rng.Range(-spread, spread));
            var p = Player.Position;
            return new Vector3(p.X + (float)Math.Cos(angle) * distance, p.Y + (float)Math.Sin(angle) * distance, p.Z);
        }

        public static Vector3 GroundedWaypoint()
        {
            if (!Game.IsWaypointActive) return Vector3.Zero;
            Vector3 wp = GTA.World.WaypointPosition;
            float z = GTA.World.GetGroundHeight(new Vector2(wp.X, wp.Y));
            if (z <= 0) z = Player.Position.Z;
            return new Vector3(wp.X, wp.Y, z + 0.5f);
        }

        public static string ZoneCode(Vector3 pos)
        {
            try { return GTA.World.GetZoneDisplayName(pos); }
            catch { return ""; }
        }

        public static string ZoneName(Vector3 pos)
        {
            try { return GTA.World.GetZoneLocalizedName(pos); }
            catch { return ""; }
        }

        public static string ProtagonistName()
        {
            var model = Player.Model;
            if (model == new Model("player_zero")) return "Michael";
            if (model == new Model("player_one")) return "Franklin";
            if (model == new Model("player_two")) return "Trevor";
            return "the boss";
        }

        public static bool Valid(Entity e) => e != null && e.Exists();
        public static bool Alive(Ped p) => p != null && p.Exists() && !p.IsDead;
    }
}
