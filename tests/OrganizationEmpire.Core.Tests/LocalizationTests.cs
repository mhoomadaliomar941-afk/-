using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Localization;
using Xunit;

namespace OrganizationEmpire.Core.Tests
{
    public class ArabicShapingTests
    {
        [Fact]
        public void JoinsLettersByPosition()
        {
            // meem initial, hah medial, meem medial, dal final
            Assert.Equal("ﻣﺤﻤﺪ", ArabicText.Shape("محمد"));
            // ain initial, dad medial, waw final
            Assert.Equal("ﻋﻀﻮ", ArabicText.Shape("عضو"));
        }

        [Fact]
        public void LamAlefBecomesLigatureAndBreaksJoining()
        {
            // seen initial, lam-alef final ligature, meem isolated (the ligature never joins forward)
            Assert.Equal("ﺳﻼﻡ", ArabicText.Shape("سلام"));
            Assert.Equal("ﻻ", ArabicText.Shape("لا"));
            Assert.Equal("ﻷ", ArabicText.Shape("لأ"));
        }

        [Fact]
        public void RightJoiningLettersStartNewForms()
        {
            // alef isolated, then noon initial, dad medial, meem final
            Assert.Equal("ﺍﻧﻀﻢ", ArabicText.Shape("انضم"));
            // waw final after beh... beh initial, waw final, beh isolated
            Assert.Equal("ﺑﻮﺏ", ArabicText.Shape("بوب"));
        }

        [Fact]
        public void MarksAreDroppedAndDoNotBreakJoining()
        {
            Assert.Equal(ArabicText.Shape("محمد"), ArabicText.Shape("مُحَمَّد"));
        }

        [Fact]
        public void NonArabicTextIsUntouched()
        {
            Assert.Equal("Big Mike 12", ArabicText.Shape("Big Mike 12"));
            Assert.Equal("", ArabicText.Shape(null));
        }

        [Fact]
        public void EveryShapedFormCanBeDescribedForGlyphGeneration()
        {
            foreach (char c in ArabicText.AllShapedForms())
            {
                Assert.True(ArabicText.TryDescribeForm(c, out string baseText, out int form), ((int)c).ToString("X4"));
                Assert.False(string.IsNullOrEmpty(baseText));
                Assert.InRange(form, 0, 3);
            }
        }
    }

    public class BidiTests
    {
        [Fact]
        public void ArabicRunIsReversedAndNumbersKeepOrder()
        {
            Assert.Equal("12 ﺩﺪﻋ", ArabicText.ToVisual("عدد 12", true));
        }

        [Fact]
        public void LatinNameInsideArabicSentenceKeepsReadingOrder()
        {
            Assert.Equal("Big Mike ﻢﻀﻧﺍ", ArabicText.ToVisual("انضم Big Mike", true));
        }

        [Fact]
        public void BracketsAreMirroredInRightToLeftText()
        {
            string v = ArabicText.ToVisual("(محمد)", true);
            Assert.Equal('(', v[0]);
            Assert.Equal(')', v[v.Length - 1]);
            Assert.Equal("ﺪﻤﺤﻣ", v.Substring(1, 4));
        }

        [Fact]
        public void MoneyAndPercentStayTogether()
        {
            // "$1,250" and "45%" must read left-to-right inside RTL text.
            string v = ArabicText.ToVisual("الرصيد $1,250", true);
            Assert.StartsWith("$1,250 ", v);
            string p = ArabicText.ToVisual("النسبة 45%", true);
            Assert.StartsWith("45% ", p);
        }

        [Fact]
        public void LeftToRightParagraphIsUnchanged()
        {
            Assert.Equal("Hello (world) 12", ArabicText.ToVisual("Hello (world) 12", false));
            Assert.False(ArabicText.FirstStrongIsRtl("12 Hello"));
            Assert.True(ArabicText.FirstStrongIsRtl("12 مرحبا"));
        }

        [Fact]
        public void ArabicWordInsideEnglishSentence()
        {
            // Paragraph is LTR (first strong is Latin); the Arabic word is reversed in place.
            Assert.Equal("Name: ﺪﻤﺤﻣ ok", ArabicText.ToVisual("Name: محمد ok", false));
        }
    }

    public class TranslatorTests
    {
        private static Translator Make()
        {
            var t = new Translator();
            t.Add("Members", "الأعضاء");
            t.Add("Squads", "الفرق");
            t.Add("Street Gang", "عصابة شوارع");
            t.Add("{0} members", "{0} عضو");
            t.Add("{0} members deployed", "{0} عضو منتشر");
            t.Add("{0} joined {1}", "انضم {0} إلى {1}");
            t.Add("Cash", "النقد");
            return t;
        }

        [Fact]
        public void ExactLookupIgnoresCase()
        {
            var t = Make();
            Assert.Equal("الأعضاء", t.Translate("Members"));
            Assert.Equal("الأعضاء", t.Translate("MEMBERS"));
        }

        [Fact]
        public void TemplatesTranslateCapturedValues()
        {
            var t = Make();
            Assert.Equal("انضم Big Mike إلى عصابة شوارع", t.Translate("Big Mike joined Street Gang"));
            Assert.Equal("12 عضو", t.Translate("12 members"));
        }

        [Fact]
        public void MostSpecificTemplateWins()
        {
            Assert.Equal("5 عضو منتشر", Make().Translate("5 members deployed"));
        }

        [Fact]
        public void PunctuationAndDecorationArePreserved()
        {
            var t = Make();
            Assert.Equal("الأعضاء:", t.Translate("Members:"));
            Assert.Equal("الأعضاء؟", t.Translate("Members?"));
            Assert.Equal("* الأعضاء", t.Translate("* Members"));
            Assert.Equal("  الأعضاء", t.Translate("  Members"));
        }

        [Fact]
        public void SeparatedPartsAreTranslatedIndividually()
        {
            var t = Make();
            Assert.Equal("الأعضاء | الفرق", t.Translate("Members | Squads"));
            Assert.Equal("الأعضاء، Unknown", t.Translate("Members, Unknown"));
            Assert.Equal("النقد: $1,200", t.Translate("Cash: $1,200"));
            Assert.Equal("الأعضاء\nالفرق", t.Translate("Members\nSquads"));
        }

        [Fact]
        public void UnknownAndAlreadyTranslatedTextIsUnchanged()
        {
            var t = Make();
            Assert.Equal("Vinewood Hills", t.Translate("Vinewood Hills"));
            Assert.Equal("الأعضاء", t.Translate("الأعضاء"));
            Assert.Equal("$1,200", t.Translate("$1,200"));
        }

        [Fact]
        public void LoadReportsBadLinesAndBadPlaceholders()
        {
            var t = new Translator();
            var problems = new List<string>();
            int n = t.Load("# comment\nGood\tجيد\nno tab here\n{0} x\t{1} س\n{0}{1} y\tبلا\nEscaped\\nline\tسطر\\nثان\n", problems);
            Assert.Equal(2, n);
            Assert.Equal(3, problems.Count);
            Assert.Equal("سطر\nثان", t.Translate("Escaped\nline"));
        }

        [Fact]
        public void BuiltInTableLoadsCleanly()
        {
            string text = Loc.ReadBuiltIn();
            Assert.NotNull(text);
            var t = new Translator();
            var problems = new List<string>();
            int n = t.Load(text, problems);
            Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
            Assert.True(n > 0);
        }
    }

    public class GlyphLayoutTests
    {
        private static GlyphFont MakeFont()
        {
            var f = new GlyphFont { SpaceAdvance = 0.3f };
            foreach (char c in new[] { '1', '2', '?', 'ﺩ', 'ﺪ', 'ﻋ' })
            {
                var g = new GlyphInfo { Code = c, Advance = c == 'ﻋ' ? 0.5f : 0.4f };
                g.Boxes[0] = new GlyphBox { Left = 0, Top = -0.8f, Width = 0.4f, Height = 1f };
                f.Set(g);
            }
            return f;
        }

        [Fact]
        public void LaysOutGlyphsInVisualOrder()
        {
            var layout = MakeFont().Layout("عدد 12", true);
            Assert.Equal(new[] { '1', '2', 'ﺩ', 'ﺪ', 'ﻋ' }, layout.Glyphs.Select(g => g.Glyph.Code).ToArray());
            Assert.Equal(0f, layout.Glyphs[0].X, 3);
            Assert.Equal(0.4f, layout.Glyphs[1].X, 3);
            Assert.Equal(1.1f, layout.Glyphs[2].X, 3); // after "12" and a space
            Assert.Equal(2.4f, layout.Width, 3);
        }

        [Fact]
        public void MissingGlyphsFallBackToQuestionMark()
        {
            var layout = MakeFont().Layout("Z", false);
            Assert.Single(layout.Glyphs);
            Assert.Equal('?', layout.Glyphs[0].Glyph.Code);
        }

        [Fact]
        public void ParsesMetricsFile()
        {
            var problems = new List<string>();
            var set = GlyphFont.ParseSet("# test\nfont r 28 56 0.9 0.4 0.25\ng r FE8D 0.3 0 -0.9 0.2 1.1 0 -0.9 0.2 1.1\nbogus\n", problems);
            Assert.Single(problems);
            var f = set["r"];
            Assert.Equal(56f, f.VariantEmPx[1]);
            Assert.True(f.Has('ﺍ'));
            Assert.Equal(-0.9f, f.Get('ﺍ').Boxes[1].Top, 3);
        }
    }
}

namespace OrganizationEmpire.Core.Tests
{
    public class ArabicTableTests
    {
        private static readonly Translator T = Load();

        private static Translator Load()
        {
            var t = new Translator();
            t.Load(Loc.ReadBuiltIn());
            return t;
        }

        [Theory]
        [InlineData("Members", "الأعضاء")]
        [InlineData("SETTINGS", "الإعدادات")]
        [InlineData("Big Mike promoted to Capo.", "تمت ترقية Big Mike إلى كابو.")]
        [InlineData("12 members (capacity 20), 3 deployed", "12 عضو (السعة 20)، 3 منتشر")]
        [InlineData("Mission complete - Protection Racket", "اكتملت المهمة - ابتزاز الحماية")]
        [InlineData("Grove Street  (contested)", "Grove Street  (متنازع عليها)")]
        [InlineData("Ballas has declared WAR on The Families!", "أعلن Ballas الحرب على The Families!")]
        [InlineData("DefendPlayer", "دافع عن اللاعب")]
        [InlineData("Street Gang", "عصابة شوارع")]
        [InlineData("Copy that, Boss.", "علم يا زعيم.")]
        [InlineData("Understood. Executing on your confirmation. Right now I'm following the boss.", "مفهوم. سأنفذ بعد تأكيدك. الآن أنا أتبع الزعيم.")]
        [InlineData("Sitrep, Commander: 8 members, 2 territories, cash $77,820; at war with The Lost MC.", "تقرير الوضع يا قائد: 8 أعضاء، 2 مناطق، النقد $77,820؛ في حرب مع The Lost MC.")]
        [InlineData("Uh... Copy that, Boss.", "أمم... علم يا زعيم.")]
        public void TranslatesRealInterfaceStrings(string english, string arabic)
        {
            Assert.Equal(arabic, T.Translate(english));
        }

        [Fact]
        public void ProperNounsStayEnglish()
        {
            Assert.Equal("Vinewood", T.Translate("Vinewood"));
            Assert.Equal("The Lost MC", T.Translate("The Lost MC"));
        }

        [Fact]
        public void EveryEnumDisplayNameHasATranslation()
        {
            var skip = new HashSet<string> { "Bc", "Joining", "LogLevel", "ChatRole", "ConversationState", "AiErrorKind", "AiProviderKind" };
            var missing = new List<string>();
            foreach (var type in typeof(Translator).Assembly.GetTypes().Where(t => t.IsEnum && !skip.Contains(t.Name)))
            {
                foreach (string name in System.Enum.GetNames(type))
                {
                    string shown = Core.Util.TextUtil.Humanize(name);
                    if (T.Translate(shown) == shown) missing.Add(type.Name + "." + name);
                }
            }
            Assert.True(missing.Count == 0, "Untranslated: " + string.Join(", ", missing));
        }
    }
}

namespace OrganizationEmpire.Core.Tests
{
    public class ArabicIntentTests
    {
        [Theory]
        [InlineData("اتبعني يا رجل", Model.OrderType.Follow)]
        [InlineData("وابقَ هنا لا تتحرك", Model.OrderType.Stay)]
        [InlineData("اركب السيارة بسرعة", Model.OrderType.EnterVehicle)]
        [InlineData("انزل من السيارة", Model.OrderType.ExitVehicle)]
        [InlineData("ارجع إلى المقر", Model.OrderType.ReturnToHQ)]
        [InlineData("غطّني!", Model.OrderType.DefendPlayer)]
        [InlineData("هاجم الهدف", Model.OrderType.Attack)]
        public void DetectsArabicOrders(string text, Model.OrderType expected)
        {
            Assert.Equal(expected, AI.IntentDetector.DetectOrder(text));
        }

        [Theory]
        [InlineData("كيف حالك اليوم؟", AI.DialogueTopic.Morale)]
        [InlineData("أعطني تقريرًا", AI.DialogueTopic.Report)]
        [InlineData("هل تريد أن تنضم إلينا؟", AI.DialogueTopic.Join)]
        [InlineData("شكرًا على العمل", AI.DialogueTopic.Praise)]
        [InlineData("السلام عليكم", AI.DialogueTopic.Greeting)]
        public void DetectsArabicTopics(string text, AI.DialogueTopic expected)
        {
            Assert.Equal(expected, AI.IntentDetector.DetectTopic(text, out _));
        }
    }
}
