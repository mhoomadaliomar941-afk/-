# Recon map: CloudDrive Gaming (Android + iOS)

النطاق: تطبيق تحكم افتراضي للقيادة (عجلة، دواسات، ناقل حركة، أزرار) يعمل كوحدة تحكم حقيقية لألعاب GeForce NOW، مع استوديو تصميم وملفات تحكم لكل لعبة.
لمن: المستخدم نفسه كمنتج يُنشر على المتاجر.
التاريخ: 2026-11-15

> هذا ليس استنساخاً لتطبيق موجود. الـ"أصل" هنا شيئان:
> 1. **GeForce NOW (GFN)** كسطح تكامل: ما الذي تسمح به NVIDIA والأنظمة فعلاً.
> 2. **صورة التخطيط المرفقة** (`replica/screens/R01-reference-layout.png`) كمرجع لعناصر التحكم.
>
> لذلك أهم قسم في هذا الملف هو **تحليل الجدوى** أدناه. وهو يغيّر شكل المنتج.

---

## 0. تحليل الجدوى التقنية (الخلاصة أولاً)

### الحكم المختصر

| المتطلب | الحكم | لماذا |
| --- | --- | --- |
| ربط حساب NVIDIA / GFN داخل التطبيق | ❌ غير ممكن رسمياً | لا يوجد OAuth/OIDC عام من NVIDIA لتطبيقات طرف ثالث. وميزة Account Linking في GFN SDK تعمل في الاتجاه المعاكس: الشريك (ناشر اللعبة) هو مزوّد الهوية وGFN هو من يطلب الربط [S3]. |
| معرفة مكتبة ألعاب المستخدم أو ما يملكه | ❌ غير ممكن | لا API عام لذلك. ميزة Game Ownership Sync خاصة بالمنصات الشريكة (Steam وغيرها) [S3]. |
| تشغيل جلسة GFN داخل تطبيقنا | ❌ غير ممكن | الـSDK لنوافذ Windows/Linux وموجّه لمطوري الألعاب، والوصول إليه بموافقة NVIDIA [S1][S2]. لا يوجد SDK بثّ للهاتف. |
| فتح GFN من تطبيقنا | ✅ ممكن | Android: فتح تطبيق GFN الرسمي. iOS: فتح `play.geforcenow.com` في Safari (GFN على iOS تطبيق ويب في Safari فقط [S9]). |
| فتح لعبة محددة (Deep Link) | ⚠️ ممكن بشروط | الصيغة الرسمية: `https://play.geforcenow.com/games?game-id=<id>&utm_source=<src>&utm_campaign=<c>`. لكن NVIDIA توثّقها رسمياً لمتصفحات Chrome/Edge على الحاسوب فقط [S4]. يجب اختبار سلوكها على الهاتف. |
| التحقق من توفر لعبة على GFN | ⚠️ جزئي | لا API موثّق. الـ`game-id` يُؤخذ من رابط صفحة اللعبة في GFN [S4]. توجد قائمة JSON عامة يستخدمها مجتمع المطورين لكنها **غير موثّقة رسمياً** وقد تختفي [S10]. |
| إرسال مدخلات gamepad/keyboard إلى تطبيق GFN على **نفس الهاتف** (Android) | ❌ غير ممكن | حقن الأحداث في تطبيق آخر يحتاج صلاحية `INJECT_EVENTS`، وهي صلاحية نظام لا تُمنح للتطبيقات العادية. |
| طبقة عائمة (Overlay) فوق GFN على Android تتحكم باللعبة | ❌ عملياً لا | الطبقة إما تأخذ اللمس لنفسها (فلا يصل لـGFN) أو تمرّره. ومنذ Android 12 يُحظر تمرير اللمس عبر نافذة تطبيق آخر إذا زادت عتمتها عن 0.8 [S6]. الطبقة وحدها = أزرار لا تعمل، وهذا ما طلبت تجنّبه. |
| Accessibility Service لمحاكاة اللمس | ⚠️ ممكن تقنياً، **لا نوصي به** | `dispatchGesture` يحاكي لمساً فقط (ليس gamepad ولا لوحة مفاتيح)، فيعمل فقط مع ألعاب GFN التي تدعم اللمس. زمن الاستجابة أعلى. وسياسة Google Play تقصر هذه الخدمة على أدوات ذوي الإعاقة، وغيرها يحتاج إفصاحاً وإعلاناً ومخاطرة رفض عالية [S5]. |
| **الهاتف كذراع تحكم Bluetooth حقيقي** (Android 9+) | ✅ ممكن ورسمي | واجهة `BluetoothHidDevice` (API 28) تجعل الهاتف جهاز HID: gamepad و/أو لوحة مفاتيح و/أو فأرة [S7]. الجهاز الذي يشغّل GFN يراه ذراعاً حقيقياً، وGFN يدعم أذرع Bluetooth [S8]. |
| iOS: طبقة فوق Safari/تطبيقات أخرى | ❌ غير ممكن | iOS لا يسمح بنوافذ فوق تطبيقات أخرى ولا بحقن مدخلات فيها. |
| iOS: الهاتف كذراع Bluetooth | ❌ غير ممكن | لا يوجد على iOS ما يعادل `BluetoothHidDevice`. |
| iOS: الهاتف كذراع عبر Wi-Fi لحاسوب يشغّل GFN | ⚠️ ممكن بشروط | يحتاج برنامجاً مرافقاً على الحاسوب ينشئ ذراعاً افتراضياً. هذا يعني مكوّناً ثالثاً وتعريفاً (driver)، ومن الخيارات المعروفة ViGEmBus وهو متوقف عن التطوير. يُترك لمرحلة لاحقة. |
| عجلة، دواسات، ناقل، استوديو، ملفات JSON، RTL | ✅ ممكن بالكامل | كلها محلية داخل تطبيقنا. |

### ما معنى هذا للمنتج

**تجربة "هاتف واحد": تطبيقنا يرسم العجلة فوق GFN على نفس الشاشة ويتحكم باللعبة. هذه التجربة غير ممكنة بطريقة رسمية، لا على Android ولا على iOS.** أي تطبيق يدّعي ذلك يستخدم إما Accessibility (لمس محاكى فقط، ومخاطرة سياسة) أو صلاحيات root.

**المسار الحقيقي الذي يعمل هو "جهازان":**

```
[هاتف Android: CloudDrive]  --Bluetooth HID (gamepad + keyboard)-->  [جهاز يشغّل GFN: PC / Mac / Android TV / تابلت / Chromebook]
   عجلة + دواسات + ناقل + أزرار                                             GFN يرى ذراعاً حقيقياً ويرسله للعبة في السحابة
```

- العجلة → محور تناظري (Left Stick X)، الغاز → RT، الفرامل → LT (تدرّج 0–100% حقيقي).
- الأزرار الإضافية (أنوار، بوق، إشارات...) → أزرار gamepad أو **مفاتيح لوحة مفاتيح** (HID مركّب). يربطها المستخدم بما تدعمه اللعبة.
- زاوية الدوران 90–1080° = كيف نحوّل دوران الإصبع إلى قيمة المحور، وتعمل مع أي لعبة.
- إطلاق اللعبة: نعرض رابط GFN الرسمي للعبة كـ**QR/مشاركة** ليُفتح على الجهاز الذي يشغّل GFN.

**مخاطر يجب اختبارها على أجهزة حقيقية قبل الالتزام (لا يمكن التحقق منها من هنا):**
1. بعض الشركات المصنّعة تعطّل HID Device profile في Android. نحتاج قائمة توافق بالأجهزة.
2. هل يتعرّف عميل GFN على ذراع HID عام (ليس Xbox/DualSense) على كل منصة؟ Windows يراه DirectInput وليس XInput. نحتاج اختباراً على Windows وmacOS وAndroid TV. **أخطر نقطة في المشروع.**
3. iPad كجهاز مضيف: iOS يقبل أذرع MFi/Xbox/DualSense فقط، فذراع HID عام غالباً لن يُقبل.
4. زمن الاستجابة عبر Bluetooth (هدف أقل من 20ms من اللمس إلى التقرير).

**المطلوب من NVIDIA إن أردت تكاملاً أعمق:** التسجيل في GeForce NOW Developer Portal عبر `gdp-queries@nvidia.com` [S2]. لكن الـSDK مصمم لمطوري الألعاب، فلا نتوقع أن يمنح تطبيق تحكم ربط حسابات أو حقن مدخلات.

---

## Sources

| # | source | URL | notes |
| --- | --- | --- | --- |
| S1 | GFN SDK (رسمي، GitHub) | https://github.com/NVIDIAGameWorks/GeForceNOW-SDK | Windows x64/x86 وLinux فقط. موجّه لمطوري وناشري الألعاب. |
| S2 | GFN Developer Platform | https://developer.nvidia.com/geforce-now | التسجيل عبر gdp-queries@nvidia.com، والوصول بموافقة. |
| S3 | GfnSdk-Account-Linking-SSO.md | https://github.com/NVIDIAGameWorks/GeForceNOW-SDK/tree/master/doc | الشريك هو IdP، وGFN يربط حسابه. ليس OAuth لتطبيقات خارجية. |
| S4 | GfnSdk-Deep-Linking.md | https://github.com/NVIDIAGameWorks/GeForceNOW-SDK/tree/master/doc | صيغة الرابط، ومصدر game-id، ودعم Chrome/Edge/Yandex على الحاسوب. |
| S4b | GfnSdk-Mobile-Touch-Integration.md | https://github.com/NVIDIAGameWorks/GeForceNOW-SDK/tree/master/doc | على Android: لمس + keyboard + mouse + gamepad. على iOS: لمس + gamepad فقط. اللمس يتطلب دعماً من اللعبة نفسها. |
| S5 | Google Play: AccessibilityService policy | https://support.google.com/googleplay/android-developer/answer/10964491 | isAccessibilityTool لأدوات ذوي الإعاقة فقط، وغيرها يحتاج إفصاحاً وإعلاناً. |
| S6 | Android: Untrusted touch events | https://medium.com/androiddevelopers/untrusted-touch-events-2c0e0b9c374c | حظر تمرير اللمس عبر overlay بعتمة أكبر من 0.8 منذ Android 12. |
| S7 | Android BluetoothHidDevice | https://developer.android.com/reference/android/bluetooth/BluetoothHidDevice | API 28، وSUBCLASS2_GAMEPAD، وصلاحية BLUETOOTH_CONNECT. |
| S8 | GFN System Requirements | https://www.nvidia.com/en-gb/geforce-now/system-reqs/ | الأذرع المدعومة تختلف حسب الجهاز. |
| S9 | GFN على iOS عبر Safari | https://www.macrumors.com/2020/11/19/geforce-now-safari-launches-fortnite-coming-soon/ | تطبيق ويب WebRTC. ذراع Bluetooth نعم، keyboard/mouse لا. |
| S10 | قائمة ألعاب GFN العامة (غير رسمية) | https://awesome.ecosyste.ms/projects/github.com%2Fskynewz%2Fgeforce-now-games | تشير إلى static.nvidiagrid.net/.../gfnpc-en-US.json. غير موثّقة، ولم أتمكن من الوصول إليها من هذه البيئة. |
| S11 | GFN Terms of Use | https://www.nvidia.com/en-gb/geforce-now/terms-of-use/ | يمنع تعديل GFN أو هندسته العكسية، والغش والـbotting. ذراع HID حقيقي لا يمس GFN. |
| R01 | صورة التخطيط المرجعية (من المستخدم) | `replica/screens/R01-reference-layout.png` | مرجع للعناصر وتوزيعها. |

---

## Core loop

المستخدم يختار لعبة، ويفتح ملف التحكم الخاص بها، ويقود بالعجلة والدواسات على هاتفه، فتصل المدخلات كذراع تحكم حقيقي إلى GeForce NOW.

---

## Screens

| ID | screen | route / how to reach | purpose | key components | states |
| --- | --- | --- | --- | --- | --- |
| S01 | Welcome | أول تشغيل | تعريف، واختيار اللغة، وشرح صريح لطريقة العمل (جهازان) | شعار، CTA، مبدّل لغة | default, RTL/LTR |
| S02 | Setup: اختيار الجهاز المضيف | S01 ← "ابدأ" | أين يعمل GFN؟ (PC/Mac/TV/تابلت/هذا الهاتف) مع توضيح ما يعمل في كل حالة | بطاقات اختيار، تنبيه حدود | default, "هذا الهاتف" (تحذير: العجلة لن تتحكم بـGFN على نفس الجهاز) |
| S03 | Bluetooth Pairing | S02 أو Settings | تسجيل الهاتف كـHID وإقرانه بالمضيف | حالة BT، قائمة أجهزة، تعليمات لكل منصة | bt off, permission denied, unsupported device (OEM), discoverable, pairing, paired, connection lost |
| S04 | Input Test / Calibration | بعد الإقران، Settings | إثبات أن المدخلات تصل: قيم المحاور والأزرار حية | عدادات المحاور، مؤشرات الأزرار، زمن الاستجابة | not connected, live, report errors |
| S05 | Home | Tab | آخر لعبة، والملف النشط، وحالة الاتصال، وزر "ابدأ القيادة" | بطاقة حالة، بطاقات ألعاب، CTA | first-run empty, connected, disconnected |
| S06 | GFN Status | Home ← بطاقة الحالة | حالة صادقة: "GFN غير مرتبط بحساب (غير ممكن رسمياً)"، والتطبيق الرسمي مثبت أم لا، وزر فتح GFN | صفوف حالة، أزرار فتح | GFN app installed / not installed, iOS (Safari) |
| S07 | My Games | Tab | ألعاب المستخدم، وبحث، وتصنيف، ومفضلة، وآخر استخدام | بحث، chips، شبكة بطاقات | empty, filled, no results, offline |
| S08 | Add Game | S07 ← "+" | إضافة لعبة بلصق رابط صفحتها في GFN (يُستخرج game-id) أو اختيارها من القائمة العامة إن توفرت | حقل رابط، معاينة، مصدر التحقق | invalid link, duplicate, list unavailable, verified-by-link |
| S09 | Game Detail | S07 ← بطاقة | الملف المرتبط، وآخر تشغيل، وزر فتح في GFN، وQR للرابط | غلاف (صورة المستخدم أو بلا صورة)، QR، CTA | no profile, profile linked |
| S10 | Controller Studio | Tab / "تعديل" من ملف | محرر سحب وإفلات على قماش بحجم الشاشة | قماش + شبكة، شريط أدوات، طبقات | empty canvas, editing, multi-select, unsaved→autosaved, phone/tablet preview |
| S11 | Element Palette | S10 ← "+" | إضافة عنصر: عجلة، دواسات، ناقل، Joystick، D-Pad، أزرار، راديو | sheet بشبكة عناصر | default |
| S12 | Element Inspector | S10 ← تحديد عنصر | حجم، دوران، لون، شفافية، أيقونة، إظهار/إخفاء، نسخ، حذف، ترتيب | sliders، color picker، icon picker | single, multi, locked |
| S13 | Binding Editor | S12 ← "الربط" | ربط العنصر بمخرج: محور/زر gamepad، مفتاح لوحة، زر فأرة | قائمة المخرجات، "اضغط للالتقاط" | unbound, bound, conflict |
| S14 | Steering Settings | S12 للعجلة / Settings | الزاوية 90–1080، الحساسية، الرجوع للمنتصف، Dead Zone، Linearity، نسبي/مباشر، المصدر (لمس/إمالة)، الشكل | sliders + منحنى استجابة حي | default, modified, reset |
| S15 | Pedals Settings | S12 للدواسة | منحنى، Dead Zone، وضع الضغط (موضع الإصبع/قوة)، إظهار الكلتش | sliders، منحنى | default, clutch hidden |
| S16 | Gearbox Settings | S12 للناقل | PRND / تسلسلي +- / H-Pattern، وربط كل غيار | محرر بوابة H، قائمة غيارات | auto, sequential, H |
| S17 | My Profiles | Tab | قائمة الملفات: إنشاء، نسخ، إعادة تسمية، حذف، استيراد، تصدير، ربط بلعبة | قائمة، قائمة سياقية | empty, filled, import error (schema), delete confirm |
| S18 | Gaming HUD | Home ← "ابدأ" / Game Detail | شاشة القيادة الحية بملء الشاشة، أفقية | العناصر المرسومة، شريط حالة الاتصال | connected, disconnected (overlay واضح)، locked, edit-unlocked |
| S19 | HUD Quick Menu | S18 ← زر/إيماءة | تبديل الملف، الشفافية، إظهار/إخفاء، قفل، اتجاه الشاشة، خروج | sheet صغير | default |
| S20 | Settings | Tab | الأقسام: General، Controller، Steering، Graphics، Audio، Language، Theme، Backup & Restore، Privacy، About | قائمة أقسام | default |
| S21 | Backup & Restore | S20 | تصدير/استيراد كل الملفات والإعدادات كملف JSON | أزرار، معاينة محتوى | success, invalid file, version mismatch |
| S22 | Help & Support | Tab/Settings | دليل الإقران لكل منصة، وجدول التوافق، و"لماذا لا يعمل على نفس الهاتف" | مقالات، FAQ | offline |

**22 شاشة.** S03 وS04 وS18 هي القلب التقني.

---

## Flows

```
F01 أول تشغيل حتى أول قيادة (Android)
    S01 -> S02 (اختيار PC) -> S03 (تفعيل BT + إقران) -> S04 (اختبار) -> S18
    happy path: ~6 نقرات + خطوة إقران على الجهاز المضيف
    edge: BT مطفأ، رفض صلاحية BLUETOOTH_CONNECT، جهاز لا يدعم HID Device،
          المضيف يرى الذراع ولكن GFN لا يتعرف عليه، انقطاع أثناء القيادة

F02 تشغيل لعبة بملفها
    S07 -> S09 -> (عرض QR / مشاركة الرابط للمضيف) -> S18 بالملف المرتبط
    happy path: 3 نقرات
    edge: لعبة بلا ملف (اقتراح قالب Racing)، رابط GFN غير صالح، المضيف غير متصل

F03 تصميم وحدة تحكم من الصفر
    S17 (+ جديد) -> S10 -> S11 (إضافة عجلة) -> سحب/تحجيم -> S12 -> S13 (ربط) -> معاينة -> حفظ تلقائي
    edge: تداخل عناصر، عنصر خارج حدود الشاشة، تغيير الاتجاه، تابلت مقابل هاتف

F04 ربط زر بأمر في اللعبة
    S12 -> S13 -> اختيار مفتاح لوحة (مثلاً L للأنوار) أو زر gamepad -> اختبار في S04
    edge: تعارض ربطين، نفاد أزرار gamepad (الحل: مفاتيح لوحة)

F05 ضبط العجلة
    S12 -> S14 -> تعديل الزاوية/المنحنى مع معاينة حية -> S04 للتحقق

F06 استيراد/تصدير ملف
    S17 -> تصدير JSON (مشاركة نظام) | استيراد ملف -> تحقق schema -> إضافة
    edge: ملف تالف، إصدار schema أحدث، اسم مكرر

F07 أثناء اللعب
    S18 -> S19 -> تبديل ملف / شفافية / قفل -> S18
    edge: انقطاع BT (إيقاف كل المدخلات وإرسال تقرير صفري فوراً لتجنب "دواسة عالقة")

F08 إضافة لعبة
    S07 -> S08 -> لصق رابط GFN -> استخراج game-id -> حفظ
    edge: رابط ليس من play.geforcenow.com، game-id غير موجود
```

---

## Components (من صورة R01 + واجهات التطبيق)

| component | variants | states | used on |
| --- | --- | --- | --- |
| SteeringWheel | 3+ أشكال (Sport 3-spoke كما في R01، Truck، Rally، Formula). وسط بوق اختياري | idle, rotating, at-limit, returning | S10, S18 |
| PaddleShifter | − / + (يسار/يمين العجلة) | idle, pressed | S10, S18 |
| Pedal | Gas (خط أخضر)، Brake (أحمر)، Clutch. مع قراءة % | 0%, partial, 100%, hidden | S10, S18 |
| GearSelector | PRND عمودي (R01)، Sequential +/-، H-Pattern | gear active (حدّ برتقالي) | S10, S18 |
| RoundIconButton | indicator ←/→، headlights، cruise، camera | off, on (LED علوي ملوّن)، pressed, blinking | S10, S18 |
| SquareIconButton | 4x4، diff lock، hazard (LED أحمر) | off, on, pressed | S10, S18 |
| RockerSwitch | ▲/▼ مزدوج | up, down, idle | S10, S18 |
| EngineStartStop | دائري | off, on, pressed | S10, S18 |
| Thumbstick | دائري بحلقة خضراء | idle, dragging | S10, S18 |
| DPad | 4 / 8 اتجاهات | idle, pressed dir | S10, S18 |
| RadioPanel | لوحة راديو: volume/tune knobs، presets 1–6 (كل منها قابل للربط) | — | S10, S18 (could) |
| TrimSlider | شريط أسفل العجلة (إعادة توسيط/trim) | idle, dragging | S18 |
| ConnectionBadge | connected / connecting / disconnected | — | S05, S18 |
| TabBar / NavRail | هاتف: tab bar، تابلت: rail | active | all |
| Card, ListRow, Toggle, Slider, Sheet, Dialog, Toast, EmptyState, ColorPicker, IconPicker, QRCode | — | default, disabled, error | all |

نظام الألوان من R01: خلفية #1E1E1E تقريباً، عناصر نيومورفيك داكنة، لكنات برتقالي (#F5A623 تقريباً) وأخضر، وأحمر للفرامل والطوارئ. تفاصيل الألوان والمقاسات في `/replica-design`.

---

## Inferred data model

النموذج **مُصمَّم من متطلباتك** وليس مستنتجاً من تطبيق آخر. كله محلي على الجهاز.

```
Profile      id, schemaVersion, name, gameId?, orientation, baseScreen {w,h,dpi},
             elements[], steering, pedals, gearbox, outputs {mode: gamepad|keyboard|both},
             createdAt, updatedAt, isTemplate
             evidence: المتطلبات §9، confidence: high

Element      id, type (wheel|pedal|gearbox|button|joystick|dpad|paddle|rocker|radio),
             transform {x,y,w,h,rotation} (نسبية 0..1 من الشاشة)، z, visible, locked,
             opacity, color, icon, skin, label, config {}, bindings[]
             evidence: §8، confidence: high

Binding      controlId (مثلاً "wheel.axis", "pedal.value", "button.press", "gear.R"),
             output {kind: gamepadAxis|gamepadButton|key|mouseButton|mouseAxis, code},
             mode (hold|toggle|pulse), invert, curve?
             evidence: §7, §11، confidence: high

SteeringCfg  rangeDeg (90..1080), sensitivity, returnSpeed, deadZone, linearity (gamma),
             mode (absolute|relative), source (touch|tilt), skin

PedalCfg     curve, deadZone, saturation, inputMode (position|pressure), clutchEnabled
GearboxCfg   layout (prnd|sequential|hPattern), gears[] → bindings, manualEnabled

Game         id, gfnGameId?, title, source (userLink|publicList), verifiedAt?,
             favorite, lastPlayedAt?, profileId?, coverUri? (صورة من المستخدم فقط)
             evidence: §10 + S4، confidence: medium (مصدر التحقق محدود)

HostDevice   btAddress, name, platform (windows|mac|android|tv|chromebook), lastConnectedAt
Settings     language (ar|en), direction auto, theme, haptics, hudFps, autosave, privacy flags
```

العلاقات: `Game 0..1 → Profile`، و`Profile 1 → n Element`، و`Element 1 → n Binding`، و`HostDevice` مستقل.
**عن قصد لا يوجد كيان `NvidiaAccount`**: لا يمكن ربطه فعلياً، فلن نخزّن حالة ربط وهمية.

---

## Feature matrix

راجع `features.csv`. Must: 34، should: 17، could: 9، skip: 9 (لا تُحتسب صفوف skip في must/should/could).

---

## Out of scope (cannot or should not be built)

- **ربط حساب NVIDIA، أو تسجيل الدخول به، أو قراءة مكتبة المستخدم.** لا API عام. البديل الصادق: شاشة حالة تقول ذلك صراحة، وزر لفتح GFN الرسمي حيث يسجّل المستخدم دخوله.
- **تشغيل بثّ GFN داخل تطبيقنا** أو تضمين play.geforcenow.com في WebView والتلاعب به. هذا يخالف شروط GFN (تعديل العميل) [S11]، وغير مدعوم.
- **حقن مدخلات في تطبيق GFN على نفس هاتف Android.** صلاحية نظام.
- **Overlay يتحكم بـGFN على نفس الهاتف.** Accessibility ممكن تقنياً للمس فقط، لكنه مستبعد بسبب سياسة Play والجودة. يمكن إعادة النظر فيه كتجربة خارج المتجر لاحقاً.
- **أي شيء فوق Safari أو تطبيقات أخرى على iOS.**
- **iPhone كذراع Bluetooth.** لا API.
- **تغيير إعدادات رسوميات GFN.** "Graphics Preferences" عندنا = جودة رسم الواجهة ومعدل الإطارات وتوفير البطارية فقط.
- **أغلفة الألعاب وشعارات NVIDIA/الناشرين.** مملوكة لأصحابها. الأغلفة من المستخدم أو بدونها. اسم المنتج لا يحتوي "GeForce" (يجوز ذكر "يعمل مع GeForce NOW" وصفياً).
- **صورة R01 نفسها.** مرجع تصميم. الأشكال ستُرسم من جديد، إلا إذا كنت تملك حقوق تلك الرسومات.

---

## Size

الشاشات 22، التدفقات 8، الكيانات 8.

الأجزاء الصعبة:
1. **مكدّس Bluetooth HID**: واصف HID مركّب (gamepad + keyboard)، والإقران، وإعادة الاتصال، والتوافق مع المضيفين وعميل GFN. هذا يحتاج اختبار أجهزة حقيقية.
2. **محرك العجلة بزمن استجابة منخفض**: لمس متعدد مستمر، ومنحنيات، وdead zone، والرجوع للمنتصف، وإرسال التقارير بمعدل 120–250Hz دون استنزاف البطارية.
3. **Controller Studio**: سحب وإفلات، وتحجيم، ودوران، ومحاذاة، وشبكة، واختيار متعدد، على هواتف وتابلت بأبعاد نسبية.

الحجم: **XL كما هو مطلوب** (Android + iOS + تكامل GFN كامل). لا يمكن الوعد به لأن نصف المتطلبات غير ممكن رسمياً.
**بعد إعادة التحديد المقترحة: L.** Android أولاً بوضع "الجهازين". iOS لاحقاً كاستوديو وعارض، ثم ذراع عبر Wi-Fi مع برنامج مرافق للحاسوب.
