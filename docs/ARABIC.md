# الواجهة العربية / Arabic interface

## بالعربية

يدعم المود لغتين للواجهة: **الإنجليزية** و**العربية**. يمكنك التبديل بينهما في أي وقت:

- من داخل اللعبة: **F7 ← الإعدادات ← عام والواجهة ← Language / اللغة**،
- أو من أول سطر في معالج **تأسيس الفصيل** (قبل إنشاء فصيلك)،
- أو من الملف `scripts\OrganizationEmpire\OrganizationEmpire.ini`:

```ini
[General]
Language = Arabic          ; أو English
MirrorLayoutForArabic = true   ; القائمة من اليمين إلى اليسار
ArabicTextScale = 1        ; حجم الخط العربي (0.8 - 1.4)
```

التبديل فوري ولا يحتاج إعادة تشغيل اللعبة.

### ما الذي يتغير عند اختيار العربية

- كل القوائم والتبويبات والأزرار والتلميحات والإشعارات وأهداف المهام وتقارير الأحداث تظهر بالعربية
  (أكثر من 2000 عبارة مترجمة).
- القائمة تنعكس من اليمين إلى اليسار (يمكن إيقاف ذلك من الإعدادات)، والأعلام لا تنعكس.
- الأرقام والمبالغ تبقى بالأرقام المعتادة ($1,250 و 45%).
- أسماء الأحياء والعصابات المنافسة والأشخاص تبقى كما هي في اللعبة (مثل Grove Street وBallas).
- **الحوار بالذكاء الاصطناعي**: إذا كنت تستخدم مزوّدًا (OpenAI / Anthropic / Gemini...) فالشخصيات ترد بالعربية.
  المحرك المدمج بدون اتصال يرد أيضًا بالعربية عبر جدول الترجمة.
- يمكنك كتابة الأوامر بالعربية في المحادثة، مثل: «اتبعني»، «ابقَ هنا»، «اركب السيارة»، «ارجع إلى المقر»،
  «غطّني»، «هاجم» - ويظهر الأمر المرصود لتؤكده بنفسك كالعادة.

### للكتابة بالعربية

غيّر تخطيط لوحة المفاتيح في Windows إلى العربية (Alt+Shift) واكتب مباشرة في حقول القائمة والمحادثة.
لوحة المفاتيح الخاصة بـ GTA (الخيار NativeKeyboard) لا تدعم العربية، فاترك **إدخال النص = مباشر**.

### لماذا يحتاج المود إلى مجلد `text`؟

خطوط GTA V لا تحتوي على حروف عربية، ومحرك النصوص في اللعبة لا يصل الحروف ولا يكتب من اليمين إلى اليسار.
لذلك يرسم المود النص العربي بنفسه من صور حروف جاهزة (خط Cairo) موجودة في
`scripts\OrganizationEmpire\text\glyphs`. **لا تحذف مجلد `text`** إذا كنت تستخدم العربية؛ إذا كان مفقودًا
يعود المود تلقائيًا إلى الإنجليزية ويظهر تنبيه.

### تحسين الترجمة بنفسك

1. افتح `scripts\OrganizationEmpire\text\ar.builtin.txt` لترى كل العبارات (الإنجليزية، ثم Tab، ثم العربية).
2. أنشئ ملفًا باسم `ar.txt` في نفس المجلد وضع فيه فقط السطور التي تريد تغييرها بنفس الصيغة.
3. احتفظ بالرموز `{0}` و`{1}` كما هي (هي أماكن الأسماء والأرقام) ويمكنك تغيير ترتيبها داخل الجملة.

الملف `ar.txt` يُحمّل بعد الجدول المدمج ويستبدل العبارات المطابقة.

### حدود معروفة (بصراحة)

- الأشياء التي ترسمها اللعبة نفسها - أسماء العلامات على الخريطة، شاشة الإيقاف، إشعارات GTA الأصلية - لا يمكنها
  عرض العربية، لذلك تبقى بالإنجليزية. لا تضع أسماء عربية للمركبات إذا أردت رؤيتها في دليل الخريطة.
- الحركات (التشكيل) تُحذف عند العرض؛ النص يبقى مقروءًا.
- لم يُختبر عرض النص العربي داخل اللعبة الحقيقية أثناء التطوير (لا تتوفر اللعبة في بيئة البناء). الوصل بين
  الحروف وترتيبها مختبَران آليًا، وصور المعاينة مأخوذة خارج اللعبة. إذا بدا الخط كبيرًا أو صغيرًا عدّل
  **حجم النص العربي** في الإعدادات.

## English

The interface can be switched between **English** and **Arabic** (Settings > General & interface > *Language / اللغة*,
the first row of the creation wizard, or `Language = Arabic` in the ini). It applies immediately.

How it works:

- **Translation at draw time.** Game logic keeps producing English; `Renderer` passes every string through
  `Loc.T` (`Core/Localization/Translator.cs`) right before measuring/drawing it. Lookup: exact entry
  (case-insensitive) -> templates with `{0}` placeholders whose captured values are translated recursively ->
  splitting on UI separators and sentences. ~2,000 strings ship in `Core/Localization/ar.txt` (embedded in the
  DLL); `text\ar.txt` next to the mod can override entries.
- **Arabic rendering.** GTA V's fonts have no Arabic glyphs and its text engine has no shaping or RTL support, so
  strings containing Arabic are drawn by `UI/SpriteText.cs` from pre-rendered glyph textures
  (`text\glyphs\*.png`, Cairo font, SIL OFL). `ArabicText.Shape` picks contextual presentation forms (incl.
  lam-alef ligatures) and `ArabicText.Reorder` applies a single-paragraph bidi pass (Western digits stay
  European numbers so `$1,250` reads naturally). Glyphs are queued and emitted at the end of the frame in painter's
  order: opaque panels drawn later (dialogs, popups, toasts) hide glyphs underneath, because ScriptHookV textures
  always draw above the game's own UI. ScriptHookV allows 64 instances of a texture per frame, so busy glyphs get
  extra texture "banks" (copies in `text\cache`), and instances that disappear are overwritten with transparent
  draws to avoid ghost text.
- **Mirroring.** With `MirrorLayoutForArabic`, `Renderer` flips every rectangle and text anchor horizontally and
  reports the mouse in the unflipped layout space, so pages need no RTL-specific code. Flags and emblems are drawn
  inside an "unmirrored" region.
- **AI.** When Arabic is active the system prompt asks the model to reply in Arabic; `IntentDetector` understands
  Arabic order and topic phrases (normalized alef/yeh/teh-marbuta forms).

Maintenance:

- `python3 tools/loc/extract_strings.py . --with-locations` lists the display strings in the sources (literals,
  interpolations and `"a" + x + "b"` concatenations as `{n}` templates).
- `tools/glyphgen/glyphgen.js` regenerates the glyph textures (see the header of that file).
- Tests: `LocalizationTests.cs` covers shaping, bidi, the translator, the shipped table (placeholder consistency,
  every enum name translated, real composed messages) and Arabic intent detection.

Limitations: text drawn by GTA itself (map blip names, the pause menu, native notifications, the on-screen keyboard)
cannot show Arabic. Arabic rendering in the running game was not verified in this environment (no GTA V available);
shaping/bidi/translation are unit-tested and the preview screenshots were rendered outside the game.
