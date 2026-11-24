using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.AI
{
    /// <summary>
    /// Built-in dialogue engine that needs no network or API key. It picks lines by faction style, topic,
    /// personality, loyalty and morale, and fills them with live game facts. Also used as a fallback when a remote provider fails.
    /// </summary>
    public sealed class OfflineDialogueProvider : IChatProvider
    {
        public string Name => "Offline";
        public bool IsRemote => false;

        public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            string last = request.Messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? "";
            var c = request.Context ?? new NpcContext { Name = "Stranger", FactionName = "the crew", PlayerTitle = "boss", FactionTier = "Crew" };
            int seed = Rng.StableHash(c.NpcId + "|" + last + "|" + request.Messages.Count);
            return Task.FromResult(ChatResult.Ok(Generate(c, last, new Rng(seed)), "offline"));
        }

        public static string Generate(NpcContext c, string playerText, Rng rng)
        {
            var topic = IntentDetector.DetectTopic(playerText, out string mentioned, c.Relationships.Select(r => r.Split(' ')[0]));
            string style = !c.IsMember ? "civ" : c.FactionIsGovernment ? "gov" : c.FactionType == FactionType.Mafia ? "maf" : c.FactionType == FactionType.Militia ? "mil" : "gang";
            bool cold = c.IsMember && c.Loyalty < 30;
            bool low = c.Morale < 35;

            string line;
            switch (topic)
            {
                case DialogueTopic.Report: line = Report(c, style, rng); break;
                case DialogueTopic.Morale: line = Pick(rng, MoraleLines(style, low, c.Personality)); break;
                case DialogueTopic.Loyalty: line = Pick(rng, LoyaltyLines(style, cold, c.Personality)); break;
                case DialogueTopic.Opinion: line = Opinion(c, mentioned, style, rng); break;
                case DialogueTopic.Orders:
                    line = c.IsMember
                        ? Pick(rng, OrderAck(style, cold)) + (string.IsNullOrEmpty(c.CurrentOrder) ? "" : " Right now I'm " + c.CurrentOrder + ".")
                        : Pick(rng, new[] { "I don't take orders from you.", "Who do you think you are?", "Not happening, pal." });
                    break;
                case DialogueTopic.Mission: line = Mission(c, style, rng); break;
                case DialogueTopic.Join: line = c.IsMember ? Pick(rng, new[] { "I'm already in, " + c.PlayerTitle + ". Where else would I be?", "You already got me. Use me." }) : JoinLine(c, rng); break;
                case DialogueTopic.Threat: line = Pick(rng, ThreatLines(style, c.Personality)); break;
                case DialogueTopic.Praise: line = Pick(rng, PraiseLines(style, cold)); break;
                case DialogueTopic.Farewell: line = Pick(rng, FarewellLines(style)); break;
                case DialogueTopic.Money: line = Pick(rng, MoneyLines(style, c)); break;
                case DialogueTopic.Rivals: line = Rivals(c, style, rng); break;
                case DialogueTopic.Greeting: line = Pick(rng, Greetings(style, cold, c)); break;
                default: line = Pick(rng, GenericLines(style, cold, c)); break;
            }
            line = Flavor(line, c, rng);
            return line.Replace("{title}", c.PlayerTitle ?? "boss").Replace("{faction}", c.FactionName ?? "the crew").Replace("{name}", c.Nickname ?? c.Name ?? "");
        }

        private static string Pick(Rng rng, IList<string> options) => options[rng.Next(options.Count)];

        private static string Flavor(string line, NpcContext c, Rng rng)
        {
            // Personality colors roughly one line in three, with a few variants, so it never becomes a verbal tic.
            if (!rng.Chance(0.33f)) return line;
            switch (c.Personality)
            {
                case Personality.Nervous: return rng.Pick(new[] { "Uh... " + line, line + " I-I mean, if that's okay.", "Okay, okay. " + line });
                case Personality.Sarcastic: return rng.Pick(new[] { line + " Living the dream, right?", "Oh, great question. " + line, line + " Shocking, I know." });
                case Personality.Aggressive: return rng.Pick(new[] { line + " Somebody's gonna bleed for it, watch.", line + " Just point me at 'em." });
                case Personality.Cowardly: return rng.Pick(new[] { line + " Just... let's not do anything crazy.", line + " Maybe we lay low a bit?" });
                case Personality.Ambitious: return rng.Pick(new[] { line + " And I'm ready for more responsibility.", line + " Give me more to handle and I'll prove it." });
                case Personality.Intelligent: return rng.Pick(new[] { line + " I've been thinking about our angles.", line + " The numbers back that up, by the way." });
                case Personality.Rebellious: return rng.Pick(new[] { line + " Not that anyone asks my opinion.", line + " For what it's worth." });
                case Personality.Calm: return rng.Pick(new[] { line, "Easy. " + line });
                default: return line;
            }
        }

        private static string Report(NpcContext c, string style, Rng rng)
        {
            if (!c.IsMember) return Pick(rng, new[] { "Report? I'm not one of your people.", "You want news, read the Daily Globe." });
            string snap = c.FactionSnapshot ?? "";
            string ev = c.RecentEvents.Count > 0 ? c.RecentEvents[c.RecentEvents.Count - 1] : null;
            switch (style)
            {
                case "mil": return "Sitrep, {title}: " + snap + "." + (ev != null ? " Latest: " + StripDay(ev) : "");
                case "maf": return "Here's how things stand, {title}. " + Capitalize(snap) + "." + (ev != null ? " And you should know - " + StripDay(ev) : "");
                case "gov": return "Briefing for the " + c.PlayerTitle + ": " + snap + ".";
                default: return "Real talk, {title}: " + snap + "." + (ev != null ? " Word on the street: " + StripDay(ev) : "");
            }
        }

        private static string StripDay(string e)
        {
            int i = e.IndexOf(':');
            return i > 0 && e.StartsWith("Day") ? e.Substring(i + 1).Trim() : e;
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string Opinion(NpcContext c, string mentioned, string style, Rng rng)
        {
            var rel = c.Relationships.FirstOrDefault(r => r.StartsWith(mentioned ?? "\0", StringComparison.OrdinalIgnoreCase));
            if (rel == null) return "I don't really know " + mentioned + " that well.";
            if (rel.Contains("close friend")) return mentioned + "? That's family. I'd trust them with my life.";
            if (rel.Contains("likes")) return mentioned + " is solid. Does the work, no complaints.";
            if (rel.Contains("hates")) return style == "maf" ? "With respect, {title}, keep " + mentioned + " away from me. That's all I'll say." : "Don't get me started on " + mentioned + ". One day that's gonna end badly.";
            if (rel.Contains("dislikes")) return mentioned + " gets on my nerves, but I keep it professional.";
            return mentioned + "? We're cool, I guess. Haven't had much to do with them.";
        }

        private static string Mission(NpcContext c, string style, Rng rng)
        {
            if (!string.IsNullOrEmpty(c.MissionContext))
                return style == "mil" ? "Current op: " + c.MissionContext + ". Unit is ready." : "We're on it: " + c.MissionContext + ".";
            switch (style)
            {
                case "mil": return Pick(rng, new[] { "No active operation, {title}. Check the mission board - the unit is ready.", "Standing by for tasking." });
                case "maf": return Pick(rng, new[] { "Nothing on the table right now. But there's always business, if you know where to look.", "Say the word and I'll take care of it." });
                default: return Pick(rng, new[] { "Ain't nothing moving right now. Find us a play and we're there.", "Point me at something, {title}. I'm bored." });
            }
        }

        private static string JoinLine(NpcContext c, Rng rng)
        {
            if (c.IsLawEnforcement) return "Are you trying to recruit a cop? You've got some nerve.";
            if (c.AffiliatedRival != null) return c.Willingness > 55 ? "Leave " + c.AffiliatedRival + "? ...Maybe. Show me you're the bigger player." : "I'm " + c.AffiliatedRival + " till I die. Get lost.";
            if (c.Willingness > 65) return Pick(rng, new[] { "Honestly? I've been looking for something like this. Tell me more.", "You might be speaking my language. What's in it for me?" });
            if (c.Willingness > 40) return Pick(rng, new[] { "I don't know... it's a big step. Convince me.", "Maybe. Depends what you're offering." });
            return Pick(rng, new[] { "No thanks, I've got enough problems.", "That sounds like a quick way to get killed. Pass." });
        }

        private static string Rivals(NpcContext c, string style, Rng rng)
        {
            string snap = c.FactionSnapshot ?? "";
            int i = snap.IndexOf("at war with", StringComparison.Ordinal);
            string war = i >= 0 ? snap.Substring(i).Split(';')[0] : null;
            switch (style)
            {
                case "mil": return war != null ? "We are " + war + ". Recommend we keep pressure on their weak points." : "No active hostilities. We should use the time to prepare.";
                case "maf": return war != null ? "We are " + war + ". Bad for business. Let's end it - one way or another." : "Things are quiet with the other families. Quiet is good. Quiet makes money.";
                default: return war != null ? "We " + war + " right now. Say the word and we ride." : "Ain't no beef right now. Won't last, though.";
            }
        }

        private static IList<string> Greetings(string style, bool cold, NpcContext c)
        {
            if (cold) return new[] { "Yeah? What do you want.", "...{title}.", "Oh. It's you." };
            switch (style)
            {
                case "mil": return new[] { "{title}. Standing by.", "Ready for orders, {title}.", "Sir. Unit is ready." };
                case "maf": return new[] { "{title}. Always a pleasure.", "Good to see you, {title}. What can I do for you?", "{title}. You need something taken care of?" };
                case "gov": return new[] { "{title}. At your service.", "Good day, {title}. The state is in good hands." };
                case "civ": return new[] { "Uh, hi?", "Can I help you?", "Do I know you?", "Hey. What's up?" };
                default: return new[] { "What's good, {title}?", "Yo! {title} in the building.", "Ayy, what's the word?" };
            }
        }

        private static IList<string> MoraleLines(string style, bool low, Personality p)
        {
            if (low)
            {
                switch (style)
                {
                    case "mil": return new[] { "Honestly, {title}, morale is low. The unit needs a win.", "Tired. Losses are weighing on everyone." };
                    case "maf": return new[] { "These are hard times for the family. People are worried.", "I'll be honest with you, the boys are on edge." };
                    default: return new[] { "Man, I ain't gonna lie, things feel rough right now.", "Everybody's stressed, {title}. We need a W." };
                }
            }
            switch (style)
            {
                case "mil": return new[] { "Morale is good, {title}. We believe in what we're doing.", "Fit and ready. The unit's spirits are high." };
                case "maf": return new[] { "Business is good, the family is strong. Can't complain.", "Never better, {title}. Things are running smooth." };
                case "civ": return new[] { "I'm fine, thanks. Long day.", "Could be better, could be worse." };
                default: return new[] { "I'm straight, {title}. We eating good.", "Feeling good, we on top right now." };
            }
        }

        private static IList<string> LoyaltyLines(string style, bool cold, Personality p)
        {
            if (cold) return new[] { "Loyalty goes both ways, {title}. Remember that.", "I'm here, ain't I? For now.", "Let's just say I've had better bosses." };
            switch (style)
            {
                case "mil": return new[] { "My loyalty is to you and the cause, {title}. Without question.", "I swore an oath. I keep my oaths." };
                case "maf": return new[] { "On my mother, {title}, I'd never betray this family.", "Loyalty is everything. You have mine." };
                default: return new[] { "I'm ride or die, {title}. You know that.", "{faction} for life. Never switching up." };
            }
        }

        private static IList<string> OrderAck(string style, bool cold)
        {
            if (cold) return new[] { "Fine. Whatever you say.", "Yeah, yeah. I heard you." };
            switch (style)
            {
                case "mil": return new[] { "Copy that, {title}.", "Understood. Executing on your confirmation.", "Roger. Ready when you give the signal." };
                case "maf": return new[] { "Consider it done, {title}.", "Whatever you need.", "Say no more." };
                default: return new[] { "Bet. Say less.", "I got you, {title}.", "Aight, on it." };
            }
        }

        private static IList<string> ThreatLines(string style, Personality p)
        {
            if (p == Personality.Cowardly || p == Personality.Nervous) return new[] { "Whoa, whoa! No need for that!", "Okay, okay! I hear you!" };
            if (p == Personality.Aggressive || p == Personality.Rebellious) return new[] { "You threatening me? Try it.", "Careful. I bite back." };
            return style == "maf" ? new[] { "There's no need for that kind of talk between us, {title}." } : new[] { "Easy. We're on the same side.", "Alright, message received." };
        }

        private static IList<string> PraiseLines(string style, bool cold)
        {
            if (cold) return new[] { "Words are cheap.", "Sure. Thanks, I guess." };
            switch (style)
            {
                case "mil": return new[] { "Thank you, {title}. Just doing my duty.", "Appreciated. The unit earned it." };
                case "maf": return new[] { "You honor me, {title}.", "Grazie. It's a privilege to serve the family." };
                default: return new[] { "Appreciate you, {title}! That's love.", "That's what I do, feel me?" };
            }
        }

        private static IList<string> FarewellLines(string style)
        {
            switch (style)
            {
                case "mil": return new[] { "{title}. Out.", "Understood. Standing by." };
                case "maf": return new[] { "Take care of yourself, {title}.", "Until next time." };
                case "civ": return new[] { "Uh, okay. Bye.", "See you around, I guess." };
                default: return new[] { "Aight, peace.", "One love, {title}." };
            }
        }

        private static IList<string> MoneyLines(string style, NpcContext c)
        {
            if (!c.IsMember) return new[] { "Money talks. What are we talking about?", "Everybody needs money in this city." };
            if (c.Morale < 40) return new[] { "Pay's been rough lately, {title}. People notice.", "The money ain't matching the risk right now." };
            switch (style)
            {
                case "mil": return new[] { "Pay is fair. Supplies matter more.", "We don't do this for money, {title}. But it helps." };
                case "maf": return new[] { "The envelopes have been generous. The family appreciates it." };
                default: return new[] { "Bread's been coming in, can't complain.", "We getting paid, that's what matters." };
            }
        }

        private static IList<string> GenericLines(string style, bool cold, NpcContext c)
        {
            if (cold) return new[] { "Hm.", "If you say so.", "Is that all?" };
            switch (style)
            {
                case "mil": return new[] { "Understood, {title}.", "Noted. Anything else?", "I'll pass it down the line." };
                case "maf": return new[] { "I hear you, {title}.", "That's interesting. Very interesting.", "You know best." };
                case "gov": return new[] { "Of course, {title}.", "I'll see that it's handled through the proper channels." };
                case "civ": return new[] { "Okay...?", "Sure, whatever you say.", "I don't really know what to tell you." };
                default: return new[] { "Facts.", "Real talk.", "I feel you, {title}.", "Say less." };
            }
        }
    }
}
