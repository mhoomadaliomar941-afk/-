using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.AI
{
    /// <summary>Everything the model (or the offline engine) knows about the NPC and the situation.</summary>
    public sealed class NpcContext
    {
        public string NpcId;
        public string Name;
        public string Nickname;
        public bool IsMember;
        public bool Female;
        public int Age;
        public string Role;
        public string Rank;
        public Personality Personality;
        public Personality SecondaryTrait;
        public string Background;
        public string Hometown;
        public float Loyalty = 50, Morale = 50, Skill = 40, Discipline = 50;
        public string CurrentOrder;
        public string Squad;
        public string GovernmentRole;

        public FactionType FactionType;
        public string FactionName;
        public string FactionTier;
        public string FactionPersonality;
        public bool FactionIsGovernment;
        public string StateName;
        public string PlayerTitle;
        public string PlayerName;

        public string FactionSnapshot;
        public List<string> Relationships = new List<string>();
        public List<string> RecentEvents = new List<string>();
        public List<string> Memories = new List<string>();
        public string MemorySummary;
        public string MissionContext;
        public string Location;
        public string TimeOfDay;
        public string Situation;
        public bool ViaRadio;

        // Non-members
        public string AffiliatedRival;
        public float Willingness;
        public bool IsLawEnforcement;
    }

    public static class ContextBuilder
    {
        public static NpcContext ForMember(SimContext ctx, Member m, AiSettings ai)
        {
            var f = ctx.Faction;
            var c = Base(ctx);
            c.NpcId = m.Id;
            c.Name = m.FullName;
            c.Nickname = m.Nickname;
            c.IsMember = true;
            c.Female = m.Female;
            c.Age = m.Age;
            c.Role = TextUtil.Humanize(m.Role.ToString());
            c.Rank = f.GetRank(m.RankId)?.Name ?? "member";
            if (m.IsDeputy) c.Rank += " (deputy)";
            c.Personality = m.Personality;
            c.SecondaryTrait = m.SecondaryTrait;
            c.Background = m.Background;
            c.Hometown = m.Hometown;
            c.Loyalty = m.Loyalty;
            c.Morale = m.Morale;
            c.Skill = m.Skill;
            c.Discipline = m.Discipline;
            c.CurrentOrder = DescribeOrder(m.Order);
            var squad = f.GetSquad(m.SquadId);
            if (squad != null)
            {
                var leader = f.GetMember(squad.LeaderId);
                c.Squad = squad.Name + (leader == m ? " (you lead it)" : leader != null ? " (led by " + leader.ShortName + ")" : "");
            }
            c.GovernmentRole = m.GovernmentRole;
            foreach (var kv in m.Opinions.OrderByDescending(kv => Math.Abs(kv.Value)).Take(4))
            {
                var other = f.GetMember(kv.Key);
                if (other == null) continue;
                c.Relationships.Add(other.ShortName + " (" + OpinionWord(kv.Value) + ")");
            }
            var mem = ctx.State.Memories.TryGetValue(m.Id, out var nm) ? nm : null;
            if (mem != null)
            {
                c.MemorySummary = mem.Summary;
                c.Memories = MemoryStore.SelectForPrompt(mem, ai.MemoriesPerRequest, ctx.World.Day).Select(e => "Day " + e.Day + ": " + e.Text).ToList();
            }
            return c;
        }

        public static NpcContext ForCivilian(SimContext ctx, NpcProfile p, AiSettings ai)
        {
            var c = Base(ctx);
            c.NpcId = "npc:" + p.Key;
            c.Name = p.FullName;
            c.IsMember = false;
            c.Female = p.Female;
            c.Age = p.Age;
            c.Role = p.IsLawEnforcement ? "police officer" : p.AffiliatedRivalId != null ? "gang member" : "local resident";
            c.Personality = p.Personality;
            c.SecondaryTrait = p.SecondaryTrait;
            c.Background = p.Background;
            c.Hometown = p.Hometown;
            c.Willingness = p.Willingness;
            c.IsLawEnforcement = p.IsLawEnforcement;
            c.AffiliatedRival = ctx.World.GetRival(p.AffiliatedRivalId)?.Name;
            if (ctx.State.Memories.TryGetValue(c.NpcId, out var mem))
            {
                c.MemorySummary = mem.Summary;
                c.Memories = MemoryStore.SelectForPrompt(mem, ai.MemoriesPerRequest, ctx.World.Day).Select(e => e.Text).ToList();
            }
            return c;
        }

        private static NpcContext Base(SimContext ctx)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            var c = new NpcContext
            {
                FactionType = f?.Type ?? FactionType.StreetGang,
                FactionName = f?.Name ?? "",
                FactionTier = f != null ? FactionTypeInfo.TierName(f) : "",
                FactionPersonality = f?.Personality.ToString(),
                FactionIsGovernment = f?.IsGovernment ?? false,
                StateName = f?.Government?.StateName,
                PlayerTitle = f?.PlayerTitle ?? "Boss",
                TimeOfDay = (w.Hour < 6 ? "night" : w.Hour < 12 ? "morning" : w.Hour < 18 ? "afternoon" : "evening"),
            };
            if (f != null)
            {
                var wars = w.Rivals.Where(r => r.Active && r.Relation == RelationStatus.War).Select(r => r.Name).ToList();
                var allies = w.Rivals.Where(r => r.Active && r.Relation == RelationStatus.Alliance).Select(r => r.Name).ToList();
                var sb = new StringBuilder();
                sb.Append(f.LivingMemberCount).Append(" members, ");
                sb.Append(TerritorySystem.PlayerTerritoryCount(w)).Append(" territories, ");
                sb.Append("cash ").Append(TextUtil.Money(f.Resources.Cash)).Append(", ");
                sb.Append("reputation ").Append((int)f.Reputation).Append("/100, ");
                sb.Append("average morale ").Append((int)f.Morale).Append("/100, ");
                sb.Append(FactionTypeInfo.SuspicionLabel(f.Type).ToLowerInvariant()).Append(": ").Append(SuspicionSystem.Level(f).ToLowerInvariant());
                sb.Append(wars.Count > 0 ? "; at war with " + string.Join(", ", wars) : "; not at war");
                if (allies.Count > 0) sb.Append("; allied with ").Append(string.Join(", ", allies));
                if (!f.IsGovernment)
                {
                    var stages = TakeoverSystem.StagesFor(f.Type);
                    if (f.Takeover.Stage < stages.Count) sb.Append("; long-term campaign stage: ").Append(stages[f.Takeover.Stage].Name);
                }
                else sb.Append("; the faction now governs the ").Append(f.Government.StateName);
                c.FactionSnapshot = sb.ToString();
                c.RecentEvents = f.Events.Skip(Math.Max(0, f.Events.Count - 5)).Select(e => "Day " + e.Day + ": " + e.Text).ToList();
            }
            return c;
        }

        public static string DescribeOrder(OrderType o)
        {
            switch (o)
            {
                case OrderType.Follow: return "following the boss";
                case OrderType.Stay: return "holding position";
                case OrderType.Guard: return "guarding a position";
                case OrderType.MoveTo: return "moving to a location";
                case OrderType.ReturnToHQ: return "returning to headquarters";
                case OrderType.EnterVehicle: return "getting in a vehicle";
                case OrderType.Attack: return "engaging a target";
                case OrderType.DefendPlayer: return "protecting the boss";
                case OrderType.DefendLocation: return "defending a location";
                case OrderType.Standby: return "off duty / on standby";
                default: return "no specific orders";
            }
        }

        public static string OpinionWord(float v)
        {
            if (v >= 60) return "close friend";
            if (v >= 25) return "likes";
            if (v > -25) return "neutral";
            if (v > -60) return "dislikes";
            return "hates";
        }

        public static string LevelWord(float v)
        {
            if (v >= 85) return "very high";
            if (v >= 65) return "high";
            if (v >= 40) return "moderate";
            if (v >= 20) return "low";
            return "very low";
        }
    }

    public static class PromptBuilder
    {
        public static string StyleGuide(NpcContext c)
        {
            if (!c.IsMember)
            {
                if (c.IsLawEnforcement) return "You are a Los Santos law enforcement officer: guarded, procedural, suspicious of criminals, speaks plainly.";
                if (c.AffiliatedRival != null) return "You run with " + c.AffiliatedRival + ". You are proud of your crew, wary of outsiders and you talk like the streets you come from.";
                return "You are an ordinary Los Santos local: casual, a bit cynical about the city, wary of dangerous strangers, speaks like a normal person.";
            }
            if (c.FactionIsGovernment)
                return "You now serve in the state forces of the " + (c.StateName ?? "new state") + ". You speak with official confidence mixed with the habits of your past life in the " + c.FactionTier.ToLowerInvariant() + ". Address the leader as '" + c.PlayerTitle + "'.";
            switch (c.FactionType)
            {
                case FactionType.Mafia:
                    return "You are a member of an old-school crime family. Speak with measured, respectful, slightly old-fashioned confidence. Call the leader '" + c.PlayerTitle + "' or 'boss'. "
                         + "Talk about business indirectly ('take care of it', 'a favor', 'our friends', 'the family'). Value honor, respect, family and silence. "
                         + "Never use street-gang slang and never sound military.";
                case FactionType.Militia:
                    return "You are a member of a disciplined paramilitary organization. Speak concisely and with structure, use ranks, call the leader '" + c.PlayerTitle + "' or 'sir/ma'am'. "
                         + "Give reports like a short situation report (sitrep). You believe in the cause and talk about the mission, the unit and the people. "
                         + "Never use gang slang and never sound like a mafioso.";
                default:
                    return "You are a Los Santos street gang member. Talk like the block: casual slang, short punchy sentences, loyalty to the set, respect, beef with rival sets. "
                         + "Call the leader '" + c.PlayerTitle + "', 'big dog' or by title. Never sound formal or military.";
            }
        }

        public static string PersonalityText(Personality p)
        {
            switch (p)
            {
                case Personality.Loyal: return "loyal and devoted; would take a bullet for the people they trust";
                case Personality.Aggressive: return "aggressive and hot-headed; always ready for a fight";
                case Personality.Calm: return "calm and level-headed; rarely raises their voice";
                case Personality.Nervous: return "nervous and jumpy; worries out loud";
                case Personality.Ambitious: return "ambitious; wants rank, money and recognition";
                case Personality.Intelligent: return "sharp and analytical; notices details and plans ahead";
                case Personality.Sarcastic: return "sarcastic with a dry sense of humor";
                case Personality.Cowardly: return "cowardly; avoids danger and makes excuses";
                case Personality.Disciplined: return "disciplined; follows procedure and respects the chain of command";
                case Personality.Rebellious: return "rebellious; questions orders and hates being told what to do";
                default: return p.ToString().ToLowerInvariant();
            }
        }

        public static string BuildSystemPrompt(NpcContext c, AiSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("You are roleplaying ").Append(c.Name);
            if (!string.IsNullOrEmpty(c.Nickname)) sb.Append(" (\"").Append(c.Nickname).Append("\")");
            sb.Append(", a character in the fictional world of Grand Theft Auto V (Los Santos and Blaine County, San Andreas). ");
            sb.Append("You are talking with the player character");
            if (c.IsMember) sb.Append(", who leads your ").Append(c.FactionTier.ToLowerInvariant()).Append(" \"").Append(c.FactionName).Append("\" as ").Append(c.PlayerTitle);
            sb.Append(c.ViaRadio ? ", over the phone/radio." : ", face to face.").Append("\n\n");

            sb.Append("VOICE: ").Append(StyleGuide(c)).Append("\n");
            sb.Append("PERSONALITY: mainly ").Append(PersonalityText(c.Personality));
            if (c.SecondaryTrait != c.Personality) sb.Append(", also ").Append(PersonalityText(c.SecondaryTrait));
            sb.Append(".\n\n");

            sb.Append("FACTS ABOUT YOU:\n");
            sb.Append("- ").Append(c.Female ? "Woman" : "Man").Append(", age ").Append(c.Age);
            if (!string.IsNullOrEmpty(c.Hometown)) sb.Append(", from ").Append(c.Hometown);
            sb.Append(".\n");
            if (!string.IsNullOrEmpty(c.Background)) sb.Append("- Background: ").Append(c.Background).Append("\n");
            if (c.IsMember)
            {
                sb.Append("- Rank: ").Append(c.Rank).Append("; role: ").Append(c.Role).Append(".\n");
                if (!string.IsNullOrEmpty(c.GovernmentRole)) sb.Append("- Government post: ").Append(c.GovernmentRole).Append(".\n");
                if (!string.IsNullOrEmpty(c.Squad)) sb.Append("- Squad: ").Append(c.Squad).Append(".\n");
                sb.Append("- Loyalty to the leader: ").Append(ContextBuilder.LevelWord(c.Loyalty)).Append(" (").Append((int)c.Loyalty).Append("/100). ");
                sb.Append("Morale: ").Append(ContextBuilder.LevelWord(c.Morale)).Append(" (").Append((int)c.Morale).Append("/100). ");
                sb.Append("Combat skill: ").Append(ContextBuilder.LevelWord(c.Skill)).Append(". Discipline: ").Append(ContextBuilder.LevelWord(c.Discipline)).Append(".\n");
                sb.Append("- Current orders: ").Append(c.CurrentOrder ?? "none").Append(".\n");
                if (c.Relationships.Count > 0) sb.Append("- Feelings about other members: ").Append(string.Join("; ", c.Relationships)).Append(".\n");
                if (!string.IsNullOrEmpty(c.FactionSnapshot)) sb.Append("- Faction status: ").Append(c.FactionSnapshot).Append(".\n");
            }
            else
            {
                sb.Append("- You are NOT a member of the player's group \"").Append(c.FactionName).Append("\" (a ").Append(c.FactionTier.ToLowerInvariant()).Append(").\n");
                if (c.AffiliatedRival != null) sb.Append("- You belong to ").Append(c.AffiliatedRival).Append(".\n");
                sb.Append("- Your openness to joining a group like theirs: ").Append(ContextBuilder.LevelWord(c.Willingness)).Append(".\n");
            }
            if (!string.IsNullOrEmpty(c.Location) || !string.IsNullOrEmpty(c.Situation))
                sb.Append("- Right now: ").Append(c.TimeOfDay).Append(", ").Append(c.Location ?? "somewhere in Los Santos").Append(string.IsNullOrEmpty(c.Situation) ? "" : ", " + c.Situation).Append(".\n");
            if (!string.IsNullOrEmpty(c.MissionContext)) sb.Append("- Active operation: ").Append(c.MissionContext).Append(".\n");
            if (c.RecentEvents.Count > 0 && c.IsMember)
            {
                sb.Append("\nRECENT EVENTS (you know about these):\n");
                foreach (var e in c.RecentEvents) sb.Append("- ").Append(e).Append("\n");
            }
            if (!string.IsNullOrEmpty(c.MemorySummary) || c.Memories.Count > 0)
            {
                sb.Append("\nYOUR MEMORIES OF PAST INTERACTIONS:\n");
                if (!string.IsNullOrEmpty(c.MemorySummary)) sb.Append("- Older: ").Append(c.MemorySummary).Append("\n");
                foreach (var m in c.Memories) sb.Append("- ").Append(m).Append("\n");
            }

            sb.Append("\nRULES:\n");
            sb.Append("1. Stay fully in character. Never mention being an AI, a model, a prompt, or a video game.\n");
            sb.Append("2. Keep replies short: usually 1-3 sentences and never more than ").Append(s.MaxReplyWords).Append(" words. Reports may be up to 5 short lines.\n");
            sb.Append("3. This is fiction inside a video game. Keep any crime or violence vague and cinematic. Never give real-world instructions or specifics (weapons, drugs, chemistry, hacking, evading police, attacks). If asked, deflect in character.\n");
            sb.Append("4. You cannot act on your own; the game executes orders only after the player confirms them. You may acknowledge, question or push back on orders in character.\n");
            sb.Append("5. Do not contradict the facts above. You may invent small personal details consistent with your background.\n");
            sb.Append("6. Let your loyalty and morale color your attitude (low loyalty = cold, evasive or insolent; high = warm and committed).\n");
            sb.Append("7. Optional: end with ONE tag if appropriate - [[MOOD:+1]] or [[MOOD:-1]] if the player's words genuinely moved your feelings toward them");
            if (!c.IsMember) sb.Append(", or [[WILLING]] if the player has genuinely convinced you to consider joining");
            sb.Append(". Never explain tags.\n");
            sb.Append("8. Reply in the language the player writes in. Plain text only, no markdown, no stage directions longer than a few words.\n");
            return sb.ToString();
        }

        public static ChatRequest BuildRequest(NpcContext ctx, IEnumerable<ChatTurn> history, string playerMessage, AiSettings s)
        {
            var req = new ChatRequest
            {
                SystemPrompt = BuildSystemPrompt(ctx, s),
                MaxTokens = s.MaxResponseTokens,
                Temperature = s.Provider == AiProviderKind.Anthropic ? -1 : s.Temperature,
                Context = ctx,
            };
            var turns = history.ToList();
            int keep = Math.Max(0, s.MemoryTurns * 2);
            foreach (var t in turns.Skip(Math.Max(0, turns.Count - keep)))
            {
                req.Messages.Add(new ChatMessage(t.Speaker == "player" ? ChatRole.User : ChatRole.Assistant, t.Text));
            }
            req.Messages.Add(new ChatMessage(ChatRole.User, playerMessage));
            return req;
        }
    }

    public sealed class ParsedReply
    {
        public string Text;
        public int MoodDelta;
        public bool Willing;
    }

    public static class ResponseParser
    {
        private static readonly Regex TagRegex = new Regex(@"\[\[\s*([A-Za-z_]+)\s*(?::\s*([+\-]?\d+))?\s*\]\]", RegexOptions.Compiled);
        private static readonly Regex Asterisks = new Regex(@"\*([^*\n]{1,60})\*", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);

        public static ParsedReply Parse(string raw, string npcName, int maxChars = 700)
        {
            var r = new ParsedReply();
            string text = raw ?? "";
            text = TagRegex.Replace(text, m =>
            {
                string tag = m.Groups[1].Value.ToUpperInvariant();
                if (tag == "MOOD" && int.TryParse(m.Groups[2].Value, out int d)) r.MoodDelta = Math.Sign(d);
                else if (tag == "WILLING") r.Willing = true;
                return "";
            });
            // Strip markdown emphasis -> stage directions in parentheses.
            text = Asterisks.Replace(text, m => "(" + m.Groups[1].Value.Trim() + ")");
            text = text.Replace("**", "").Replace("__", "").Replace("#", "");
            text = text.Trim();
            // Remove "Name:" prefixes models sometimes add.
            if (!string.IsNullOrEmpty(npcName))
            {
                foreach (var prefix in new[] { npcName + ":", npcName.Split(' ')[0] + ":" })
                {
                    if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) text = text.Substring(prefix.Length).Trim();
                }
            }
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"' && text.Count(ch => ch == '"') == 2) text = text.Substring(1, text.Length - 2);
            text = Spaces.Replace(text, " ");
            var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
            text = string.Join("\n", lines);
            text = TextUtil.SafeForGame(text);
            if (text.Length > maxChars)
            {
                int cut = text.LastIndexOfAny(new[] { '.', '!', '?' }, maxChars - 1);
                text = cut > maxChars / 2 ? text.Substring(0, cut + 1) : text.Substring(0, maxChars) + "...";
            }
            if (text.Length == 0) text = "...";
            r.Text = text;
            return r;
        }
    }

    public enum DialogueTopic { Greeting, Report, Morale, Loyalty, Opinion, Orders, Mission, Join, Threat, Praise, Farewell, Money, Rivals, Generic }

    /// <summary>
    /// Local, deterministic understanding of the player's message. Used to suggest commands (which the player must confirm)
    /// and to drive the offline dialogue engine. The AI's own text is never parsed into commands.
    /// </summary>
    public static class IntentDetector
    {
        private static bool Has(string t, params string[] words) => words.Any(w => Regex.IsMatch(t, @"\b" + Regex.Escape(w) + @"\b"));

        public static OrderType? DetectOrder(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            if (Has(t, "follow me", "come with me", "with me", "let's go", "lets go", "on me", "fall in")) return OrderType.Follow;
            if (Has(t, "stay here", "wait here", "hold position", "stay put", "don't move", "hold here")) return OrderType.Stay;
            if (Has(t, "guard this", "guard here", "watch this place", "stand guard", "keep watch")) return OrderType.Guard;
            if (Has(t, "go to hq", "go back to hq", "return to base", "go home", "back to base", "head back", "return to hq")) return OrderType.ReturnToHQ;
            if (Has(t, "get in the car", "get in", "in the car", "get in the vehicle", "mount up", "hop in")) return OrderType.EnterVehicle;
            if (Has(t, "get out", "out of the car", "dismount", "exit the vehicle")) return OrderType.ExitVehicle;
            if (Has(t, "protect me", "cover me", "watch my back", "defend me")) return OrderType.DefendPlayer;
            if (Has(t, "take him out", "attack", "kill him", "light him up", "engage", "take them out", "open fire")) return OrderType.Attack;
            return null;
        }

        public static DialogueTopic DetectTopic(string text, out string mentionedName, IEnumerable<string> knownNames = null)
        {
            mentionedName = null;
            string t = (text ?? "").ToLowerInvariant();
            if (knownNames != null)
            {
                foreach (var n in knownNames)
                {
                    if (!string.IsNullOrWhiteSpace(n) && n.Length >= 3 && t.Contains(n.ToLowerInvariant())) { mentionedName = n; return DialogueTopic.Opinion; }
                }
            }
            if (DetectOrder(t) != null) return DialogueTopic.Orders;
            if (Has(t, "report", "status", "sitrep", "update", "what's happening", "whats happening", "news")) return DialogueTopic.Report;
            if (Has(t, "morale", "how are you", "how you doing", "how's it going", "feeling", "you good", "you ok", "how are things")) return DialogueTopic.Morale;
            if (Has(t, "loyal", "trust", "betray", "rat", "snitch", "faithful")) return DialogueTopic.Loyalty;
            if (Has(t, "join", "recruit", "work for me", "roll with", "become one of us", "sign up", "enlist")) return DialogueTopic.Join;
            if (Has(t, "mission", "job", "operation", "plan", "next move", "assignment")) return DialogueTopic.Mission;
            if (Has(t, "orders", "what are you doing", "what's your task", "your orders")) return DialogueTopic.Orders;
            if (Has(t, "rival", "enemy", "enemies", "war", "ballas", "vagos", "families", "lost", "cartel", "triads", "merryweather")) return DialogueTopic.Rivals;
            if (Has(t, "money", "cash", "pay", "paid", "salary")) return DialogueTopic.Money;
            if (Has(t, "or else", "i'll kill", "ill kill", "you're dead", "watch yourself", "threat")) return DialogueTopic.Threat;
            if (Has(t, "good job", "well done", "proud", "thank", "thanks", "nice work", "great work")) return DialogueTopic.Praise;
            if (Has(t, "bye", "later", "see you", "that's all", "dismissed")) return DialogueTopic.Farewell;
            if (Has(t, "hello", "hey", "yo", "sup", "hi", "good morning", "evening")) return DialogueTopic.Greeting;
            return DialogueTopic.Generic;
        }
    }
}
