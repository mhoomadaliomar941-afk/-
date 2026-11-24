using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.World;

namespace OrganizationEmpire.UI.Pages
{
    /// <summary>Faction creation: type, name, identity, flag, personality, behavior, difficulty, progression, members, uniform, HQ.</summary>
    internal sealed class CreationWizardPage : Page
    {
        private readonly FactionCreationOptions _o = new FactionCreationOptions { Name = "", InitialMembers = 3, HasHeadquarters = true };
        private FlagDesign _flag;

        public override string Title => "Found Your Faction";
        public override string Subtitle => "Configure every aspect, then create. Everything except the type can be changed later.";

        public override void OnShow()
        {
            ApplyTypeDefaults();
        }

        private void ApplyTypeDefaults()
        {
            switch (_o.Type)
            {
                case FactionType.Mafia: _o.PrimaryColor = "#C8A165"; _o.SecondaryColor = "#1E1E1E"; _o.Personality = FactionPersonality.Honorable; _o.Behavior = FactionBehavior.Balanced; _o.Emblem = EmblemType.Crown; break;
                case FactionType.Militia: _o.PrimaryColor = "#556B2F"; _o.SecondaryColor = "#1E1E1E"; _o.Personality = FactionPersonality.Zealous; _o.Behavior = FactionBehavior.Covert; _o.Emblem = EmblemType.Star; break;
                default: _o.PrimaryColor = "#8E44AD"; _o.SecondaryColor = "#1E1E1E"; _o.Personality = FactionPersonality.Flamboyant; _o.Behavior = FactionBehavior.Aggressive; _o.Emblem = EmblemType.Spade; _o.Activities = GangActivities.Both; break;
            }
            if (_o.Type != FactionType.StreetGang) _o.Activities = GangActivities.None;
            _o.UniformPreset = UniformCatalog.DefaultFor(_o.Type).Id;
            _flag = FlagCatalog.DefaultFor(_o.Type, _o.PrimaryColor, _o.SecondaryColor);
        }

        public override void Build(ItemList l)
        {
            PageUtil.LanguageChoice(l, Mod);
            l.Header("1. Type");
            l.Choice("Faction type", FactionTypeInfo.TypeName(_o.Type), d => { _o.Type = PageUtil.Next(_o.Type, d); ApplyTypeDefaults(); }, FactionTypeInfo.TypeBlurb(_o.Type));

            l.Header("2. Name & description");
            l.Action("Name", () => Ui.AskText("Faction name", _o.Name, 40, s => _o.Name = s, null, s => s.Length < 2 ? "At least 2 characters." : null),
                "The name your members and rivals will use.", value: string.IsNullOrEmpty(_o.Name) ? "(required)" : _o.Name);
            l.Action("Tag / abbreviation", () => Ui.AskText("Tag", _o.Tag, 5, s => _o.Tag = s), "Used on plates, blips and the flag monogram. Leave empty to use initials.",
                value: string.IsNullOrEmpty(_o.Tag) ? TextUtil.Initials(_o.Name) : _o.Tag);
            l.Action("Description", () => Ui.AskText("Description", _o.Description, 160, s => _o.Description = s), "Optional. Fed to AI dialogue as faction lore.",
                value: string.IsNullOrEmpty(_o.Description) ? "-" : TextUtil.Truncate(_o.Description, 30));
            l.Action("Motto", () => Ui.AskText("Motto", _o.Motto, 60, s => _o.Motto = s), "Optional.", value: string.IsNullOrEmpty(_o.Motto) ? "-" : TextUtil.Truncate(_o.Motto, 30));

            l.Header("3. Identity");
            PageUtil.ColorRow(l, Ui, "Primary color", _o.PrimaryColor, c => { _o.PrimaryColor = c; _flag.Color2 = c; }, "Blips, UI accent, vehicles.");
            PageUtil.ColorRow(l, Ui, "Secondary color", _o.SecondaryColor, c => { _o.SecondaryColor = c; _flag.Color1 = c; });
            l.Choice("Flag pattern", PageUtil.Name(_flag.Pattern), d => _flag.Pattern = PageUtil.Next(_flag.Pattern, d), "Refine everything later in the Flag Designer (including custom images).");
            l.Choice("Emblem", PageUtil.Name(_o.Emblem), d => { _o.Emblem = PageUtil.Next(_o.Emblem, d); _flag.Emblem = _o.Emblem == EmblemType.CustomImage ? EmblemType.Monogram : _o.Emblem; });
            var uniforms = UniformCatalog.ForType(_o.Type).ToList();
            l.Choice("Uniform", uniforms, UniformCatalog.Get(_o.UniformPreset) ?? uniforms[0], u => _o.UniformPreset = u.Id, u => u.Name,
                "Model set used for hired members and reinforcements. Recruited NPCs keep their own look unless the option below is on.");
            l.Toggle("Recruits wear the uniform", _o.ApplyUniformToRecruits, v => _o.ApplyUniformToRecruits = v,
                "When on, recruited NPCs are swapped to a uniform model (keeping their name and personality).");

            l.Header("4. Personality & behavior");
            l.Choice("Faction personality", PageUtil.Name(_o.Personality), d => _o.Personality = PageUtil.Next(_o.Personality, d),
                "Ruthless / Honorable / Pragmatic / Flamboyant / Secretive / Zealous. Affects diplomacy, heat decay and dialogue tone.");
            l.Choice("Behavior", PageUtil.Name(_o.Behavior), d => _o.Behavior = PageUtil.Next(_o.Behavior, d),
                "Aggressive raises rival hostility; Expansionist gains territory faster; Covert keeps suspicion down; Defensive and Balanced are steady.");
            if (_o.Type == FactionType.StreetGang)
            {
                var acts = new[] { GangActivities.None, GangActivities.Narcotics, GangActivities.Arms, GangActivities.Both };
                l.Choice("Side activities", acts, _o.Activities, a => _o.Activities = a, a => a == GangActivities.Narcotics ? "Product" : a == GangActivities.Arms ? "Arms trade" : a == GangActivities.Both ? "Product + Arms" : "None",
                    "Fictional resource systems: 'Product' converts supplies into income and unlocks Stash Run missions; 'Arms trade' stockpiles and sells Arms and unlocks Arms Pickup missions. Both raise heat.");
            }

            l.Header("5. Start");
            l.Choice("Difficulty", PageUtil.Name(_o.Difficulty), d => _o.Difficulty = PageUtil.Next(_o.Difficulty, d), "Scales income, upkeep, rival aggression, recruitment and suspicion.");
            l.Choice("Starting progression", PageUtil.Name(_o.Progression), d => _o.Progression = PageUtil.Next(_o.Progression, d),
                "Humble: level 1, little cash. Established: level 4 with a foothold. Powerful: level 8, rich, several territories.");
            l.Slider("Initial members", _o.InitialMembers, 0, FactionFactory.MaxInitialMembers, 1, v => _o.InitialMembers = (int)v, "0",
                "Generated members who join immediately (in uniform). You can recruit more from the streets.");
            l.Toggle("Headquarters at my current location", _o.HasHeadquarters, v => _o.HasHeadquarters = v,
                "Uses where you're standing now. You can move it any time from the Headquarters page.");

            l.Header("6. Create");
            bool valid = !string.IsNullOrWhiteSpace(_o.Name) && _o.Name.Trim().Length >= 2;
            l.Action("CREATE FACTION", Create, "Founds the faction with these settings.", valid, disabledReason: "Give your faction a name first.");
        }

        private void Create()
        {
            var player = GtaUtil.Player;
            if (_o.HasHeadquarters)
            {
                if (player.IsInVehicle()) { Ui.Notify.Push("Step out of the vehicle to set the headquarters here (or turn the HQ option off).", EventSeverity.Warning); return; }
                _o.HqPosition = player.Position.ToVec3();
                _o.HqHeading = player.Heading;
                _o.HqZone = GtaUtil.ZoneCode(player.Position);
                _o.HqName = _o.Name.Trim() + " HQ";
            }
            _flag.MonogramText = string.IsNullOrEmpty(_o.Tag) ? TextUtil.Initials(_o.Name) : _o.Tag;
            _o.Flag = _flag;
            Ui.Confirm("Create " + _o.Name + "?", "Create a " + FactionTypeInfo.TypeName(_o.Type) + " with " + _o.InitialMembers + " starting member(s) on " + _o.Difficulty + " difficulty?", () =>
            {
                Mod.NewState();
                Mod.State.Faction = FactionFactory.Create(Mod.State, _o, Mod.Rng);
                foreach (var m in Mod.Faction.Members) m.Order = OrderType.Follow;
                MissionBoard.Refresh(Mod.Sim, true);
                Mod.FlushNotifications();
                string slot = SaveManagerSlot(_o.Name);
                var r = Mod.Save(slot);
                Ui.RebuildTabs();
                Ui.Notify.Push(Mod.Faction.Name + " is born. " + (r.Ok ? "Saved to slot '" + slot + "'." : r.Message), EventSeverity.Good);
                Nat.Sound("RANK_UP", "HUD_AWARDS");
            });
        }

        private static string SaveManagerSlot(string name) => Core.Persistence.SaveManager.SanitizeSlot(name);

        public override void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            var t = r.Theme;
            r.Text("PREVIEW", x, y, Theme.TinySize + 0.02f, t.Accent, Theme.Condensed);
            var preview = _flag.Copy();
            preview.MonogramText = string.IsNullOrEmpty(_o.Tag) ? TextUtil.Initials(_o.Name) : _o.Tag;
            Ui.Flags.Draw(r, preview, x, y + 30, 240, 150, false);
            float cy = y + 30;
            float tx = x + 264;
            r.Text(string.IsNullOrEmpty(_o.Name) ? "UNNAMED" : _o.Name.ToUpperInvariant(), tx, cy, Theme.HeadSize, Rgb.Parse(_o.PrimaryColor, new Rgb(255, 255, 255)).ToColor(), Theme.Condensed);
            r.Text(FactionTypeInfo.TypeName(_o.Type), tx, cy + 40, Theme.SmallSize, t.TextDim);
            r.Text(FactionTypeInfo.Tiers(_o.Type)[0] + " -> " + FactionTypeInfo.Tiers(_o.Type).Last(), tx, cy + 66, Theme.SmallSize, t.TextDim);
            r.Text(_o.Difficulty + " | " + _o.Progression + " | " + _o.InitialMembers + " members", tx, cy + 92, Theme.SmallSize, t.TextDim);
            cy = y + 200;
            r.Paragraph(FactionTypeInfo.TypeBlurb(_o.Type), x, cy, Theme.SmallSize, t.Text, w);
            cy += r.ParagraphHeight(FactionTypeInfo.TypeBlurb(_o.Type), Theme.SmallSize, Theme.Body, w) + 18;
            if (focused != null)
            {
                r.Rect(x, cy, w, 1, t.Line);
                cy += 12;
                r.Text(focused.Label?.ToUpperInvariant(), x, cy, Theme.SmallSize + 0.02f, t.Accent, Theme.Condensed);
                cy += 30;
                if (!focused.Enabled && focused.DisabledReason != null) cy += r.Paragraph(focused.DisabledReason, x, cy, Theme.SmallSize, t.Warn, w) + 6;
                if (focused.Hint != null) r.Paragraph(focused.Hint, x, cy, Theme.SmallSize, t.TextDim, w);
            }
        }
    }
}
