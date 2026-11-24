using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using System.Linq;
using System.Windows.Forms;
using GTA;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.UI.Pages;
using OrganizationEmpire.World;

namespace OrganizationEmpire.UI
{
    internal sealed class TabDef
    {
        public string Name;
        public Func<Page> Root;
        public TabDef(string name, Func<Page> root) { Name = name; Root = root; }
    }

    /// <summary>Owns the menu, popups, dialogs, chat and HUD overlays, and routes keyboard/mouse input.</summary>
    internal sealed class UiManager
    {
        public readonly ModContext Mod;
        public readonly Renderer Renderer = new Renderer();
        public readonly UiInput Input = new UiInput();
        public readonly Notifier Notify = new Notifier();
        public readonly FlagRenderer Flags;
        public readonly ChatWindow Chat;

        public bool IsOpen { get; private set; }
        private List<TabDef> _tabs = new List<TabDef>();
        private int _tab;
        private readonly Dictionary<int, Stack<Page>> _stacks = new Dictionary<int, Stack<Page>>();
        private readonly List<Dialog> _dialogs = new List<Dialog>();
        private Stack<Page> _popup;
        private readonly ItemList _items = new ItemList();
        private bool _hadFaction;
        private bool _pausedGame;
        private bool _slowMo;
        private string _bigTitle, _bigText;
        private int _bigUntil;
        private readonly Dictionary<string, int> _moodToday = new Dictionary<string, int>();

        public UiManager(ModContext mod)
        {
            Mod = mod;
            Flags = new FlagRenderer(mod);
            Chat = new ChatWindow(mod, this);
        }

        public bool IsCapturingInput => _dialogs.Count > 0 || Chat.IsOpen || _popup != null;
        public bool PopupOpen => _popup != null;

        // ------------------------------------------------------------------ tabs & navigation

        private void BuildTabs()
        {
            _tabs = new List<TabDef>();
            if (!Mod.HasFaction)
            {
                _tabs.Add(new TabDef("New Faction", () => new CreationWizardPage()));
                _tabs.Add(new TabDef("Save / Load", () => new SaveLoadPage()));
                _tabs.Add(new TabDef("Settings", () => new SettingsPage()));
            }
            else
            {
                _tabs.Add(new TabDef("Dashboard", () => new DashboardPage()));
                _tabs.Add(new TabDef(Mod.Faction.IsGovernment ? "State" : "Faction", () => new FactionPage()));
                _tabs.Add(new TabDef("Members", () => new MembersPage()));
                _tabs.Add(new TabDef("Recruitment", () => new RecruitmentPage()));
                _tabs.Add(new TabDef("Ranks", () => new RanksPage()));
                _tabs.Add(new TabDef(Mod.Faction.Type == FactionType.Militia ? "Units" : "Squads", () => new SquadsPage()));
                _tabs.Add(new TabDef("AI Dialogue", () => new DialoguePage()));
                _tabs.Add(new TabDef("Missions", () => new MissionsPage()));
                _tabs.Add(new TabDef("Territory", () => new TerritoryPage()));
                _tabs.Add(new TabDef("Vehicles", () => new VehiclesPage()));
                _tabs.Add(new TabDef("Headquarters", () => new HeadquartersPage()));
                _tabs.Add(new TabDef("Relations", () => new RelationsPage()));
                _tabs.Add(new TabDef("Resources", () => new ResourcesPage()));
                _tabs.Add(new TabDef("Identity", () => new IdentityPage()));
                _tabs.Add(new TabDef("Flag Designer", () => new FlagDesignerPage()));
                _tabs.Add(new TabDef("Government", () => new GovernmentPage()));
                _tabs.Add(new TabDef("Settings", () => new SettingsPage()));
                _tabs.Add(new TabDef("Save / Load", () => new SaveLoadPage()));
            }
            _stacks.Clear();
            _tab = MathEx.Clamp(_tab, 0, _tabs.Count - 1);
            _hadFaction = Mod.HasFaction;
        }

        public int TabIndex(string name) => _tabs.FindIndex(t => t.Name == name);

        private Stack<Page> Stack
        {
            get
            {
                if (!_stacks.TryGetValue(_tab, out var s))
                {
                    s = new Stack<Page>();
                    var root = _tabs[_tab].Root();
                    root.Ui = this;
                    s.Push(root);
                    root.OnShow();
                    _stacks[_tab] = s;
                }
                return s;
            }
        }

        public Page Current => Stack.Peek();

        public void Push(Page p)
        {
            p.Ui = this;
            var target = _popup ?? Stack;
            target.Push(p);
            p.OnShow();
            Nat.Sound("SELECT");
        }

        public void Back()
        {
            if (_popup != null)
            {
                if (_popup.Count > 1) _popup.Pop(); else _popup = null;
                Nat.Sound("BACK");
                return;
            }
            if (Stack.Count > 1) { Stack.Pop(); Nat.Sound("BACK"); }
            else Close();
        }

        public void SwitchTab(int index)
        {
            if (_tabs.Count == 0) return;
            _tab = (index % _tabs.Count + _tabs.Count) % _tabs.Count;
            Nat.Sound("NAV_LEFT_RIGHT");
        }

        public void OpenAt(string tabName, Page sub = null)
        {
            if (!IsOpen) Open();
            int idx = TabIndex(tabName);
            if (idx >= 0) _tab = idx;
            if (sub != null) Push(sub);
        }

        public void Open()
        {
            if (_hadFaction != Mod.HasFaction || _tabs.Count == 0) BuildTabs();
            IsOpen = true;
            _popup = null;
            var mode = Mod.Config.General.PauseMode;
            if (mode == MenuPauseMode.Pause) { Game.Pause(true); _pausedGame = true; }
            else if (mode == MenuPauseMode.SlowMotion) { Game.TimeScale = 0.25f; _slowMo = true; }
            Nat.Sound("FocusIn", "HINTCAMSOUNDS");
        }

        public void Close()
        {
            IsOpen = false;
            RestoreGameState();
            Nat.Sound("BACK");
        }

        public void RestoreGameState()
        {
            if (_pausedGame) { Game.Pause(false); _pausedGame = false; }
            if (_slowMo) { Game.TimeScale = 1f; _slowMo = false; }
        }

        public void RebuildTabs()
        {
            int keep = _tab;
            BuildTabs();
            _tab = MathEx.Clamp(keep, 0, _tabs.Count - 1);
        }

        public void ShowDialog(Dialog d) => _dialogs.Add(d);

        public void Confirm(string title, string message, Action onYes, bool danger = false, string yes = "Confirm")
            => ShowDialog(new ConfirmDialog(title, message, onYes, null, yes, "Cancel", danger));

        public void AskText(string title, string initial, int max, Action<string> onDone, string hint = null, Func<string, string> validate = null, bool mask = false)
            => ShowDialog(new TextInputDialog(title, initial, max, onDone, null, hint, validate, mask, Mod.Config.General.TextInput));

        public void ShowPicker(string title, List<PickOption> options, Action<int> onPick, Action onCancel = null)
            => ShowDialog(new PickerDialog(title, options, onPick, onCancel));

        public void OpenPopup(Page root)
        {
            root.Ui = this;
            _popup = new Stack<Page>();
            _popup.Push(root);
            root.OnShow();
            Nat.Sound("SELECT");
        }

        public void ClosePopup() { _popup = null; }

        public void CloseChat() => Chat.Close();

        public void ShowMissionIntro(string title, string text)
        {
            _bigTitle = title;
            _bigText = text;
            _bigUntil = Environment.TickCount + 9000;
        }

        public void ShowMissionResult(string title, bool success, string reason, List<string> lines)
        {
            ShowDialog(new MessageDialog(success ? "Mission complete - " + title : "Mission failed - " + title,
                success ? "Good work." : (reason ?? "The operation failed."), lines, success ? Renderer.Theme.Good : Renderer.Theme.Bad));
        }

        public void OpenGovernmentFormation()
        {
            if (!Mod.HasFaction || !TakeoverSystem.AwaitingGovernmentFormation(Mod.Faction)) return;
            OpenAt("Government");
        }

        // ------------------------------------------------------------------ AI dialogue effects

        public void OnDialogueResult(DialogueResult r)
        {
            if (r?.Reply == null) return;
            var conv = r.Conversation;
            var f = Mod.Faction;
            if (f == null) return;
            var member = f.GetMember(conv.NpcId);
            if (member != null && r.Reply.MoodDelta != 0)
            {
                string key = member.Id + "|" + Mod.State.World.Day;
                _moodToday.TryGetValue(key, out int used);
                if (used < 3)
                {
                    _moodToday[key] = used + 1;
                    member.Loyalty = MathEx.Pct(member.Loyalty + 1.5f * r.Reply.MoodDelta);
                    member.Morale = MathEx.Pct(member.Morale + 2f * r.Reply.MoodDelta);
                    MemoryStore.AddEntry(Mod.State, member.Id, member.FullName, MemoryKind.Relationship,
                        r.Reply.MoodDelta > 0 ? "A conversation with the boss lifted my spirits." : "Something the boss said rubbed me the wrong way.",
                        0.45f, Mod.State.World.Day, Mod.Config.AI.MaxMemoryEntries);
                }
            }
            if (member == null && r.Reply.Willing && Mod.Config.AI.PersuasionAffectsRecruitment && Chat.Profile != null && conv == Chat.Conversation)
            {
                float before = Chat.Profile.RapportBonus;
                Chat.Profile.RapportBonus = Math.Min(20f, Chat.Profile.RapportBonus + 10f);
                if (Chat.Profile.RapportBonus > before) Notify.Push(Chat.Profile.FirstName + " seems open to joining (+" + (int)(Chat.Profile.RapportBonus - before) + "% recruitment chance).", EventSeverity.Good, 4000);
            }
            if (conv == Chat.Conversation && GtaUtil.Alive(Chat.Ped) && !conv.ViaRadio) Nat.Speech(Chat.Ped, "CHAT_RESP");
            if (_moodToday.Count > 200) _moodToday.Clear();
        }

        // ------------------------------------------------------------------ input

        public void HandleKey(KeyEventArgs e)
        {
            Keys key = e.KeyCode;
            bool shift = e.Shift, ctrl = e.Control;
            var hk = Mod.Config.Hotkeys;

            if (_dialogs.Count > 0)
            {
                var d = _dialogs[_dialogs.Count - 1];
                char? ch = d.CapturesText ? KeyText.ToChar(e.KeyData) : null;
                d.HandleKey(key, shift, ctrl, ch);
                _dialogs.RemoveAll(x => x.Done);
                return;
            }
            if (Chat.IsOpen && !IsOpen)
            {
                Chat.HandleKey(key, shift, ctrl, KeyText.ToChar(e.KeyData));
                return;
            }
            if (Chat.IsOpen && IsOpen && Current is DialoguePage)
            {
                Chat.HandleKey(key, shift, ctrl, KeyText.ToChar(e.KeyData));
                return;
            }

            bool isMenuKey = KeyText.TryParseKey(hk.OpenMenu, out Keys menuKey) && key == menuKey;
            if (_popup != null)
            {
                if (key == Keys.Escape || key == Keys.Back) { Back(); return; }
                if (isMenuKey || (KeyText.TryParseKey(hk.QuickOrders, out Keys qk) && key == qk)) { _popup = null; return; }
                NavigateList(_popup.Peek(), key, shift, ctrl);
                return;
            }
            if (IsOpen)
            {
                if (isMenuKey) { Close(); return; }
                if (Current.HandleKey(key, shift, ctrl)) return;
                switch (key)
                {
                    case Keys.Escape: Close(); return;
                    case Keys.Back: Back(); return;
                    case Keys.Tab: SwitchTab(_tab + (shift ? -1 : 1)); return;
                    case Keys.PageDown: SwitchTab(_tab + 1); return;
                    case Keys.PageUp: SwitchTab(_tab - 1); return;
                }
                NavigateList(Current, key, shift, ctrl);
                return;
            }

            // Menu closed: global hotkeys
            if (Game.IsCutsceneActive || !GtaUtil.Alive(GtaUtil.Player)) return;
            if (isMenuKey) { Open(); return; }
            if (KeyText.TryParseKey(hk.QuickOrders, out Keys quick) && key == quick)
            {
                if (Mod.HasFaction) OpenPopup(new QuickOrdersPage());
                return;
            }
            if (KeyText.TryParseKey(hk.Interact, out Keys interact) && key == interact && Mod.HasFaction && Mod.Interaction.Target != null)
            {
                OpenPopup(new InteractionPage(Mod.Interaction.Target));
                return;
            }
            if (KeyText.TryParseKey(hk.Talk, out Keys talk) && key == talk && Mod.HasFaction)
            {
                StartTalk();
            }
        }

        public void StartTalk()
        {
            var target = Mod.Interaction.Target;
            if (target != null)
            {
                var rt = Mod.Members.FindByPed(target);
                if (rt != null) Chat.OpenForMember(rt.Member, rt.Ped);
                else Chat.OpenForCivilian(target);
                return;
            }
            var player = GtaUtil.Player;
            var nearest = Mod.Members.Deployed.Where(x => GtaUtil.Alive(x.Ped)).OrderBy(x => x.Ped.Position.DistanceTo(player.Position)).FirstOrDefault();
            if (nearest != null && nearest.Ped.Position.DistanceTo(player.Position) < 12f) Chat.OpenForMember(nearest.Member, nearest.Ped);
            else OpenAt("AI Dialogue");
        }

        private int FirstSelectable(List<Item> items, int from, int dir)
        {
            if (items.Count == 0) return -1;
            int i = from;
            for (int n = 0; n < items.Count; n++)
            {
                i = (i + dir + items.Count) % items.Count;
                if (items[i].Selectable) return i;
            }
            return -1;
        }

        private void NavigateList(Page page, Keys key, bool shift, bool ctrl)
        {
            _items.Items.Clear();
            page.Build(_items);
            var items = _items.Items;
            if (page.Focus < 0 || page.Focus >= items.Count || !items[page.Focus].Selectable) page.Focus = FirstSelectable(items, -1, 1);
            var focused = page.Focus >= 0 ? items[page.Focus] : null;
            switch (key)
            {
                case Keys.Up: page.Focus = FirstSelectable(items, page.Focus, -1); Nat.Sound("NAV_UP_DOWN"); break;
                case Keys.Down: page.Focus = FirstSelectable(items, page.Focus, 1); Nat.Sound("NAV_UP_DOWN"); break;
                case Keys.Home: page.Focus = FirstSelectable(items, -1, 1); break;
                case Keys.End: page.Focus = FirstSelectable(items, items.Count, -1); break;
                case Keys.Left: if (focused != null) Adjust(focused, -1); break;
                case Keys.Right: if (focused != null) Adjust(focused, 1); break;
                case Keys.Enter:
                case Keys.Space: if (focused != null) Activate(focused); break;
            }
        }

        private void Adjust(Item item, int dir)
        {
            if (!item.Enabled) { Disabled(item); return; }
            if (item.Adjust != null) { item.Adjust(dir); Nat.Sound("NAV_LEFT_RIGHT"); }
        }

        private void Activate(Item item)
        {
            if (!item.Enabled) { Disabled(item); return; }
            if (item.Kind == ItemKind.Link && item.LinkTo != null) { Push(item.LinkTo()); return; }
            if (item.Activate != null) { item.Activate(); if (item.Kind != ItemKind.Action) Nat.Sound("NAV_LEFT_RIGHT"); }
        }

        private void Disabled(Item item)
        {
            Notify.Push(item.DisabledReason ?? "Not available.", EventSeverity.Warning, 2500);
            Nat.Sound("ERROR");
        }

        // ------------------------------------------------------------------ frame

        public void Update(bool cutscene)
        {
            var r = Renderer;
            r.BeginFrame(Mod.Config.General.UiScale);
            if (Mod.HasFaction)
            {
                var identity = Mod.Faction.IsGovernment ? Mod.Faction.Government.StateIdentity : Mod.Faction.Identity;
                r.Theme.SetAccent(identity.Primary);
            }
            if (_hadFaction != Mod.HasFaction && _tabs.Count > 0) BuildTabs();
            Chat.Update();
            Flags.SuppressCustomImages = _dialogs.Count > 0 || _popup != null;
            foreach (var d in _dialogs.ToList()) d.Update();
            _dialogs.RemoveAll(x => x.Done);

            bool captureText = _dialogs.Any(d => d.CapturesText) || Chat.IsOpen;
            bool interactive = IsOpen || _popup != null || _dialogs.Count > 0 || Chat.IsOpen;
            if (cutscene && !interactive) return;
            if (interactive) BlockControls(captureText || Mod.Config.General.BlockMovementWhileMenuOpen);
            Input.Poll(r, Mod.Config.General.EnableMouse, IsOpen || _popup != null || _dialogs.Count > 0 || Chat.IsOpen);

            if (!IsOpen) DrawHud();
            if (IsOpen) DrawMenu();
            if (_popup != null) DrawPopup();
            if (Chat.IsOpen && (!IsOpen || Current is DialoguePage)) Chat.Draw(r, Input);
            if (_dialogs.Count > 0)
            {
                var top = _dialogs[_dialogs.Count - 1];
                top.Draw(r, Input);
                _dialogs.RemoveAll(x => x.Done);
            }
            Notify.Draw(r, IsOpen ? 40f : 230f);
            if (Mod.Config.Debug.ShowDebugOverlay) DrawDebug();
        }

        private static readonly GTA.Control[] Blocked =
        {
            GTA.Control.Attack, GTA.Control.Attack2, GTA.Control.Aim, GTA.Control.MeleeAttackLight, GTA.Control.MeleeAttackHeavy,
            GTA.Control.MeleeAttackAlternate, GTA.Control.MeleeAttack1, GTA.Control.MeleeAttack2, GTA.Control.WeaponWheelNext, GTA.Control.WeaponWheelPrev,
            GTA.Control.WeaponWheelUpDown, GTA.Control.WeaponWheelLeftRight, GTA.Control.SelectWeapon, GTA.Control.SelectNextWeapon, GTA.Control.SelectPrevWeapon,
            GTA.Control.NextWeapon, GTA.Control.PrevWeapon, GTA.Control.Phone, GTA.Control.PhoneUp, GTA.Control.PhoneDown, GTA.Control.PhoneLeft, GTA.Control.PhoneRight,
            GTA.Control.PhoneSelect, GTA.Control.PhoneCancel, GTA.Control.Cover, GTA.Control.Context, GTA.Control.ContextSecondary, GTA.Control.Reload,
            GTA.Control.FrontendPause, GTA.Control.FrontendPauseAlternate, GTA.Control.CharacterWheel, GTA.Control.Jump, GTA.Control.VehicleAim,
            GTA.Control.VehicleAttack, GTA.Control.VehicleAttack2, GTA.Control.VehiclePassengerAim, GTA.Control.VehiclePassengerAttack,
            GTA.Control.VehicleRadioWheel, GTA.Control.VehicleNextRadio, GTA.Control.VehiclePrevRadio, GTA.Control.VehicleCinCam, GTA.Control.SpecialAbility,
            GTA.Control.SpecialAbilityPC, GTA.Control.Detonate, GTA.Control.ThrowGrenade, GTA.Control.Talk, GTA.Control.Enter, GTA.Control.VehicleExit,
            GTA.Control.Duck, GTA.Control.MultiplayerInfo, GTA.Control.Map, GTA.Control.VehicleHeadlight, GTA.Control.VehicleHorn,
        };

        private void BlockControls(bool all)
        {
            if (all)
            {
                Game.DisableAllControlsThisFrame();
                return;
            }
            foreach (var c in Blocked) Nat.DisableControl(c);
            if (Mod.Config.General.EnableMouse)
            {
                Nat.DisableControl(GTA.Control.LookLeftRight);
                Nat.DisableControl(GTA.Control.LookUpDown);
                Nat.DisableControl(GTA.Control.VehicleMouseControlOverride);
            }
        }

        // ------------------------------------------------------------------ drawing: menu

        private float _px, _py, _pw, _ph;

        private void DrawMenu()
        {
            if (_tabs.Count == 0) BuildTabs();
            var r = Renderer;
            var t = r.Theme;
            _pw = Math.Min(1580f, r.W - 80f);
            _ph = 920f;
            _px = (r.W - _pw) / 2f;
            _py = (r.H - _ph) / 2f;
            r.Rect(0, 0, r.W, r.H, t.Backdrop);
            r.Rect(_px, _py, _pw, _ph, t.Panel);
            r.Corners(_px - 5, _py - 5, _pw + 10, _ph + 10, 26f, 2f, t.Accent);
            DrawHeader();
            DrawSidebar();
            DrawContent();
            DrawFooter();
        }

        private void DrawHeader()
        {
            var r = Renderer;
            var t = r.Theme;
            float x = _px, y = _py, h = 96f;
            r.Rect(x, y, _pw, h, t.PanelAlt);
            r.Rect(x, y + h - 2, _pw, 2, t.Accent);
            var f = Mod.Faction;
            if (f == null)
            {
                r.Text("ORGANIZATION EMPIRE", x + 28, y + 14, Theme.TitleSize, t.Text, Theme.Condensed);
                r.Text("No faction yet - found one to begin.", x + 30, y + 58, Theme.SmallSize, t.TextDim);
                return;
            }
            var identity = f.IsGovernment ? f.Government.StateIdentity : f.Identity;
            Flags.Draw(r, identity.Flag, x + 22, y + 16, 104, 64, _dialogs.Count == 0);

            // Stat chips
            var w = Mod.State.World;
            var chips = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("CASH", TextUtil.Money(f.Resources.Cash)),
                new KeyValuePair<string, string>("MEMBERS", f.ActiveMembers.Count() + "/" + f.LivingMemberCount),
                new KeyValuePair<string, string>("TERRITORY", TerritorySystem.PlayerTerritoryCount(w).ToString()),
                new KeyValuePair<string, string>("REP", ((int)f.Reputation).ToString()),
                new KeyValuePair<string, string>("MORALE", ((int)f.Morale).ToString()),
                new KeyValuePair<string, string>(f.IsGovernment ? "STABILITY" : FactionTypeInfo.SuspicionLabel(f.Type).ToUpperInvariant(), f.IsGovernment ? ((int)f.Government.Stability).ToString() : ((int)f.Suspicion).ToString()),
                new KeyValuePair<string, string>("LEVEL", f.Level.ToString()),
            };
            float cx = x + _pw - 24;
            for (int i = chips.Count - 1; i >= 0; i--)
            {
                float cw = Math.Max(92f, r.TextWidth(chips[i].Value, Theme.HeadSize, Theme.Numbers) + 26);
                cx -= cw;
                r.Rect(cx, y + 18, cw - 8, 60, Color.FromArgb(40, 255, 255, 255));
                r.Text(chips[i].Key, cx + 10, y + 22, Theme.TinySize, t.TextFaint);
                Color vc = t.Text;
                if (chips[i].Key == "HEAT" || chips[i].Key == "SUSPICION") vc = f.Suspicion >= 60 ? t.Bad : f.Suspicion >= 40 ? t.Warn : t.Good;
                r.Text(chips[i].Value, cx + 10, y + 40, Theme.HeadSize, vc, Theme.Numbers);
            }
            string name = f.IsGovernment ? f.Government.StateName : f.Name;
            string sub = f.IsGovernment
                ? f.Government.GovernmentName + "  |  " + f.Government.RulerTitle
                : FactionTypeInfo.TierName(f) + "  |  " + FactionTypeInfo.TypeName(f.Type) + "  |  " + f.PlayerTitle;
            float nameMax = Math.Max(120f, cx - (x + 146) - 24f);
            r.Text(r.Ellipsize(name.ToUpperInvariant(), Theme.TitleSize, Theme.Condensed, nameMax), x + 146, y + 10, Theme.TitleSize, t.Text, Theme.Condensed);
            r.Text(r.Ellipsize(sub, Theme.SmallSize, Theme.Body, nameMax), x + 148, y + 58, Theme.SmallSize, t.TextDim);
            r.Bar(cx, y + 82, x + _pw - 24 - cx - 8, 3, ProgressionSystem.LevelProgress(f), t.Accent);
            r.Text("DAY " + w.Day + "  " + w.Hour.ToString("00") + ":00", cx - 14, y + 66, Theme.TinySize, t.TextFaint, Theme.Body, Align.Right);
        }

        private void DrawSidebar()
        {
            var r = Renderer;
            var t = r.Theme;
            float x = _px, y = _py + 106, w = 236f;
            float rowH = Math.Min(40f, (_ph - 106 - 52) / Math.Max(1, _tabs.Count));
            r.Rect(x, y - 6, w, _ph - 106 - 40, Color.FromArgb(120, 0, 0, 0));
            for (int i = 0; i < _tabs.Count; i++)
            {
                float ry = y + i * rowH;
                bool sel = i == _tab;
                bool hover = r.Hover(x, ry, w, rowH - 2);
                if (sel) { r.Rect(x, ry, w, rowH - 2, t.AccentDim); r.Rect(x, ry, 4, rowH - 2, t.Accent); }
                else if (hover) r.Rect(x, ry, w, rowH - 2, Color.FromArgb(30, 255, 255, 255));
                string label = _tabs[i].Name;
                if (label == "Government" && Mod.HasFaction && TakeoverSystem.AwaitingGovernmentFormation(Mod.Faction)) label += "  (!)";
                if (label == "Missions" && Mod.HasFaction && Mod.State.MissionBoard.Any(o => o.Urgent)) label += "  (!)";
                r.Text(label.ToUpperInvariant(), x + 20, r.TextTop(ry, rowH - 2, Theme.SmallSize + 0.02f, Theme.Condensed), Theme.SmallSize + 0.02f, sel ? t.Text : t.TextDim, Theme.Condensed);
                if (hover && Input.Click && _dialogs.Count == 0 && _popup == null) { _tab = i; Nat.Sound("NAV_LEFT_RIGHT"); }
            }
        }

        private void DrawContent()
        {
            var r = Renderer;
            var t = r.Theme;
            var page = Current;
            float x = _px + 252, y = _py + 106, w = _pw - 252 - 20, h = _ph - 106 - 48;
            r.Text(page.Title.ToUpperInvariant(), x, y - 2, Theme.HeadSize + 0.04f, t.Text, Theme.Condensed);
            float crumbX = x + r.TextWidth(page.Title.ToUpperInvariant(), Theme.HeadSize + 0.04f, Theme.Condensed) + 16;
            if (Stack.Count > 1) r.Text("< " + string.Join(" / ", Stack.Reverse().Take(Stack.Count - 1).Select(p => p.Title)), crumbX, y + 10, Theme.TinySize, t.TextFaint);
            if (page.Subtitle != null) r.Text(r.Ellipsize(page.Subtitle, Theme.SmallSize, Theme.Body, w), x, y + 36, Theme.SmallSize, t.TextDim);
            float listY = y + 70, listH = h - 70;
            float listW = w * page.ListFraction;
            _items.Items.Clear();
            page.Build(_items);
            var focused = DrawList(page, _items.Items, x, listY, listW, listH, _dialogs.Count == 0 && _popup == null);
            float dx = x + listW + 24, dw = w - listW - 24;
            r.Rect(dx - 12, listY, 1, listH, t.Line);
            page.DrawDetail(r, dx + 6, listY + 4, dw - 12, listH - 8, focused);
            if (Input.RightClick && _dialogs.Count == 0 && _popup == null) Back();
        }

        private void DrawFooter()
        {
            var r = Renderer;
            var t = r.Theme;
            if (!Mod.Config.General.ShowHints) return;
            float y = _py + _ph - 34;
            r.Rect(_px, y - 6, _pw, 1, t.Line);
            string hints = "Up/Down: select   Left/Right: change   Enter: confirm   Backspace: back   Tab / PgUp / PgDn: tabs   " + Mod.Config.Hotkeys.OpenMenu + " / Esc: close" + (Mod.Config.General.EnableMouse ? "   Mouse: click, wheel, right-click back" : "");
            r.Text(r.Ellipsize(hints, Theme.TinySize, Theme.Body, _pw - 220), _px + 20, y, Theme.TinySize, t.TextFaint);
            r.Text("Organization Empire " + ModContext.Version, _px + _pw - 20, y, Theme.TinySize, t.TextFaint, Theme.Body, Align.Right);
        }

        /// <summary>Draws a scrollable item list with keyboard focus and mouse support. Returns the focused item.</summary>
        public Item DrawList(Page page, List<Item> items, float x, float y, float w, float h, bool interactive)
        {
            var r = Renderer;
            var t = r.Theme;
            if (page.Focus < 0 || page.Focus >= items.Count || !items[page.Focus].Selectable) page.Focus = FirstSelectable(items, page.Focus < 0 ? -1 : page.Focus - 1, 1);
            // Row layout
            var heights = new float[items.Count];
            var tops = new float[items.Count];
            float total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                heights[i] = it.Kind == ItemKind.Text ? r.ParagraphHeight(it.Label, Theme.SmallSize, Theme.Body, w - 20) + 10 : it.RowHeight;
                tops[i] = total;
                total += heights[i];
            }
            // Keep focus visible
            if (page.Focus >= 0)
            {
                float ft = tops[page.Focus], fb = ft + heights[page.Focus];
                if (ft < page.Scroll) page.Scroll = ft - (page.Focus > 0 && items[page.Focus - 1].Kind == ItemKind.Header ? heights[page.Focus - 1] : 0);
                if (fb > page.Scroll + h) page.Scroll = fb - h;
            }
            if (interactive && Input.Wheel != 0 && r.Hover(x, y, w, h)) page.Scroll -= Input.Wheel * 80f;
            page.Scroll = MathEx.Clamp(page.Scroll, 0, Math.Max(0, total - h));

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float ry = y + tops[i] - page.Scroll;
                float rh = heights[i];
                if (ry < y - 1 || ry + rh > y + h + 1) continue;
                bool focused = i == page.Focus;
                bool hover = interactive && it.Selectable && r.Hover(x, ry, w, rh);
                if (hover && Input.MouseMoved) { page.Focus = i; focused = true; }
                DrawRow(it, x, ry, w, rh, focused);
                if (hover && Input.Click)
                {
                    page.Focus = i;
                    if (it.Kind == ItemKind.Choice || it.Kind == ItemKind.Slider)
                    {
                        float mx = r.MouseX;
                        Adjust(it, mx < x + w - 110 ? 1 : (mx < x + w - 55 ? -1 : 1));
                    }
                    else Activate(it);
                }
            }
            if (total > h)
            {
                float barH = h * h / total;
                float barY = y + (h - barH) * (page.Scroll / Math.Max(1, total - h));
                r.Rect(x + w + 4, y, 3, h, Color.FromArgb(30, 255, 255, 255));
                r.Rect(x + w + 4, barY, 3, barH, t.Accent);
            }
            return page.Focus >= 0 && page.Focus < items.Count ? items[page.Focus] : null;
        }

        private void DrawRow(Item it, float x, float y, float w, float h, bool focused)
        {
            var r = Renderer;
            var t = r.Theme;
            float size = Theme.BodySize;
            switch (it.Kind)
            {
                case ItemKind.Header:
                    r.Text(it.Label.ToUpperInvariant(), x + 2, y + 12, Theme.TinySize + 0.02f, t.Accent, Theme.Condensed);
                    r.Rect(x + 2, y + h - 6, w - 4, 1, t.Line);
                    return;
                case ItemKind.Spacer:
                    return;
                case ItemKind.Text:
                    r.Paragraph(it.Label, x + 8, y + 4, Theme.SmallSize, it.Tint ?? t.TextDim, w - 20);
                    return;
            }
            if (focused) { r.Rect(x, y + 1, w, h - 2, t.AccentDim); r.Rect(x, y + 1, 3, h - 2, t.Accent); }
            Color labelColor = !it.Enabled ? t.TextFaint : it.Danger ? t.Bad : (it.Kind == ItemKind.Info ? t.TextDim : t.Text);
            float valueRight = x + w - 12;
            float ty = r.TextTop(y, it.Kind == ItemKind.Progress ? 26 : h, size);
            string valueText = it.Value ?? "";
            float valueW = 0;
            switch (it.Kind)
            {
                case ItemKind.Link:
                    r.Text(">", valueRight, ty, size, focused ? t.Accent : t.TextFaint, Theme.Body, Align.Right);
                    valueRight -= 24;
                    break;
                case ItemKind.Toggle:
                    bool on = it.Value == "ON";
                    r.Rect(valueRight - 56, y + h / 2 - 11, 56, 22, on ? t.Accent : Color.FromArgb(60, 255, 255, 255));
                    r.Text(on ? "ON" : "OFF", valueRight - 28, r.TextTop(y + h / 2 - 11, 22, Theme.TinySize), Theme.TinySize, on ? t.AccentText : t.TextDim, Theme.Body, Align.Center);
                    valueText = "";
                    valueW = 60;
                    break;
                case ItemKind.Choice:
                    r.Text("<", valueRight - 100, ty, size, focused ? t.Accent : t.TextFaint);
                    r.Text(">", valueRight, ty, size, focused ? t.Accent : t.TextFaint, Theme.Body, Align.Right);
                    valueRight -= 22;
                    break;
                case ItemKind.Slider:
                    r.Bar(valueRight - 100, y + h / 2 + 8, 100, 3, it.Fraction, t.Accent);
                    break;
            }
            if (it.Swatch.HasValue)
            {
                r.Rect(valueRight - 26, y + h / 2 - 9, 22, 18, it.Swatch.Value.ToColor());
                r.Border(valueRight - 26, y + h / 2 - 9, 22, 18, 1, Color.FromArgb(120, 255, 255, 255));
                valueRight -= 32;
            }
            if (valueText.Length > 0)
            {
                string v = r.Ellipsize(valueText, size - 0.02f, Theme.Body, w * 0.5f);
                Color vc = it.Tint ?? (it.Kind == ItemKind.Info ? t.Text : (focused ? t.Text : t.TextDim));
                if (it.Kind == ItemKind.Choice) vc = focused ? t.Text : t.TextDim;
                r.Text(v, valueRight - (it.Kind == ItemKind.Slider ? 108 : 0), ty + 1, size - 0.02f, vc, Theme.Body, Align.Right);
                valueW = r.TextWidth(v, size - 0.02f) + 16 + (it.Kind == ItemKind.Slider ? 108 : 0);
            }
            float labelMax = Math.Max(40, valueRight - x - 18 - valueW);
            r.Text(r.Ellipsize(it.Label, size, Theme.Body, labelMax), x + 14, ty, size, labelColor);
            if (it.Kind == ItemKind.Progress)
            {
                r.Bar(x + 14, y + 30, w - 28, 5, it.Fraction, it.Tint ?? t.Accent);
            }
        }

        // ------------------------------------------------------------------ popups

        private void DrawPopup()
        {
            var r = Renderer;
            var t = r.Theme;
            var page = _popup.Peek();
            float w = 560f, h = 640f;
            float x = r.W - w - 60f, y = (r.H - h) / 2f;
            r.Rect(x, y, w, h, t.Panel);
            r.Rect(x, y, w, 4, t.Accent);
            r.Corners(x - 4, y - 4, w + 8, h + 8, 16, 2, t.Accent);
            r.Text(r.Ellipsize(page.Title.ToUpperInvariant(), Theme.HeadSize, Theme.Condensed, w - 40), x + 20, y + 14, Theme.HeadSize, t.Text, Theme.Condensed);
            if (page.Subtitle != null) r.Text(r.Ellipsize(page.Subtitle, Theme.SmallSize, Theme.Body, w - 40), x + 20, y + 50, Theme.SmallSize, t.TextDim);
            _items.Items.Clear();
            page.Build(_items);
            var focused = DrawList(page, _items.Items, x + 14, y + 84, w - 34, h - 84 - 96, _dialogs.Count == 0);
            r.Rect(x + 14, y + h - 92, w - 28, 1, t.Line);
            string hint = focused == null ? "" : (!focused.Enabled && focused.DisabledReason != null ? focused.DisabledReason : focused.Hint ?? "");
            r.Paragraph(hint, x + 20, y + h - 84, Theme.SmallSize, focused != null && !focused.Enabled ? t.Warn : t.TextDim, w - 40, Theme.Body, 2);
            r.Text("Enter: select   Left/Right: change   Backspace: back   Esc: close", x + 20, y + h - 30, Theme.TinySize, t.TextFaint);
            if (Input.RightClick && _dialogs.Count == 0) Back();
        }

        // ------------------------------------------------------------------ HUD (menu closed)

        private void DrawHud()
        {
            var r = Renderer;
            var t = r.Theme;
            int now = Environment.TickCount;
            if (_popup == null && !Chat.IsOpen) Mod.Interaction.DrawPrompt(r);

            // Territory banner
            var terr = Mod.Territories;
            if (terr.BannerText != null && Game.GameTime < terr.BannerUntil && Mod.HasFaction)
            {
                float a = Math.Min(1f, (terr.BannerUntil - Game.GameTime) / 600f);
                r.Text(terr.BannerText, r.W / 2, 150, 0.6f, Color.FromArgb((int)(230 * a), 240, 240, 240), Font_Condensed, Align.Center, true, true);
                r.Text(terr.BannerSub, r.W / 2, 192, 0.32f, Color.FromArgb((int)(230 * a), t.Accent.R, t.Accent.G, t.Accent.B), GTA.UI.Font.ChaletLondon, Align.Center, true, false);
            }

            // Mission intro
            if (_bigTitle != null && now < _bigUntil)
            {
                float a = Math.Min(1f, (_bigUntil - now) / 800f);
                float w = 760f;
                float x = (r.W - w) / 2, y = 250f;
                float th = r.ParagraphHeight(_bigText ?? "", Theme.SmallSize, Theme.Body, w - 48);
                r.Rect(x, y, w, 76 + th, Color.FromArgb((int)(210 * a), 10, 13, 18));
                r.Rect(x, y, w, 3, Color.FromArgb((int)(255 * a), t.Accent.R, t.Accent.G, t.Accent.B));
                r.Text(_bigTitle.ToUpperInvariant(), r.W / 2, y + 12, 0.5f, Color.FromArgb((int)(255 * a), 240, 240, 240), Font_Condensed, Align.Center);
                r.Paragraph(_bigText ?? "", x + 24, y + 58, Theme.SmallSize, Color.FromArgb((int)(255 * a), 200, 205, 212), w - 48);
            }

            // Active mission objective box
            var m = Mod.Missions.Active;
            if (m != null)
            {
                float w = 600f, x = (r.W - w) / 2f, y = 24f;
                bool hasProgress = m.Progress >= 0;
                float h = hasProgress ? 98f : 84f;
                r.Rect(x, y, w, h, Color.FromArgb(200, 10, 13, 18));
                r.Rect(x, y, 4, h, t.Accent);
                r.Text(m.Def.Title.ToUpperInvariant(), x + 18, y + 8, Theme.SmallSize + 0.02f, t.Accent, Font_Condensed);
                int secs = m.SecondsLeft;
                if (secs >= 0) r.Text((secs / 60) + ":" + (secs % 60).ToString("00"), x + w - 16, y + 8, Theme.SmallSize + 0.02f, secs < 60 ? t.Bad : t.Text, Font_Condensed, Align.Right);
                r.Text(r.Ellipsize(m.Objective ?? "", Theme.SmallSize + 0.01f, Theme.Body, w - 36), x + 18, y + 34, Theme.SmallSize + 0.01f, t.Text);
                r.Text(r.Ellipsize(m.Detail ?? "", Theme.SmallSize, Theme.Body, w - 36), x + 18, y + 58, Theme.SmallSize, t.TextDim);
                if (hasProgress) r.Bar(x + 18, y + h - 12, w - 36, 4, m.Progress, t.Accent);
            }
        }

        private static GTA.UI.Font Font_Condensed => GTA.UI.Font.ChaletComprimeCologne;

        private void DrawDebug()
        {
            var r = Renderer;
            var e = Mod.Entities;
            string s = "OE debug | tick " + EmpireScript.LastTickMs.ToString("0.00") + " ms | entities " + e.Total
                       + " (mem " + e.Count(SpawnCategory.Member) + ", mis " + e.Count(SpawnCategory.Mission) + ", riv " + e.Count(SpawnCategory.Rival)
                       + ", veh " + e.Count(SpawnCategory.Vehicle) + ") | deployed " + Mod.Members.DeployedCount + " | AI in flight " + Mod.Dialogue.InFlight
                       + " | zone " + Mod.Territories.CurrentZoneCode;
            r.Rect(10, r.H - 40, r.TextWidth(s, Theme.TinySize) + 20, 28, Color.FromArgb(180, 0, 0, 0));
            r.Text(s, 20, r.H - 36, Theme.TinySize, Color.FromArgb(255, 180, 255, 180));
        }
    }
}
