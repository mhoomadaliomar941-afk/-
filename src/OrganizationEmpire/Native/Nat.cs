using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Native
{
    /// <summary>Thin, typed wrappers over the natives this mod uses. All calls must happen on the script thread.</summary>
    internal static class Nat
    {
        // ------------------------------------------------------------------ 2D drawing (normalized 0..1 coordinates)

        public static void Rect(float cx, float cy, float w, float h, Color c)
        {
            Function.Call(Hash.DRAW_RECT, cx, cy, w, h, (int)c.R, (int)c.G, (int)c.B, (int)c.A, false);
        }

        private static void AddText(string text)
        {
            if (text.Length <= 99)
            {
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                return;
            }
            for (int i = 0; i < text.Length; i += 99)
            {
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text.Substring(i, Math.Min(99, text.Length - i)));
            }
        }

        private static string Label(string text) => text.Length <= 99 ? "STRING" : "CELL_EMAIL_BCON";

        /// <summary>Draws text. justify: 0 center, 1 left, 2 right (needs wrap bounds).</summary>
        public static void Text(string text, float x, float y, float scale, Color c, int font, int justify, float wrapStart, float wrapEnd, bool shadow, bool outline)
        {
            if (string.IsNullOrEmpty(text)) return;
            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, justify);
            if (justify == 2 || wrapEnd > 0) Function.Call(Hash.SET_TEXT_WRAP, wrapStart, wrapEnd <= 0 ? 1f : wrapEnd);
            if (shadow) Function.Call(Hash.SET_TEXT_DROP_SHADOW);
            if (outline) Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, Label(text));
            AddText(text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
        }

        private static readonly Dictionary<string, float> WidthCache = new Dictionary<string, float>();

        /// <summary>Width of a single line of text in normalized screen units (cached).</summary>
        public static float TextWidth(string text, float scale, int font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            string key = font + "|" + scale.ToString("0.000") + "|" + text;
            if (WidthCache.TryGetValue(key, out float w)) return w;
            if (WidthCache.Count > 4000) WidthCache.Clear();
            Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, Label(text));
            AddText(text);
            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            w = Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, true);
            WidthCache[key] = w;
            return w;
        }

        private static readonly Dictionary<long, float> HeightCache = new Dictionary<long, float>();

        public static float LineHeight(float scale, int font)
        {
            long key = ((long)font << 32) | (uint)(scale * 10000);
            if (HeightCache.TryGetValue(key, out float h)) return h;
            h = Function.Call<float>(Hash.GET_RENDERED_CHARACTER_HEIGHT, scale, font);
            if (h <= 0.0001f || float.IsNaN(h)) h = scale * 0.045f;
            HeightCache[key] = h;
            return h;
        }

        public static void ClearTextCaches()
        {
            WidthCache.Clear();
            HeightCache.Clear();
        }

        private static readonly HashSet<string> RequestedDicts = new HashSet<string>();

        public static bool TextureDictReady(string dict)
        {
            if (!RequestedDicts.Contains(dict))
            {
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, false);
                RequestedDicts.Add(dict);
            }
            return Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict);
        }

        private static readonly Dictionary<string, bool> TextureExists = new Dictionary<string, bool>();

        /// <summary>True if the texture exists (GET_TEXTURE_RESOLUTION returns 4x4 for missing textures).</summary>
        public static bool HasTexture(string dict, string name)
        {
            string key = dict + "/" + name;
            if (TextureExists.TryGetValue(key, out bool ok)) return ok;
            if (!TextureDictReady(dict)) return false;
            Vector3 res = Function.Call<Vector3>(Hash.GET_TEXTURE_RESOLUTION, dict, name);
            ok = res.X > 4.5f && res.Y > 4.5f;
            TextureExists[key] = ok;
            return ok;
        }

        public static void Sprite(string dict, string name, float cx, float cy, float w, float h, float heading, Color c)
        {
            if (!TextureDictReady(dict)) return;
            Function.Call(Hash.DRAW_SPRITE, dict, name, cx, cy, w, h, heading, (int)c.R, (int)c.G, (int)c.B, (int)c.A, false, 0);
        }

        // ------------------------------------------------------------------ input

        public static void DisableControl(Control c) => Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)c, true);

        public static bool DisabledJustPressed(Control c) => Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c);

        public static bool DisabledPressed(Control c) => Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)c);

        public static float DisabledNormal(Control c) => Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)c);

        public static void ShowCursor() => Function.Call(Hash.SET_MOUSE_CURSOR_THIS_FRAME);

        public static bool UsingKeyboard() => Function.Call<bool>(Hash.IS_USING_KEYBOARD_AND_MOUSE, 0);

        // ------------------------------------------------------------------ native on-screen keyboard (non-blocking)

        public static void OpenOnscreenKeyboard(string defaultText, int maxLength)
        {
            Function.Call(Hash.DISPLAY_ONSCREEN_KEYBOARD, 1, "FMMC_KEY_TIP8", "", defaultText ?? "", "", "", "", maxLength);
        }

        /// <summary>0 = editing, 1 = finished, 2 = cancelled, 3 = not active.</summary>
        public static int OnscreenKeyboardStatus() => Function.Call<int>(Hash.UPDATE_ONSCREEN_KEYBOARD);

        public static string OnscreenKeyboardResult() => Function.Call<string>(Hash.GET_ONSCREEN_KEYBOARD_RESULT);

        // ------------------------------------------------------------------ sounds

        public static void Sound(string name, string set = "HUD_FRONTEND_DEFAULT_SOUNDSET")
        {
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name, set, true);
        }

        // ------------------------------------------------------------------ peds

        public static bool IsMissionEntity(Entity e) => e != null && Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, e.Handle);

        public static void SetMissionEntity(Entity e) => Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, e.Handle, true, true);

        public static int PedType(Ped p) => Function.Call<int>(Hash.GET_PED_TYPE, p.Handle);

        public static bool IsLawEnforcementType(int pedType) => pedType == 6 || pedType == 27 || pedType == 29;

        public static int RelationshipGroupHash(Ped p) => Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, p.Handle);

        public static PedAppearance CaptureAppearance(Ped p)
        {
            var a = new PedAppearance { Captured = true };
            for (int c = 0; c < 12; c++)
            {
                a.Drawables[c] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, p.Handle, c);
                a.Textures[c] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, p.Handle, c);
                a.Palettes[c] = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, p.Handle, c);
            }
            int[] propIds = { 0, 1, 2, 6, 7 };
            for (int i = 0; i < propIds.Length; i++)
            {
                a.PropIndices[i] = Function.Call<int>(Hash.GET_PED_PROP_INDEX, p.Handle, propIds[i]);
                a.PropTextures[i] = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, p.Handle, propIds[i]);
            }
            return a;
        }

        public static void ApplyAppearance(Ped p, PedAppearance a)
        {
            if (a == null || !a.Captured) return;
            for (int c = 0; c < 12 && c < a.Drawables.Length; c++)
            {
                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, p.Handle, c, a.Drawables[c], a.Textures[c], a.Palettes[c]);
            }
            int[] propIds = { 0, 1, 2, 6, 7 };
            for (int i = 0; i < propIds.Length && i < a.PropIndices.Length; i++)
            {
                if (a.PropIndices[i] >= 0) Function.Call(Hash.SET_PED_PROP_INDEX, p.Handle, propIds[i], a.PropIndices[i], a.PropTextures[i], true);
                else Function.Call(Hash.CLEAR_PED_PROP, p.Handle, propIds[i]);
            }
        }

        /// <summary>Combat and behavior setup for faction members.</summary>
        public static void ConfigureMember(Ped p, float skill, int armor)
        {
            int ability = skill >= 70 ? 2 : skill >= 35 ? 1 : 0;
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, p.Handle, ability);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, p.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, p.Handle, 1);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p.Handle, 0, true);   // can use cover
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p.Handle, 5, true);   // can fight armed peds when unarmed
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p.Handle, 46, true);  // always fight
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, p.Handle, 0, false);
            Function.Call(Hash.SET_PED_CAN_BE_TARGETTED_BY_PLAYER, p.Handle, Game.Player.Handle, false);
            Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, p.Handle, false);
            Function.Call(Hash.SET_PED_KEEP_TASK, p.Handle, true);
            Function.Call(Hash.SET_PED_PATH_PREFER_TO_AVOID_WATER, p.Handle, true);
            Function.Call(Hash.SET_PED_PATH_AVOID_FIRE, p.Handle, true);
            Function.Call(Hash.SET_PED_SEEING_RANGE, p.Handle, 70f);
            Function.Call(Hash.SET_PED_HEARING_RANGE, p.Handle, 70f);
            p.Accuracy = (int)MathEx.Clamp(20 + skill * 0.55f, 15, 75);
            p.MaxHealth = 200 + (int)skill;
            p.Health = p.MaxHealth;
            p.Armor = armor;
            p.CanWrithe = false;
            p.BlockPermanentEvents = false;
        }

        public static void ConfigureHostile(Ped p, float accuracy, int armor, bool aggressive)
        {
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, p.Handle, 1);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, p.Handle, aggressive ? 2 : 1);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p.Handle, 0, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p.Handle, 46, true);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, p.Handle, 0, false);
            Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, p.Handle, false);
            Function.Call(Hash.SET_PED_KEEP_TASK, p.Handle, true);
            Function.Call(Hash.SET_PED_SEEING_RANGE, p.Handle, 80f);
            p.Accuracy = (int)accuracy;
            p.Armor = armor;
            p.CanWrithe = false;
        }

        public static void GiveWeapon(Ped p, string weaponName, int ammo, bool equip)
        {
            try { p.Weapons.Give(weaponName, ammo, equip, true); }
            catch (Exception ex) { Log.Debug("GiveWeapon " + weaponName + " failed: " + ex.Message); }
        }

        public static void FollowOffset(Ped p, Entity target, Vector3 offset, float speed)
        {
            Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, p.Handle, target.Handle, offset.X, offset.Y, offset.Z, speed, -1, 2.5f, true);
        }

        public static void NavTo(Ped p, Vector3 pos, float speed)
        {
            Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p.Handle, pos.X, pos.Y, pos.Z, speed, -1, 1.5f, 0, 0f);
        }

        public static void GoToAnyMeans(Ped p, Vector3 pos, float speed)
        {
            Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS, p.Handle, pos.X, pos.Y, pos.Z, speed, 0, false, 786603, 0f);
        }

        public static void GuardSphere(Ped p, Vector3 pos, float radius)
        {
            Function.Call(Hash.SET_PED_SPHERE_DEFENSIVE_AREA, p.Handle, pos.X, pos.Y, pos.Z, radius, false, false);
        }

        public static void CombatAround(Ped p, float radius) => Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, p.Handle, radius, 0);

        public static void WanderInArea(Ped p, Vector3 pos, float radius) => Function.Call(Hash.TASK_WANDER_IN_AREA, p.Handle, pos.X, pos.Y, pos.Z, radius, 2f, 5f);

        public static void Scenario(Ped p, string scenario) => Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p.Handle, scenario, 0, true);

        public static void DriveTo(Ped driver, Vehicle v, Vector3 pos, float speed, float stopRange)
        {
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, driver.Handle, v.Handle, pos.X, pos.Y, pos.Z, speed, 786603, stopRange);
        }

        public static bool IsPedInCombat(Ped p) => Function.Call<bool>(Hash.IS_PED_IN_COMBAT, p.Handle, 0);

        public static int ScriptTaskStatus(Ped p, uint taskHash) => Function.Call<int>(Hash.GET_SCRIPT_TASK_STATUS, p.Handle, taskHash);

        public static void SetModelSuppressed(int modelHash, bool suppressed) => Function.Call(Hash.SET_PED_MODEL_IS_SUPPRESSED, modelHash, suppressed);

        public static void Speech(Ped p, string line)
        {
            try { p.PlayAmbientSpeech(line, SpeechModifier.Standard); } catch { /* voice may lack the line */ }
        }

        // ------------------------------------------------------------------ world helpers

        public static Vector3 SafePedSpot(Vector3 near, bool sidewalk = true)
        {
            Vector3 p = GTA.World.GetSafeCoordForPed(near, sidewalk, 16);
            if (p == Vector3.Zero) p = GTA.World.GetSafeCoordForPed(near, false, 0);
            if (p == Vector3.Zero)
            {
                float z = GTA.World.GetGroundHeight(near);
                p = new Vector3(near.X, near.Y, z > 0 ? z + 0.5f : near.Z);
            }
            return p;
        }

        public static bool StreetNodeWithHeading(Vector3 near, out Vector3 pos, out float heading)
        {
            var outPos = new OutputArgument();
            var outHeading = new OutputArgument();
            bool ok = Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, near.X, near.Y, near.Z, outPos, outHeading, 1, 3f, 0);
            pos = ok ? outPos.GetResult<Vector3>() : near;
            heading = ok ? outHeading.GetResult<float>() : 0f;
            return ok && pos != Vector3.Zero;
        }

        public static Vector3 ToVector3(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToVec3(this Vector3 v) => new Vec3(v.X, v.Y, v.Z);
        public static Color ToColor(this Rgb c) => Color.FromArgb(c.A, c.R, c.G, c.B);
        public static Color WithAlpha(this Color c, int a) => Color.FromArgb(MathEx.Clamp(a, 0, 255), c.R, c.G, c.B);

        public static Model ParseModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return new Model(0);
            model = model.Trim();
            if (model.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(model.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint h))
                return new Model(unchecked((int)h));
            return new Model(model);
        }

        public static string ModelToString(Model m) => "0x" + unchecked((uint)m.Hash).ToString("X8");

        /// <summary>Loads a model without blocking the game (yields the script fiber up to the timeout).</summary>
        public static bool LoadModel(Model m, int timeoutMs = 1500)
        {
            if (!m.IsValid || !m.IsInCdImage) return false;
            if (m.IsLoaded) return true;
            return m.Request(timeoutMs);
        }

        public static int GameTimeMs => Game.GameTime;
    }
}
