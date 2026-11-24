using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    /// <summary>Fully customizable rank hierarchy: create, rename, delete, reorder, permissions, promotions.</summary>
    public static class RankSystem
    {
        public const int MaxRanks = 12;
        public const int MaxNameLength = 32;

        public static void Normalize(FactionState f)
        {
            var ordered = f.Ranks.OrderBy(r => r.Order).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i;
                ordered[i].ReservedForPlayer = i == 0;
            }
            f.Ranks = ordered;
        }

        public static OpResult Create(FactionState f, string name, out Rank rank)
        {
            rank = null;
            name = (name ?? "").Trim();
            if (name.Length == 0) return OpResult.Fail("Rank name cannot be empty.");
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            if (f.Ranks.Count >= MaxRanks) return OpResult.Fail("Maximum of " + MaxRanks + " ranks reached.");
            if (f.Ranks.Any(r => string.Equals(r.Name, name, System.StringComparison.OrdinalIgnoreCase)))
                return OpResult.Fail("A rank with that name already exists.");
            // New ranks are inserted just above the lowest rank by default.
            var lowest = f.LowestRank;
            rank = new Rank
            {
                Id = Ids.New("rank"),
                Name = name,
                Order = lowest != null ? lowest.Order : f.Ranks.Count,
                Permissions = RankPermission.None,
                Salary = lowest != null ? lowest.Salary + 50 : 100,
            };
            if (lowest != null && !lowest.ReservedForPlayer) lowest.Order++;
            else rank.Order = f.Ranks.Count;
            f.Ranks.Add(rank);
            Normalize(f);
            return OpResult.Success("Rank '" + name + "' created.");
        }

        public static OpResult Rename(FactionState f, Rank rank, string newName)
        {
            newName = (newName ?? "").Trim();
            if (rank == null) return OpResult.Fail("No rank selected.");
            if (newName.Length == 0) return OpResult.Fail("Rank name cannot be empty.");
            if (newName.Length > MaxNameLength) newName = newName.Substring(0, MaxNameLength);
            if (f.Ranks.Any(r => r != rank && string.Equals(r.Name, newName, System.StringComparison.OrdinalIgnoreCase)))
                return OpResult.Fail("A rank with that name already exists.");
            string old = rank.Name;
            rank.Name = newName;
            return OpResult.Success("Renamed '" + old + "' to '" + newName + "'.");
        }

        /// <summary>Deletes a rank. Members holding it move to the next lower rank (or the next higher if it was the lowest).</summary>
        public static OpResult Delete(FactionState f, Rank rank)
        {
            if (rank == null) return OpResult.Fail("No rank selected.");
            if (rank.ReservedForPlayer) return OpResult.Fail("The top rank belongs to you and cannot be deleted.");
            if (f.Ranks.Count <= 2) return OpResult.Fail("A faction needs at least two ranks.");
            var ordered = f.Ranks.OrderBy(r => r.Order).ToList();
            int idx = ordered.IndexOf(rank);
            Rank fallback = idx + 1 < ordered.Count ? ordered[idx + 1] : ordered[idx - 1];
            if (fallback.ReservedForPlayer) return OpResult.Fail("Cannot delete: no rank available to move members into.");
            int moved = 0;
            foreach (var m in f.Members.Where(m => m.RankId == rank.Id))
            {
                m.RankId = fallback.Id;
                moved++;
            }
            f.Ranks.Remove(rank);
            Normalize(f);
            return OpResult.Success("Deleted rank '" + rank.Name + "'." + (moved > 0 ? " " + moved + " member(s) moved to " + fallback.Name + "." : ""));
        }

        /// <summary>Moves a rank up (delta -1) or down (+1). The player's top rank never moves.</summary>
        public static OpResult Move(FactionState f, Rank rank, int delta)
        {
            if (rank == null) return OpResult.Fail("No rank selected.");
            if (rank.ReservedForPlayer) return OpResult.Fail("The top rank is fixed.");
            Normalize(f);
            int target = rank.Order + delta;
            if (target <= 0 || target >= f.Ranks.Count) return OpResult.Fail("Cannot move further.");
            var other = f.Ranks.First(r => r.Order == target);
            other.Order = rank.Order;
            rank.Order = target;
            Normalize(f);
            return OpResult.Success(rank.Name + " moved " + (delta < 0 ? "up." : "down."));
        }

        public static void TogglePermission(Rank rank, RankPermission p)
        {
            if (rank == null || rank.ReservedForPlayer) return;
            if (rank.Has(p)) rank.Permissions &= ~p;
            else rank.Permissions |= p;
        }

        public static OpResult SetRank(FactionState f, Member m, Rank rank, int day)
        {
            if (m == null || rank == null) return OpResult.Fail("Invalid selection.");
            if (rank.ReservedForPlayer) return OpResult.Fail(rank.Name + " is reserved for you.");
            var old = f.GetRank(m.RankId);
            if (old == rank) return OpResult.Fail(m.ShortName + " already holds that rank.");
            bool promotion = old == null || rank.Order < old.Order;
            m.RankId = rank.Id;
            if (promotion)
            {
                m.Loyalty = MathEx.Pct(m.Loyalty + 8);
                m.Morale = MathEx.Pct(m.Morale + 10);
            }
            else
            {
                m.Loyalty = MathEx.Pct(m.Loyalty - (m.Personality == Personality.Ambitious || m.Personality == Personality.Rebellious ? 18 : 10));
                m.Morale = MathEx.Pct(m.Morale - 12);
            }
            return OpResult.Success(m.ShortName + (promotion ? " promoted to " : " demoted to ") + rank.Name + ".");
        }

        public static OpResult Promote(FactionState f, Member m, int day)
        {
            var current = f.GetRank(m.RankId) ?? f.LowestRank;
            var next = f.Ranks.Where(r => r.Order < current.Order && !r.ReservedForPlayer).OrderByDescending(r => r.Order).FirstOrDefault();
            if (next == null) return OpResult.Fail(m.ShortName + " already holds the highest rank available.");
            return SetRank(f, m, next, day);
        }

        public static OpResult Demote(FactionState f, Member m, int day)
        {
            var current = f.GetRank(m.RankId) ?? f.LowestRank;
            var next = f.Ranks.Where(r => r.Order > current.Order).OrderBy(r => r.Order).FirstOrDefault();
            if (next == null) return OpResult.Fail(m.ShortName + " is already at the lowest rank.");
            return SetRank(f, m, next, day);
        }

        public static OpResult ApplyTemplate(FactionState f, System.Collections.Generic.List<Rank> template)
        {
            var oldOrdered = f.Ranks.OrderBy(r => r.Order).ToList();
            var newOrdered = template.OrderBy(r => r.Order).ToList();
            // Map members by relative position in the hierarchy.
            foreach (var m in f.Members)
            {
                var old = f.GetRank(m.RankId);
                int idx = old != null ? oldOrdered.IndexOf(old) : oldOrdered.Count - 1;
                float rel = oldOrdered.Count <= 1 ? 1f : idx / (float)(oldOrdered.Count - 1);
                int newIdx = MathEx.Clamp((int)System.Math.Round(rel * (newOrdered.Count - 1)), 1, newOrdered.Count - 1);
                m.RankId = newOrdered[newIdx].Id;
            }
            f.Ranks = newOrdered;
            Normalize(f);
            return OpResult.Success("Rank structure replaced.");
        }

        public static int MemberCount(FactionState f, Rank r) => f.Members.Count(m => m.RankId == r.Id && m.Status != MemberStatus.Dead && m.Status != MemberStatus.Deserted);
    }
}
