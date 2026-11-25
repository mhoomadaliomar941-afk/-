using System.Collections.Generic;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public sealed class SimNotification
    {
        public string Text;
        public EventSeverity Severity;
        public string Category;
    }

    /// <summary>Result of a player action: success flag plus a user-facing message.</summary>
    public struct OpResult
    {
        public bool Ok;
        public string Message;

        public static OpResult Success(string msg = "") => new OpResult { Ok = true, Message = msg };
        public static OpResult Fail(string msg) => new OpResult { Ok = false, Message = msg };
    }

    /// <summary>Everything a system needs: state, config, RNG and a notification sink.</summary>
    public sealed class SimContext
    {
        public GameState State;
        public ModConfig Config;
        public Rng Rng;
        public readonly List<SimNotification> Notifications = new List<SimNotification>();
        /// <summary>Runtime-only: a police mod is installed, so law enforcement is never recruitable (not saved to the ini).</summary>
        public bool ForbidLawRecruitment;

        public SimContext(GameState state, ModConfig config, Rng rng)
        {
            State = state;
            Config = config ?? new ModConfig();
            Rng = rng ?? Rng.FromTime();
        }

        public FactionState Faction => State.Faction;
        public WorldState World => State.World;
        public DifficultyProfile Difficulty => DifficultyProfile.For(Faction?.Difficulty ?? Model.Difficulty.Normal);

        /// <summary>Adds an entry to the faction event log and queues an on-screen notification.</summary>
        public void Report(string category, string text, EventSeverity severity, bool notify = true)
        {
            if (Faction != null) EventLog.Add(Faction, World, category, text, severity);
            if (notify) Notifications.Add(new SimNotification { Text = text, Severity = severity, Category = category });
        }

        public List<SimNotification> DrainNotifications()
        {
            var copy = new List<SimNotification>(Notifications);
            Notifications.Clear();
            return copy;
        }
    }

    public static class EventLog
    {
        public const int MaxEvents = 150;

        public static void Add(FactionState f, WorldState w, string category, string text, EventSeverity severity)
        {
            f.Events.Add(new FactionEvent { Day = w?.Day ?? 0, Hour = w?.Hour ?? 0, Category = category, Text = text, Severity = severity });
            if (f.Events.Count > MaxEvents) f.Events.RemoveRange(0, f.Events.Count - MaxEvents);
        }
    }
}
