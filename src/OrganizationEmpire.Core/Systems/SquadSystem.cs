using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public static class SquadSystem
    {
        public const int MaxSquads = 10;
        public const int MaxSquadSize = 10;

        public static OpResult Create(FactionState f, string name, out Squad squad)
        {
            squad = null;
            if (f.Squads.Count >= MaxSquads) return OpResult.Fail("Maximum of " + MaxSquads + " squads.");
            var created = FactionFactory.NewSquad(f.Type, f.Squads.Count);
            if (!string.IsNullOrWhiteSpace(name)) created.Name = TextUtil.Truncate(name.Trim(), 32);
            while (f.Squads.Any(s => s.Name == created.Name)) created.Name += "+";
            f.Squads.Add(created);
            squad = created;
            return OpResult.Success("Created " + created.Name + ".");
        }

        public static OpResult Rename(FactionState f, Squad s, string name)
        {
            name = (name ?? "").Trim();
            if (s == null) return OpResult.Fail("No squad selected.");
            if (name.Length == 0) return OpResult.Fail("Name cannot be empty.");
            s.Name = TextUtil.Truncate(name, 32);
            return OpResult.Success("Squad renamed to " + s.Name + ".");
        }

        public static OpResult Delete(FactionState f, Squad s)
        {
            if (s == null) return OpResult.Fail("No squad selected.");
            foreach (var id in s.MemberIds)
            {
                var m = f.GetMember(id);
                if (m != null) m.SquadId = null;
            }
            foreach (var v in f.Vehicles.Where(v => v.AssignedSquadId == s.Id)) v.AssignedSquadId = null;
            f.Squads.Remove(s);
            return OpResult.Success(s.Name + " disbanded.");
        }

        public static OpResult AddMember(FactionState f, Squad s, Member m)
        {
            if (s == null || m == null) return OpResult.Fail("Invalid selection.");
            if (s.MemberIds.Contains(m.Id)) return OpResult.Fail(m.ShortName + " is already in " + s.Name + ".");
            if (s.MemberIds.Count >= MaxSquadSize) return OpResult.Fail(s.Name + " is full (" + MaxSquadSize + ").");
            var old = f.GetSquad(m.SquadId);
            if (old != null) RemoveMember(f, old, m);
            s.MemberIds.Add(m.Id);
            m.SquadId = s.Id;
            if (s.LeaderId == null) s.LeaderId = m.Id;
            return OpResult.Success(m.ShortName + " assigned to " + s.Name + ".");
        }

        public static OpResult RemoveMember(FactionState f, Squad s, Member m)
        {
            if (s == null || m == null) return OpResult.Fail("Invalid selection.");
            if (!s.MemberIds.Remove(m.Id)) return OpResult.Fail(m.ShortName + " is not in " + s.Name + ".");
            if (m.SquadId == s.Id) m.SquadId = null;
            if (s.LeaderId == m.Id) s.LeaderId = s.MemberIds.FirstOrDefault();
            return OpResult.Success(m.ShortName + " removed from " + s.Name + ".");
        }

        public static OpResult SetLeader(FactionState f, Squad s, Member m)
        {
            if (s == null || m == null) return OpResult.Fail("Invalid selection.");
            var rank = f.GetRank(m.RankId);
            if (rank != null && !rank.Has(RankPermission.LeadSquad) && !rank.Has(RankPermission.Leadership))
                return OpResult.Fail(m.ShortName + "'s rank (" + rank.Name + ") lacks the 'Lead squad' permission.");
            if (!s.MemberIds.Contains(m.Id))
            {
                var r = AddMember(f, s, m);
                if (!r.Ok) return r;
            }
            s.LeaderId = m.Id;
            m.Morale = MathEx.Pct(m.Morale + 5);
            return OpResult.Success(m.ShortName + " now leads " + s.Name + ".");
        }

        public static IEnumerable<Member> Members(FactionState f, Squad s)
        {
            foreach (var id in s.MemberIds)
            {
                var m = f.GetMember(id);
                if (m != null) yield return m;
            }
        }

        /// <summary>Removes dead/deserted members and dangling ids.</summary>
        public static void Cleanup(FactionState f)
        {
            foreach (var s in f.Squads)
            {
                s.MemberIds.RemoveAll(id =>
                {
                    var m = f.GetMember(id);
                    return m == null || m.Status == MemberStatus.Dead || m.Status == MemberStatus.Deserted;
                });
                if (s.LeaderId != null && !s.MemberIds.Contains(s.LeaderId)) s.LeaderId = s.MemberIds.FirstOrDefault();
            }
        }

        /// <summary>Combat value used in abstract battle resolution.</summary>
        public static float Strength(FactionState f, Squad s)
        {
            float total = 0;
            foreach (var m in Members(f, s))
            {
                if (!m.IsAvailable) continue;
                total += MemberSystem.CombatValue(m);
            }
            return total;
        }
    }
}
