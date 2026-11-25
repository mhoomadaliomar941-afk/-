using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrganizationEmpire.Core.Integration;
using OrganizationEmpire.Core.Model;

namespace OrganizationEmpire.Integration
{
    /// <summary>
    /// Optional integration with iFruitAddon2 (phone contacts library by Bob74, based on CamxxCore's iFruitAddon).
    /// It is not bundled or referenced at compile time: when the player has it installed, its public API
    /// (CustomiFruit, iFruitContact, the Answered event) is used through reflection to add two contacts:
    /// "Organization Empire" opens the command menu, "Call the crew" summons available members.
    /// Marked Experimental: written against the library's documented API, not verified in a running game.
    /// Any mismatch (renamed type/member) stops only this integration, with the reason in the log.
    /// </summary>
    internal sealed class PhoneContactModule : IntegrationModule
    {
        private readonly ModContext _mod;
        private readonly IntegrationHost _host;
        private object _ifruit;
        private MethodInfo _update, _close;
        private readonly List<object> _contacts = new List<object>();
        private readonly Dictionary<object, Action> _actions = new Dictionary<object, Action>();
        private readonly List<(EventInfo evt, object target, Delegate handler)> _subscriptions = new List<(EventInfo, object, Delegate)>();

        public PhoneContactModule(ModContext mod, IntegrationHost host) { _mod = mod; _host = host; }

        public override string Id => "phone";
        public override string Name => "Phone contacts (iFruitAddon2)";
        public override string Description => "Adds 'Organization Empire' (opens the menu) and 'Call the crew' (summons members) to the in-game phone when iFruitAddon2 is installed.";
        public override IntegrationLevel Level => IntegrationLevel.Experimental;
        public override IEnumerable<string> RequiresAny => new[] { "ifruitaddon2" };
        public override string Warning => "Experimental: uses iFruitAddon2's API through reflection and has not been verified in a running game. If it misbehaves it switches itself off; you can also turn it off here.";
        public override string Unavailable(ScanResult scan) => _mod.Config.Integrations.PhoneContact ? null : "Switched off in the ini (PhoneContact).";
        public override string Detail => _ifruit != null ? _contacts.Count + " contact(s)" : null;

        public override void Start(ScanResult scan)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, "iFruitAddon2", StringComparison.OrdinalIgnoreCase));
            if (asm == null) throw new InvalidOperationException("iFruitAddon2 is installed but not loaded by ScriptHookVDotNet yet.");
            var phoneType = Need(asm.GetType("iFruitAddon2.CustomiFruit"), "type CustomiFruit");
            var contactType = Need(asm.GetType("iFruitAddon2.iFruitContact"), "type iFruitContact");
            _ifruit = Need(Activator.CreateInstance(phoneType), "CustomiFruit()");
            _update = Need(phoneType.GetMethod("Update", Type.EmptyTypes), "CustomiFruit.Update()");
            _close = phoneType.GetMethod("Close", new[] { typeof(int) });
            var contactsProp = Need(phoneType.GetProperty("Contacts"), "CustomiFruit.Contacts");
            var contacts = Need(contactsProp.GetValue(_ifruit) as IList, "Contacts list");

            AddContact(contactType, contacts, "Organization Empire", () => { if (!_mod.Ui.IsOpen) _mod.Ui.Open(); });
            AddContact(contactType, contacts, "Call the crew", () =>
            {
                if (!_mod.HasFaction) return;
                var available = _mod.Faction.ActiveMembers.Take(_mod.Config.Spawning.MaxDeployedMembers);
                var r = _mod.Commands.OrderMany(available, OrderType.Follow);
                _mod.Ui.Notify.Push(r.Message, r.Ok ? EventSeverity.Good : EventSeverity.Warning);
            });
            _host.LogLine(Id, "added " + _contacts.Count + " phone contact(s) through iFruitAddon2 " + asm.GetName().Version);
        }

        private void AddContact(Type contactType, IList contacts, string name, Action action)
        {
            var contact = Need(Activator.CreateInstance(contactType, name), "iFruitContact(string)");
            SetIfPresent(contactType, contact, "DialTimeout", 2500);
            SetIfPresent(contactType, contact, "Active", true);
            var answered = Need(contactType.GetEvent("Answered"), "iFruitContact.Answered");
            // The handler takes 'object': delegate parameter contravariance lets it bind to the library's delegate type.
            var method = typeof(PhoneContactModule).GetMethod(nameof(OnAnswered), BindingFlags.NonPublic | BindingFlags.Instance);
            var handler = Delegate.CreateDelegate(answered.EventHandlerType, this, method);
            answered.AddEventHandler(contact, handler);
            _subscriptions.Add((answered, contact, handler));
            contacts.Add(contact);
            _contacts.Add(contact);
            _actions[contact] = action;
        }

        private void OnAnswered(object contact)
        {
            try
            {
                if (contact != null && _actions.TryGetValue(contact, out var action)) action();
                _close?.Invoke(_ifruit, new object[] { 1500 });
            }
            catch (Exception ex)
            {
                _host.LogLine(Id, "contact action failed: " + ex.Message);
            }
        }

        public override void Tick() => _update?.Invoke(_ifruit, null);

        public override void Stop()
        {
            foreach (var (evt, target, handler) in _subscriptions)
            {
                try { evt.RemoveEventHandler(target, handler); } catch (Exception) { /* library gone */ }
            }
            _subscriptions.Clear();
            try
            {
                var list = _ifruit?.GetType().GetProperty("Contacts")?.GetValue(_ifruit) as IList;
                if (list != null) foreach (var c in _contacts) list.Remove(c);
            }
            catch (Exception) { /* library gone */ }
            _contacts.Clear();
            _actions.Clear();
            _ifruit = null;
            _update = _close = null;
        }

        private static void SetIfPresent(Type t, object target, string prop, object value)
        {
            var p = t.GetProperty(prop);
            if (p != null && p.CanWrite && p.PropertyType.IsAssignableFrom(value.GetType())) p.SetValue(target, value);
        }

        private static T Need<T>(T value, string what) where T : class
        {
            if (value == null) throw new InvalidOperationException("iFruitAddon2 API not recognised (missing " + what + ").");
            return value;
        }
    }
}
