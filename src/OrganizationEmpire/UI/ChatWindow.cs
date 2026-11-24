using System;
using System.Drawing;
using Font = GTA.UI.Font;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GTA;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.World;

namespace OrganizationEmpire.UI
{
    /// <summary>
    /// Conversation panel. The player types freely; replies arrive asynchronously. If the player's words contain an
    /// order, a suggestion chip appears - the order only runs if the player confirms it, and it is executed by the
    /// validated command system.
    /// </summary>
    internal sealed class ChatWindow
    {
        private readonly ModContext _mod;
        private readonly UiManager _ui;
        private readonly StringBuilder _input = new StringBuilder();
        private float _scroll;
        private bool _nativeKeyboardOpen;

        public Conversation Conversation { get; private set; }
        public Member Member { get; private set; }
        public NpcProfile Profile { get; private set; }
        public Ped Ped { get; private set; }

        public ChatWindow(ModContext mod, UiManager ui) { _mod = mod; _ui = ui; }

        public bool IsOpen => Conversation != null;

        public void OpenForMember(Member m, Ped ped)
        {
            bool radio = !GtaUtil.Alive(ped) || ped.Position.DistanceTo(GtaUtil.Player.Position) > 15f;
            if (radio && !_mod.Config.AI.AllowRadioChat) { _ui.Notify.Push(m.ShortName + " is not nearby (radio chat is disabled in settings).", EventSeverity.Warning); return; }
            Member = m;
            Profile = null;
            Ped = ped;
            Conversation = _mod.Dialogue.Open(_mod.State, m.Id, m.FullName, true, radio);
            Begin();
        }

        public void OpenForCivilian(Ped ped)
        {
            if (!_mod.Config.AI.AllowCivilianChat) { _ui.Notify.Push("Conversations with non-members are disabled in settings.", EventSeverity.Warning); return; }
            var p = _mod.Interaction.ProfileFor(ped);
            Member = null;
            Profile = p;
            Ped = ped;
            Conversation = _mod.Dialogue.Open(_mod.State, "npc:" + p.Key, p.FullName, false, false);
            Begin();
        }

        private void Begin()
        {
            _input.Clear();
            _scroll = 0;
            if (GtaUtil.Alive(Ped) && !Ped.IsInVehicle() && !Nat.IsPedInCombat(Ped))
            {
                if (Member == null) Ped.Task.ClearAll();
                Ped.Task.TurnTo(GtaUtil.Player, 1500);
            }
            if (_mod.Config.General.TextInput == Core.Config.TextInputMode.NativeKeyboard) OpenNativeKeyboard();
            Nat.Sound("SELECT");
        }

        public void Close()
        {
            if (Conversation != null) _mod.Dialogue.Cancel(Conversation);
            Conversation = null;
            Member = null;
            Profile = null;
            Ped = null;
            _input.Clear();
            _nativeKeyboardOpen = false;
        }

        private NpcContext BuildContext()
        {
            NpcContext c = Member != null
                ? ContextBuilder.ForMember(_mod.Sim, Member, _mod.Config.AI)
                : ContextBuilder.ForCivilian(_mod.Sim, Profile, _mod.Config.AI);
            c.PlayerName = GtaUtil.ProtagonistName();
            c.Location = GtaUtil.ZoneName(GtaUtil.Player.Position);
            if (_mod.Missions.IsActive) c.MissionContext = _mod.Missions.Active.ContextForDialogue();
            if (GtaUtil.Alive(Ped))
            {
                if (Nat.IsPedInCombat(Ped)) c.Situation = "in the middle of a firefight";
                else if (Ped.IsInVehicle()) c.Situation = "riding in a vehicle";
            }
            if (Game.Player.WantedLevel > 0) c.Situation = (c.Situation == null ? "" : c.Situation + ", ") + "the police are after the boss";
            return c;
        }

        private void Send()
        {
            string text = _input.ToString().Trim();
            if (text.Length == 0) return;
            if (!_mod.Dialogue.Send(Conversation, text, BuildContext(), _mod.State, out string err))
            {
                _ui.Notify.Push(err, EventSeverity.Warning, 2500);
                return;
            }
            _input.Clear();
            _scroll = 0;
            if (GtaUtil.Alive(Ped) && !Ped.IsInVehicle() && !Nat.IsPedInCombat(Ped)) Ped.Task.TurnTo(GtaUtil.Player, 1500);
        }

        private void OpenNativeKeyboard()
        {
            Nat.OpenOnscreenKeyboard("", 200);
            _nativeKeyboardOpen = true;
        }

        public void ConfirmSuggestion()
        {
            var c = Conversation;
            if (c?.SuggestedOrder == null || Member == null) return;
            var order = c.SuggestedOrder.Value;
            c.SuggestedOrder = null;
            var r = order == OrderType.Attack
                ? _mod.Commands.Order(Member, order, default(GTA.Math.Vector3), _mod.Commands.CurrentTarget())
                : _mod.Commands.Order(Member, order);
            _ui.Notify.Push(r.Message, r.Ok ? EventSeverity.Good : EventSeverity.Warning, 3500);
        }

        public void Update()
        {
            if (!IsOpen) return;
            if (_nativeKeyboardOpen)
            {
                int status = Nat.OnscreenKeyboardStatus();
                if (status == 1)
                {
                    _nativeKeyboardOpen = false;
                    _input.Clear();
                    _input.Append(Nat.OnscreenKeyboardResult() ?? "");
                    Send();
                }
                else if (status == 2 || status == 3) { _nativeKeyboardOpen = false; }
            }
        }

        public void HandleKey(Keys key, bool shift, bool ctrl, char? ch)
        {
            switch (key)
            {
                case Keys.Escape: Close(); Nat.Sound("BACK"); return;
                case Keys.Enter:
                    if (ctrl) ConfirmSuggestion();
                    else if (_input.Length == 0 && _mod.Config.General.TextInput == Core.Config.TextInputMode.NativeKeyboard) OpenNativeKeyboard();
                    else Send();
                    return;
                case Keys.Back: if (_input.Length > 0) _input.Length--; return;
                case Keys.PageUp: _scroll += 120; return;
                case Keys.PageDown: _scroll = Math.Max(0, _scroll - 120); return;
            }
            if (ctrl && key == Keys.V)
            {
                try { foreach (char c in Clipboard.GetText()) if (!char.IsControl(c) && _input.Length < 300) _input.Append(c); } catch { /* ignore */ }
                return;
            }
            if (ch.HasValue && _input.Length < 300 && ch.Value != '~') _input.Append(ch.Value);
        }

        public void Draw(Renderer r, UiInput input)
        {
            var c = Conversation;
            if (c == null) return;
            var t = r.Theme;
            float w = 600f, h = 660f;
            float x = r.W - w - 40f, y = (r.H - h) / 2f + 20f;
            r.Rect(x, y, w, h, t.Panel);
            r.Rect(x, y, w, 4f, t.Accent);
            r.Corners(x - 4, y - 4, w + 8, h + 8, 16f, 2f, t.Accent);

            // Header
            string title = c.NpcName;
            string sub;
            if (Member != null)
            {
                sub = (_mod.Faction.GetRank(Member.RankId)?.Name ?? "") + " - " + TextUtil.Humanize(Member.Role.ToString()) + " - " + Member.Personality + (c.ViaRadio ? "  (radio)" : "");
            }
            else sub = (Profile?.Personality.ToString() ?? "") + " - not a member";
            r.Text(title.ToUpperInvariant(), x + 20, y + 14, Theme.HeadSize, t.Text, Theme.Condensed);
            r.Text(r.Ellipsize(sub, Theme.SmallSize, Theme.Body, w - 40), x + 20, y + 48, Theme.SmallSize, t.TextDim);
            if (Member != null)
            {
                r.Text("LOYALTY", x + 20, y + 76, Theme.TinySize, t.TextFaint);
                r.Bar(x + 100, y + 84, 170, 6, Member.Loyalty / 100f, t.Accent);
                r.Text("MORALE", x + 300, y + 76, Theme.TinySize, t.TextFaint);
                r.Bar(x + 380, y + 84, 170, 6, Member.Morale / 100f, t.Good);
            }
            string provider = (_mod.Config.AI.Enabled ? _mod.Config.AI.Provider.ToString() : "Offline") + (c.LastProvider != null ? "" : "");
            r.Text(provider, x + w - 20, y + 18, Theme.TinySize, t.TextFaint, Theme.Body, Align.Right);
            r.Rect(x + 16, y + 104, w - 32, 1f, t.Line);

            // Messages (bottom-up)
            float areaTop = y + 112, areaBottom = y + h - 150;
            if (input.Wheel != 0 && r.Hover(x, y, w, h)) _scroll = Math.Max(0, _scroll + input.Wheel * 60);
            float cursor = areaBottom + _scroll;
            float bubbleW = w - 120;
            if (c.State == ConversationState.Waiting)
            {
                string dots = new string('.', 1 + (Environment.TickCount / 350) % 3);
                cursor -= 34;
                if (cursor > areaTop && cursor < areaBottom) r.Text(c.NpcName.Split(' ')[0] + " is typing" + dots, x + 24, cursor + 6, Theme.SmallSize, t.TextDim);
            }
            for (int i = c.Visible.Count - 1; i >= 0 && cursor > areaTop - 400; i--)
            {
                var turn = c.Visible[i];
                bool player = turn.Speaker == "player";
                bool system = turn.Speaker == "system";
                var lines = r.Wrap(turn.Text, Theme.SmallSize + 0.01f, Theme.Body, bubbleW - 28);
                float lh = r.LineHeight(Theme.SmallSize + 0.01f);
                float bh = lines.Count * lh + 20;
                cursor -= bh + 10;
                if (cursor + bh < areaTop || cursor > areaBottom) continue;
                float bx = player ? x + w - 24 - bubbleW : x + 24;
                Color bg = system ? Color.FromArgb(60, t.Warn.R, t.Warn.G, t.Warn.B) : player ? Color.FromArgb(200, t.Accent.R / 3, t.Accent.G / 3, t.Accent.B / 3) : Color.FromArgb(255, 30, 37, 48);
                if (cursor >= areaTop) r.Rect(bx, cursor, bubbleW, bh, bg);
                if (player && cursor >= areaTop) r.Rect(bx + bubbleW - 3, cursor, 3, bh, t.Accent);
                for (int li = 0; li < lines.Count; li++)
                {
                    float ly = cursor + 10 + li * lh;
                    if (ly < areaTop || ly > areaBottom - lh * 0.5f) continue;
                    r.Text(lines[li], bx + 14, ly, Theme.SmallSize + 0.01f, system ? t.Warn : t.Text);
                }
            }

            // Suggestion chip
            float sy = y + h - 140;
            if (c.SuggestedOrder != null && Member != null)
            {
                string label = "Order detected: " + CommandSystem.Describe(c.SuggestedOrder.Value) + "  -  [Ctrl+Enter] or click to execute";
                bool hover = r.Hover(x + 20, sy, w - 40, 34);
                r.Rect(x + 20, sy, w - 40, 34, hover ? t.Accent : t.AccentDim);
                r.Text(r.Ellipsize(label, Theme.SmallSize, Theme.Body, w - 70), x + 34, r.TextTop(sy, 34, Theme.SmallSize), Theme.SmallSize, hover ? t.AccentText : t.Text);
                if (hover && input.Click) ConfirmSuggestion();
            }

            // Input
            float iy = y + h - 92;
            r.Rect(x + 20, iy, w - 40, 46, Color.FromArgb(255, 6, 8, 12));
            r.Border(x + 20, iy, w - 40, 46, 1.5f, c.State == ConversationState.Waiting ? t.TextFaint : t.Accent);
            string text = _input.ToString();
            bool caret = (Environment.TickCount / 500) % 2 == 0;
            string display = text;
            while (display.Length > 0 && r.TextWidth(display + "_", Theme.SmallSize + 0.01f) > w - 80) display = display.Substring(1);
            if (text.Length == 0 && !caret) r.Text(_mod.Config.General.TextInput == Core.Config.TextInputMode.NativeKeyboard ? "Press Enter to type..." : "Say something...", x + 34, r.TextTop(iy, 46, Theme.SmallSize), Theme.SmallSize, t.TextFaint);
            else r.Text(display + (caret ? "_" : ""), x + 34, r.TextTop(iy, 46, Theme.SmallSize + 0.01f), Theme.SmallSize + 0.01f, t.Text);
            r.Text("Enter: send   Esc: close   PgUp/PgDn: scroll   Ctrl+V: paste", x + 20, y + h - 36, Theme.TinySize, t.TextFaint);
        }
    }
}
