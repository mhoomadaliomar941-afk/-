using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.AI
{
    /// <summary>Per-NPC long-term memory with configurable limits; older memories collapse into a short summary.</summary>
    public static class MemoryStore
    {
        public const int MaxSummaryChars = 700;
        public const int MaxDialogueTurns = 40;

        public static NpcMemory Get(GameState s, string npcId, string name, bool create = true)
        {
            if (s.Memories.TryGetValue(npcId, out var m)) { if (name != null) m.Name = name; return m; }
            if (!create) return null;
            m = new NpcMemory { NpcId = npcId, Name = name };
            s.Memories[npcId] = m;
            TrimNpcCount(s);
            return m;
        }

        /// <summary>Non-member memories are capped so wandering around doesn't grow the save forever.</summary>
        private static void TrimNpcCount(GameState s)
        {
            var civs = s.Memories.Values.Where(m => m.NpcId.StartsWith("npc:", StringComparison.Ordinal)).OrderBy(m => m.LastTalkedDay).ToList();
            while (civs.Count > 30)
            {
                s.Memories.Remove(civs[0].NpcId);
                civs.RemoveAt(0);
            }
        }

        public static void AddEntry(GameState s, string npcId, string name, MemoryKind kind, string text, float importance, int day, int maxEntries)
        {
            if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(text) || maxEntries <= 0) return;
            var m = Get(s, npcId, name);
            m.Entries.Add(new MemoryEntry { Day = day, Kind = kind, Text = TextUtil.Truncate(text, 200), Importance = MathEx.Clamp01(importance) });
            Trim(m, maxEntries, day);
        }

        public static void AddDialogue(GameState s, string npcId, string name, string speaker, string text, int day)
        {
            var m = Get(s, npcId, name);
            m.Dialogue.Add(new ChatTurn { Speaker = speaker, Text = TextUtil.Truncate(text, 600), Day = day });
            m.LastTalkedDay = day;
            if (m.Dialogue.Count > MaxDialogueTurns)
            {
                var old = m.Dialogue.Take(m.Dialogue.Count - MaxDialogueTurns).ToList();
                m.Dialogue.RemoveRange(0, old.Count);
                var topics = old.Where(t => t.Speaker == "player").Select(t => TextUtil.Truncate(t.Text, 50)).ToList();
                if (topics.Count > 0) AppendSummary(m, "Talked about: " + string.Join(" / ", topics));
            }
        }

        public static void Trim(NpcMemory m, int maxEntries, int day)
        {
            if (m.Entries.Count <= maxEntries) return;
            // Score = importance weighted by recency. Lowest scores are summarized and dropped.
            var scored = m.Entries.Select(e => new { e, score = e.Importance * 2f + 1f / (1f + Math.Max(0, day - e.Day) * 0.3f) })
                                  .OrderBy(x => x.score).ToList();
            int remove = m.Entries.Count - maxEntries;
            var dropped = scored.Take(remove).Select(x => x.e).ToList();
            foreach (var e in dropped) m.Entries.Remove(e);
            AppendSummary(m, string.Join("; ", dropped.Select(e => TextUtil.Truncate(e.Text, 60))));
        }

        private static void AppendSummary(NpcMemory m, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            m.Summary = string.IsNullOrEmpty(m.Summary) ? text : m.Summary + "; " + text;
            if (m.Summary.Length > MaxSummaryChars) m.Summary = "..." + m.Summary.Substring(m.Summary.Length - MaxSummaryChars + 3);
        }

        public static List<MemoryEntry> SelectForPrompt(NpcMemory m, int count, int day)
        {
            if (m == null || count <= 0) return new List<MemoryEntry>();
            return m.Entries
                .OrderByDescending(e => e.Importance * 2f + 1f / (1f + Math.Max(0, day - e.Day) * 0.3f))
                .Take(count)
                .OrderBy(e => e.Day)
                .ToList();
        }

        public static void Clear(GameState s, string npcId) => s.Memories.Remove(npcId);

        public static void ClearAll(GameState s) => s.Memories.Clear();

        public static void ClearDialogueOnly(GameState s)
        {
            foreach (var m in s.Memories.Values) m.Dialogue.Clear();
        }
    }

    public enum ConversationState { Idle, Waiting, Error }

    public sealed class Conversation
    {
        public string NpcId;
        public string NpcName;
        public bool IsMember;
        public bool ViaRadio;
        public ConversationState State = ConversationState.Idle;
        public string LastError;
        public string LastProvider;
        public OrderType? SuggestedOrder;
        public readonly List<ChatTurn> Visible = new List<ChatTurn>();
        internal CancellationTokenSource Cts;
    }

    /// <summary>A completed AI request waiting to be applied on the game thread.</summary>
    public sealed class DialogueResult
    {
        public Conversation Conversation;
        public string PlayerText;
        public ParsedReply Reply;
        public ChatResult Raw;
        public bool UsedFallback;
    }

    /// <summary>
    /// Runs AI requests off the game thread. Results are queued and only applied when the game calls
    /// <see cref="Pump"/> from its tick, so game state is never touched from a background thread.
    /// </summary>
    public sealed class DialogueService
    {
        private readonly Func<AiSettings> _settings;
        private readonly Func<IChatProvider> _providerFactory;
        private readonly ConcurrentQueue<DialogueResult> _completed = new ConcurrentQueue<DialogueResult>();
        private readonly OfflineDialogueProvider _offline = new OfflineDialogueProvider();
        private DateTime _lastRequestUtc = DateTime.MinValue;
        private int _inFlight;

        public DialogueService(Func<AiSettings> settings, Func<IChatProvider> providerFactory)
        {
            _settings = settings;
            _providerFactory = providerFactory;
        }

        public int InFlight => _inFlight;

        public Conversation Open(GameState state, string npcId, string name, bool isMember, bool viaRadio)
        {
            var c = new Conversation { NpcId = npcId, NpcName = name, IsMember = isMember, ViaRadio = viaRadio };
            if (state.Memories.TryGetValue(npcId, out var mem))
            {
                foreach (var t in mem.Dialogue.Skip(Math.Max(0, mem.Dialogue.Count - 12))) c.Visible.Add(t);
            }
            return c;
        }

        /// <summary>Queues a player message. Returns false (with a reason) if rate-limited or already waiting.</summary>
        public bool Send(Conversation conv, string playerText, NpcContext context, GameState state, out string error)
        {
            error = null;
            var s = _settings();
            playerText = (playerText ?? "").Trim();
            if (playerText.Length == 0) { error = "Type something first."; return false; }
            if (playerText.Length > 400) playerText = playerText.Substring(0, 400);
            if (conv.State == ConversationState.Waiting) { error = "Still waiting for a reply."; return false; }
            double since = (DateTime.UtcNow - _lastRequestUtc).TotalSeconds;
            if (since < s.MinSecondsBetweenRequests) { error = "Slow down a little."; return false; }
            if (_inFlight >= 2) { error = "Too many conversations at once."; return false; }

            var history = state.Memories.TryGetValue(conv.NpcId, out var mem) ? mem.Dialogue.ToList() : new List<ChatTurn>();
            context.ViaRadio = conv.ViaRadio;
            var request = PromptBuilder.BuildRequest(context, history, playerText, s);
            conv.Visible.Add(new ChatTurn { Speaker = "player", Text = TextUtil.SafeForGame(playerText), Day = state.World.Day });
            conv.SuggestedOrder = conv.IsMember ? IntentDetector.DetectOrder(playerText) : null;
            conv.State = ConversationState.Waiting;
            conv.LastError = null;
            conv.Cts = new CancellationTokenSource();
            _lastRequestUtc = DateTime.UtcNow;
            Interlocked.Increment(ref _inFlight);

            IChatProvider provider;
            try { provider = _providerFactory(); }
            catch (Exception ex) { Log.Error("Could not create AI provider", ex); provider = _offline; }
            conv.LastProvider = provider.Name;
            var token = conv.Cts.Token;
            string npcName = conv.NpcName;

            Task.Run(async () =>
            {
                var result = new DialogueResult { Conversation = conv, PlayerText = playerText };
                try
                {
                    ChatResult raw = await provider.CompleteAsync(request, token).ConfigureAwait(false);
                    if (!raw.Success && provider.IsRemote && raw.Error != AiErrorKind.Cancelled)
                    {
                        Log.Warn("AI request failed (" + provider.Name + "): " + raw.Error + " " + raw.ErrorMessage);
                        result.UsedFallback = true;
                        var fb = await _offline.CompleteAsync(request, token).ConfigureAwait(false);
                        fb.Error = raw.Error;
                        fb.ErrorMessage = raw.ErrorMessage;
                        fb.StatusCode = raw.StatusCode;
                        raw = new ChatResult { Success = true, Text = fb.Text, Error = raw.Error, ErrorMessage = raw.UserFacingError(), StatusCode = raw.StatusCode };
                    }
                    else if (raw.Success)
                    {
                        Log.Debug("AI reply from " + provider.Name + " in " + raw.LatencyMs + " ms.");
                    }
                    result.Raw = raw;
                    if (raw.Success) result.Reply = ResponseParser.Parse(raw.Text, npcName);
                }
                catch (Exception ex)
                {
                    Log.Error("AI request crashed", ex);
                    result.Raw = ChatResult.Fail(AiErrorKind.Unknown, ex.Message);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
                _completed.Enqueue(result);
            });
            return true;
        }

        public void Cancel(Conversation conv)
        {
            try { conv?.Cts?.Cancel(); } catch { /* ignore */ }
        }

        /// <summary>Applies finished requests. Call once per game tick on the game thread.</summary>
        public void Pump(GameState state, Action<DialogueResult> onApplied)
        {
            int guard = 0;
            while (guard++ < 8 && _completed.TryDequeue(out var r))
            {
                var conv = r.Conversation;
                conv.State = ConversationState.Idle;
                if (r.Raw == null || !r.Raw.Success || r.Reply == null)
                {
                    conv.State = ConversationState.Error;
                    conv.LastError = r.Raw?.UserFacingError() ?? "Unknown error.";
                    conv.Visible.Add(new ChatTurn { Speaker = "system", Text = conv.LastError, Day = state.World.Day });
                    onApplied?.Invoke(r);
                    continue;
                }
                if (r.UsedFallback)
                {
                    conv.LastError = r.Raw.ErrorMessage;
                    conv.Visible.Add(new ChatTurn { Speaker = "system", Text = "[AI unavailable - offline reply] " + r.Raw.ErrorMessage, Day = state.World.Day });
                }
                conv.Visible.Add(new ChatTurn { Speaker = "npc", Text = r.Reply.Text, Day = state.World.Day });
                if (conv.Visible.Count > 60) conv.Visible.RemoveRange(0, conv.Visible.Count - 60);
                var s = _settings();
                if (s.PersistentMemory || !conv.NpcId.StartsWith("npc:", StringComparison.Ordinal))
                {
                    MemoryStore.AddDialogue(state, conv.NpcId, conv.NpcName, "player", r.PlayerText, state.World.Day);
                    MemoryStore.AddDialogue(state, conv.NpcId, conv.NpcName, "npc", r.Reply.Text, state.World.Day);
                }
                onApplied?.Invoke(r);
            }
        }

        public Task<ChatResult> TestConnectionAsync(CancellationToken ct)
        {
            var provider = _providerFactory();
            var req = new ChatRequest
            {
                SystemPrompt = "You are a connection test for a video game mod. Reply with the single word: ready",
                MaxTokens = 16,
                Temperature = -1,
                Context = new NpcContext { Name = "Test", PlayerTitle = "boss", FactionName = "test", FactionTier = "Crew" },
            };
            req.Messages.Add(new ChatMessage(ChatRole.User, "Connection test. Reply with: ready"));
            return Task.Run(() => provider.CompleteAsync(req, ct), ct);
        }
    }

    public interface IKeyProtector
    {
        bool Available { get; }
        string Protect(string plain);
        string Unprotect(string protectedValue);
    }

    /// <summary>Resolves the configured API key: plain text, env:VARIABLE, or dpapi:... (Windows user-bound encryption).</summary>
    public static class KeyResolver
    {
        public static string Resolve(string configured, IKeyProtector protector, out string source)
        {
            source = "none";
            if (string.IsNullOrWhiteSpace(configured)) return "";
            string v = configured.Trim();
            if (v.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                source = "environment variable";
                string name = v.Substring(4).Trim();
                string val = Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) ?? "";
                SecretRedactor.Register(val);
                return val;
            }
            if (v.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase))
            {
                source = "encrypted (DPAPI)";
                if (protector == null || !protector.Available) return "";
                try
                {
                    string val = protector.Unprotect(v.Substring(6));
                    SecretRedactor.Register(val);
                    return val;
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not decrypt API key: " + ex.Message);
                    return "";
                }
            }
            source = "plain text in OrganizationEmpire.ini";
            SecretRedactor.Register(v);
            return v;
        }
    }
}
