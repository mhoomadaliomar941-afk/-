# PROJECT_STATUS – REALISTIC WORLD (Java Edition 1.12.2)

آخر تحديث: 2026-11-14 · النسخة 0.3.0 (مطابقة لنسخة Bedrock 0.3.0)

## الهدف
نقل المود بالكامل لـ **Minecraft Java Edition 1.12.2** كمود **Forge** (المنصة المعتمدة لهذا الإصدار).

## الحالة

| المرحلة | الحالة |
|---|---|
| **J1 – النواة بـ Java 8** (المحرك، الإعدادات، الأداء، المناطق، الطقس، المناخ، الضباب، المياه، HUD، الفحص الذاتي) | ✅ مكتملة، **52/52 اختبار** |
| **J2 – طبقة Forge** (البلوكات، الأدوات، الأحداث، الواجهة، الـ HUD والضباب على العميل، الشبكة، الحفظ، الأوامر) | ⛔ **متوقفة:** شبكة البيئة تمنع مستودع Forge وخوادم Mojang |
| **J3 – تجربة على سيرفر Forge حقيقي** (Java 8 منصّب هنا)، الوثائق، ملف الـ jar | ⏳ بعد J2 |

## اللي يلزم حتى نكمل J2
البيئة لازم تسمح بهذني المواقع (مكتبات Forge و Minecraft):
```
maven.minecraftforge.net
files.minecraftforge.net
libraries.minecraft.net
launchermeta.mojang.com
launcher.mojang.com
piston-meta.mojang.com
piston-data.mojang.com
resources.download.minecraft.net
```
الطريقة: من claude.ai بالكمبيوتر أو تطبيق Claude محدّث، اكتب «اكمل» ووافق على طلب البيئة. أو من إعدادات البيئة: Network access ← Allowed domains ← أضف المواقع أعلاه (مع إبقاء Allow package managers مفعّل).

## الهيكل
```
REALISTIC_WORLD_JAVA/
├── settings.gradle, build.gradle     Gradle 4.10.3 (wrapper) — الإصدار المتوافق مع ForgeGradle 2.3
├── core/                             Java 8 خالص، بدون أي كلاس من Minecraft
│   ├── src/main/java/com/realisticworld/
│   │   ├── util/      MathUtil, Rng (نفس تسلسل نسخة Bedrock بالضبط)
│   │   ├── core/      Engine, Config, DataStore, Performance, Scheduler, RwLog, Hud, SelfTest
│   │   ├── sim/       Regions (LOD + سجلات القطاعات + مدة الغياب)
│   │   ├── weather/   WeatherModel, Climate, WeatherSystem, ClimateSystem, FogSystem
│   │   └── water/     WaterPhysics, Basin, TankScan, Bodies, WaterSystem
│   └── src/test/java/  52 اختبار (JUnit 4)
└── forge/                            (J2) مود Forge 1.12.2 فوق core
```

## الاختبارات (J1)
`JAVA_HOME=/usr/lib/jvm/java-8-openjdk-amd64 ./gradlew :core:test`

| الملف | يغطي |
|---|---|
| `EngineTest` (8) | الجدولة وحد الوقت والعدالة، عزل الأعطال، الطابور، الإيقاف، الإبطاء التلقائي، الإعدادات، **تسلسل RNG مطابق لـ JavaScript بالضبط** |
| `RegionsTest` (9) | حلقات LOD، المناطق غير المحمّلة، عدة لاعبين، مدة الغياب، الحفظ وإعادة الفتح، بيانات الأنظمة، حد الذاكرة، تخطي إعادة الحساب، القطاع التالف |
| `WeatherTest` (16) | الحتمية والاستمرار بعد الحفظ، خطوة كبيرة = صغيرة، **إحصائيات 300 يوم** (نفس النطاقات)، LOW/NORMAL/HIGH، الجفاف، الفرض، التوقعات، المناخ والتربة والضباب، ربط طقس اللعبة واتباع `/weather`، المناخ القريب/البعيد، التعويض، الضباب بالخارج |
| `WaterTest` (14) | الضغط وتورشيلي، **الاختبار الإلزامي: يطابق الحل التحليلي ضمن 5%**، المسح والفتحات والمساحة الداخلية، الماء الطبيعي، المصارف والصمامات والمطر والتجمد، الأواني المستطرقة، الأنابيب، الملء والفيضان، الأرض المستوية، البرك، التعويض، الحفظ والاسترجاع |
| `WaterWorldTest` (5) | **السيناريو الإلزامي كامل عبر الـ Engine بعالم بلوكات:** كسر الجدار → الاكتشاف → المستوى ينزل ويوقف عند الفتحة → الماء بالحفرة (توازن الكتلة) → الصندوق سليم → الصمام → البحيرة الطبيعية ما تنصرف → **ما انبدّل أي بلوك غير الهواء/الماء** → طبقات كاملة → الحفظ وإعادة الفتح → سد الفتحة → **إعادة الملء ما تبدّل بلوك وضعه اللاعب** (اختبار متحقق منه بطفرة متعمدة) + الأنابيب + برك المطر + الفحص الذاتي + الإعدادات |

## القرارات التقنية (Java)
1. **Forge 1.12.2** (14.23.5.2860)، ForgeGradle 2.3، Gradle 4.10.3، Java 8. الكود يترجم بـ Java 8 (`sourceCompatibility 1.8`).
2. **فصل كامل:** `core` ما يعرف شي عن Minecraft. طبقة Forge توفر `Engine.Host` فقط (العالم، اللاعبين، البلوكات). نفس أسلوب نسخة Bedrock، وهذا اللي سمح بنقل كل الاختبارات.
3. **الحفظ:** `DataStore` (شجرة Map/List) تتحول كلها لـ NBT داخل `WorldSavedData`. ماين كرافت يكتبها لملف مؤقت ثم يعيد تسميته، فالانهيار أثناء الحفظ يبقي الملف السابق. ما نحتاج تقسيم Bedrock (حد 32767 بايت).
4. **البايومات:** الحرارة والأمطار تُقرأ من اللعبة نفسها (`Biome#getDefaultTemperature`, `getRainfall`) والماء من `BiomeDictionary` (OCEAN/RIVER/SWAMP/BEACH). ما نحتاج جدول ثابت.
5. **المواقع المضغوطة:** `long` بنفس تخطيط `BlockPos.toLong` بدل النصوص (أسرع وأقل ذاكرة).
6. **العميل والسيرفر:** المحاكاة على السيرفر. الضباب والـ HUD والواجهة على العميل، وتنتقل برسائل شبكة صغيرة (SimpleNetworkWrapper).
7. **الماء:** نفس قواعد الأمان: تغيير البلوكات فقط عبر فلتر أنواع، والطبقة كلها بنفس الـ tick.

## خطة J2 (طبقة Forge)
1. `forge/build.gradle` بـ ForgeGradle 2.3، يعتمد على `core` ويضمّن كلاساته بالـ jar.
2. `RealisticWorldMod` (@Mod) + Proxy للعميل والسيرفر.
3. `ForgeHost implements Engine.Host`: اللاعبين، `world.isBlockLoaded`، `WorldInfo` للمطر والرعد، البايوم، `getHeight`، `setBlockState` بفلتر الأنواع.
4. `RwSavedData extends WorldSavedData`: تحويل DataStore ↔ NBT.
5. `WorldTickEvent` ← `engine.tick()` (Overworld)، ومراقبة تغيّر الطقس لاتباع `/weather`.
6. الأحداث: `BlockEvent.BreakEvent`، `ExplosionEvent.Detonate`، `BlockEvent.PlaceEvent`، `PlayerInteractEvent.RightClickBlock` (الصمام، المقياس).
7. البلوكات: Valve (`PropertyBool OPEN`)، Pipe، Drain، Puddle (AABB رقيق، طبقة شفافة). العناصر: Control Center، Water Gauge. الوصفات JSON، الموديلات، النصوص `en_us.lang`.
8. الأوامر: `/rw menu|status|selftest`.
9. الشبكة: فتح الواجهة، بيانات اللوحة، أسطر HUD، نوع الضباب.
10. العميل: `GuiScreen` لمركز التحكم (Dashboard, Weather, Water, Systems, Performance, General, Error Log, Self-Test)، `RenderGameOverlayEvent.Text` للـ HUD، `EntityViewRenderEvent.FogDensity/FogColors` للضباب.

## خطة J3
تشغيل سيرفر Forge 1.12.2 حقيقي هنا (بدون واجهة، Java 8 منصّب)، وأوامر من الكونسول تبني خزان وتكسر جدار، ونقرأ النتيجة من السجل والعالم. هذا اختبار داخل اللعبة الحقيقية لكل جزء السيرفر. الواجهة والضباب (العميل) تحتاج تجربتك.
