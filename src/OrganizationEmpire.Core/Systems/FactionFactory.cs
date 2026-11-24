using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public sealed class FactionCreationOptions
    {
        public string Name = "New Faction";
        public string Description = "";
        public FactionType Type = FactionType.StreetGang;
        public string PrimaryColor = "#B01E23";
        public string SecondaryColor = "#1E1E1E";
        public FlagDesign Flag;
        public EmblemType Emblem = EmblemType.Star;
        public FactionPersonality Personality = FactionPersonality.Pragmatic;
        public FactionBehavior Behavior = FactionBehavior.Balanced;
        public GangActivities Activities = GangActivities.None;
        public Difficulty Difficulty = Difficulty.Normal;
        public StartingProgression Progression = StartingProgression.Humble;
        public int InitialMembers = 3;
        public string UniformPreset;
        public bool ApplyUniformToRecruits;
        public bool HasHeadquarters;
        public Vec3 HqPosition;
        public float HqHeading;
        public string HqZone;
        public string HqName = "Headquarters";
        public string Tag = "";
        public string Motto = "";
    }

    public static class FactionFactory
    {
        public const int MaxInitialMembers = 12;

        public static FactionState Create(GameState state, FactionCreationOptions o, Rng rng)
        {
            var dp = DifficultyProfile.For(o.Difficulty);
            var f = new FactionState
            {
                Name = string.IsNullOrWhiteSpace(o.Name) ? "Unnamed Faction" : o.Name.Trim(),
                Description = o.Description ?? "",
                Type = o.Type,
                Difficulty = o.Difficulty,
                Personality = o.Personality,
                Behavior = o.Behavior,
                Activities = o.Type == FactionType.StreetGang ? o.Activities : GangActivities.None,
                CreatedDay = state.World.Day,
            };

            f.Identity = new Identity
            {
                PrimaryColor = o.PrimaryColor,
                SecondaryColor = o.SecondaryColor,
                Flag = o.Flag?.Copy() ?? FlagCatalog.DefaultFor(o.Type, o.PrimaryColor, o.SecondaryColor),
                Emblem = o.Emblem,
                Tag = string.IsNullOrWhiteSpace(o.Tag) ? TextUtil.Initials(f.Name) : o.Tag.Trim(),
                Motto = o.Motto ?? "",
                UniformPreset = o.UniformPreset ?? UniformCatalog.DefaultFor(o.Type).Id,
                ApplyUniformToRecruits = o.ApplyUniformToRecruits,
            };
            if (string.IsNullOrEmpty(f.Identity.Flag.MonogramText)) f.Identity.Flag.MonogramText = f.Identity.Tag;
            f.IdentityPresets.Add(new IdentityPreset { Name = "Original", Identity = f.Identity.Copy() });

            f.Ranks = RankTemplates.For(o.Type);

            // Starting progression
            switch (o.Progression)
            {
                case StartingProgression.Established:
                    f.Level = 4; f.Reputation = 30;
                    f.Resources = new Resources { Cash = 60000, Supplies = 40, Arms = 40, Influence = 20, Intel = 10 };
                    break;
                case StartingProgression.Powerful:
                    f.Level = 8; f.Reputation = 55;
                    f.Resources = new Resources { Cash = 200000, Supplies = 90, Arms = 100, Influence = 50, Intel = 30 };
                    break;
                default:
                    f.Level = 1; f.Reputation = 8;
                    f.Resources = new Resources { Cash = 15000, Supplies = 15, Arms = 12, Influence = 5, Intel = 2 };
                    break;
            }
            f.Resources.Cash = (long)(f.Resources.Cash * dp.StartingCash);
            if (o.Type == FactionType.Mafia) f.Resources.Influence += 10;
            if (o.Type == FactionType.Militia) { f.Resources.Supplies += 15; f.Suspicion = 0; f.PublicSupport = 15; }
            if (o.Type == FactionType.StreetGang && (f.Activities & GangActivities.Narcotics) != 0) f.Resources.Product = 10;

            // Squads
            f.Squads.Add(NewSquad(o.Type, 0));

            // Initial members
            int count = MathEx.Clamp(o.InitialMembers, 0, MaxInitialMembers);
            var uniform = UniformCatalog.Get(f.Identity.UniformPreset) ?? UniformCatalog.DefaultFor(o.Type);
            for (int i = 0; i < count; i++)
            {
                var m = GenerateMember(f, rng, rng.Pick(uniform.Models), false, state.World.Day);
                if (o.Progression != StartingProgression.Humble) m.Skill += 15;
                f.Members.Add(m);
                if (f.Squads[0].MemberIds.Count < 8) SquadSystem.AddMember(f, f.Squads[0], m);
            }
            // First member becomes deputy/second-in-command if we have any.
            if (f.Members.Count > 0)
            {
                var deputy = f.Members.OrderByDescending(m => m.Skill).First();
                var secondRank = f.Ranks.Where(r => !r.ReservedForPlayer).OrderBy(r => r.Order).First();
                deputy.RankId = secondRank.Id;
                deputy.IsDeputy = true;
                f.Squads[0].LeaderId = deputy.Id;
            }

            // Headquarters
            if (o.HasHeadquarters)
            {
                f.Headquarters = new HeadquartersState
                {
                    Name = string.IsNullOrWhiteSpace(o.HqName) ? "Headquarters" : o.HqName,
                    Position = o.HqPosition,
                    Heading = o.HqHeading,
                    ZoneCode = o.HqZone,
                    EstablishedDay = state.World.Day,
                };
                var t = state.World.FindByZone(o.HqZone);
                if (t != null)
                {
                    float amount = o.Progression == StartingProgression.Powerful ? 80 : o.Progression == StartingProgression.Established ? 50 : 25;
                    TerritorySystem.AddInfluence(t, FactionIds.Player, amount);
                    TerritorySystem.UpdateOwnership(t);
                }
            }

            // Powerful/established factions start with extra footholds.
            if (o.Progression != StartingProgression.Humble)
            {
                var free = state.World.Territories.Where(t => t.OwnerId == null).ToList();
                rng.Shuffle(free);
                int extra = o.Progression == StartingProgression.Powerful ? 3 : 1;
                foreach (var t in free.Take(extra))
                {
                    TerritorySystem.AddInfluence(t, FactionIds.Player, 65);
                    TerritorySystem.UpdateOwnership(t);
                }
            }

            EventLog.Add(f, state.World, "Founding", f.Name + " was founded as a " + FactionTypeInfo.TypeName(o.Type) + ".", EventSeverity.Good);
            return f;
        }

        public static Squad NewSquad(FactionType type, int index)
        {
            string[] names;
            switch (type)
            {
                case FactionType.Mafia: names = new[] { "First Crew", "Second Crew", "Third Crew", "Fourth Crew", "Fifth Crew", "Sixth Crew" }; break;
                case FactionType.Militia: names = new[] { "Alpha Unit", "Bravo Unit", "Charlie Unit", "Delta Unit", "Echo Unit", "Foxtrot Unit" }; break;
                default: names = new[] { "Main Crew", "Corner Boys", "Wrecking Crew", "Night Shift", "Young Guns", "Hustlers" }; break;
            }
            string name = index < names.Length ? names[index] : "Squad " + (index + 1);
            return new Squad
            {
                Id = Ids.New("sq"),
                Name = name,
                Callsign = type == FactionType.Militia ? name.Split(' ')[0] : name,
                Role = index == 0 ? SquadRole.Escort : SquadRole.Assault,
                Formation = FormationType.Default,
            };
        }

        /// <summary>Generates a member (used for initial members and hired recruits).</summary>
        public static Member GenerateMember(FactionState f, Rng rng, string model, bool female, int day)
        {
            var lowest = f.LowestRank;
            var m = new Member
            {
                Id = Ids.New("mem"),
                Female = female,
                FirstName = NameGenerator.FirstName(rng, female),
                LastName = NameGenerator.LastName(rng, f.Type),
                Nickname = rng.Chance(0.6f) ? NameGenerator.Nickname(rng, f.Type) : "",
                Age = rng.Next(19, 52),
                Hometown = NameGenerator.Hometown(rng),
                Background = NameGenerator.Background(rng, f.Type),
                Model = model,
                Personality = FactionTypeInfo.RandomPersonality(rng, f.Type),
                Role = f.Recruitment.DefaultRole,
                RankId = lowest?.Id,
                Loyalty = rng.Range(50, 75),
                Morale = rng.Range(55, 75),
                Skill = rng.Range(25, 55),
                Discipline = rng.Range(35, 65),
                JoinedDay = day,
                Loadout = LoadoutType.Sidearm,
            };
            do { m.SecondaryTrait = FactionTypeInfo.RandomPersonality(rng, f.Type); } while (m.SecondaryTrait == m.Personality && rng.Chance(0.8f));
            MemberSystem.ApplyPersonalityBaseline(m);
            return m;
        }
    }
}
