# PROJECT_STATUS – REALISTIC WORLD (Java Edition 1.12.2)

آخر تحديث: 2026-11-14 · النسخة **0.3.1** (مطابقة لنسخة Bedrock 0.3.1)

## الهدف
نقل المود بالكامل لـ **Minecraft Java Edition 1.12.2** كمود **Forge** (المنصة المعتمدة لهذا الإصدار).

## الحالة: النقل مكتمل ✅

| المرحلة | الحالة |
|---|---|
| **J1 – النواة بـ Java 8** (المحرك، الإعدادات، الأداء، المناطق، الطقس، المناخ، الضباب، المياه، HUD، الفحص الذاتي، مركز التحكم) | ✅ **63/63 اختبار** |
| **J2 – طبقة Forge** (البلوكات، الأدوات، الوصفات، الأحداث، الواجهة، الـ HUD والضباب على العميل، الشبكة، الحفظ، الأوامر) | ✅ |
| **J3 – سيرفر Forge 1.12.2 حقيقي** + لاعب حقيقي متصل | ✅ **21/21**، 7 أخطاء اكتُشفت وتصلحت (5 منها موجودة بـ Bedrock وتصلحت هناك هم) |

**الملف الجاهز:** `release/realisticworld-1.12.2-0.3.1.jar`
**التنصيب والاستخدام:** `docs/INSTALL_AR.md` · **الاختبارات:** `docs/TEST_REPORT.md`

## ما يحتاج تجربتك
- الشكل على الشاشة (الواجهة، الضباب، الـ HUD، صور البلوكات) واللعب الفردي: السيرفر بدون شاشة، فانفحص محتوى الرسائل بس مو شكلها.

## البناء
```bash
export JAVA_HOME=/usr/lib/jvm/java-8-openjdk-amd64
./gradlew --max-workers=1 :core:test :forge:build     # الناتج: forge/build/libs/realisticworld-1.12.2-<version>.jar
```
البناء يحتاج الوصول لـ `maven.minecraftforge.net` و `libraries.minecraft.net` وخوادم Mojang (`launchermeta`, `launcher`, `piston-meta`, `piston-data`, `resources.download`).

## الهيكل
```
REALISTIC_WORLD_JAVA/
├── settings.gradle, build.gradle     Gradle 4.10.3 (wrapper)
├── core/                             Java 8 خالص، بدون أي كلاس من Minecraft
│   ├── src/main/java/com/realisticworld/
│   │   ├── util/      MathUtil, Rng (نفس تسلسل نسخة Bedrock بالضبط)
│   │   ├── core/      Engine, Config, DataStore, Performance, Scheduler, RwLog, Hud, SelfTest
│   │   ├── sim/       Regions (LOD + سجلات القطاعات + مدة الغياب)
│   │   ├── weather/   WeatherModel, Climate, WeatherSystem, ClimateSystem, FogSystem
│   │   ├── water/     WaterPhysics, Basin, TankScan, Bodies, WaterSystem
│   │   └── ui/        Form, ControlCenter (الصفحات تُبنى بالسيرفر وتنرسم عند اللاعب)
│   └── src/test/java/  63 اختبار (JUnit 4)
├── forge/                            مود Forge 1.12.2 فوق core
│   ├── RealisticWorldMod   الأحداث، الـ tick، الحفظ، النماذج، الـ gauge، الـ anchors
│   ├── ForgeHost           العالم الحقيقي لـ Engine.Host (لاعبين، بايومات، بلوكات، طقس)
│   ├── BlockValve, BlockPuddle, RwBlocks, CommandRw, PlayerPrefs, RwSavedData, Nbt
│   ├── net/RwNetwork       4 رسائل: نموذج، رد، HUD، ضباب
│   └── client/             GuiForm (أي نموذج من السيرفر)، ClientProxy (HUD، الضباب، النماذج)
├── release/                          ملف الـ jar الجاهز
├── docs/                             INSTALL_AR.md, TEST_REPORT.md
└── tools/server-test/                سيرفر حقيقي + لاعب مبرمج (21 فحص)
```

## القرارات التقنية (Java)
1. **Forge 1.12.2** (14.23.5.2860)، **ForgeGradle 3** (نفس الـ MDK الرسمي؛ ملفات ForgeGradle 2.3 ما عادت موجودة على مستودع Forge)، mappings `snapshot 20171003-1.12`، Gradle 4.10.3، Java 8. الكود يترجم بـ Java 8 (`sourceCompatibility 1.8`).
2. **فصل كامل:** `core` ما يعرف شي عن Minecraft. طبقة Forge توفر `Engine.Host` فقط (العالم، اللاعبين، البلوكات). نفس أسلوب نسخة Bedrock، وهذا اللي سمح بنقل كل الاختبارات.
3. **الحفظ:** `DataStore` (شجرة Map/List) تتحول كلها لـ NBT داخل `WorldSavedData`. ماين كرافت يكتبها لملف مؤقت ثم يعيد تسميته، فالانهيار أثناء الحفظ يبقي الملف السابق. ما نحتاج تقسيم Bedrock (حد 32767 بايت).
4. **البايومات:** الحرارة والأمطار تُقرأ من اللعبة نفسها (`Biome#getDefaultTemperature`, `getRainfall`) والماء من `BiomeDictionary` (OCEAN/RIVER/SWAMP/BEACH). ما نحتاج جدول ثابت.
5. **المواقع المضغوطة:** `long` بنفس تخطيط `BlockPos.toLong` بدل النصوص (أسرع وأقل ذاكرة).
6. **العميل والسيرفر:** المحاكاة على السيرفر. الضباب والـ HUD والواجهة على العميل، وتنتقل برسائل شبكة صغيرة (SimpleNetworkWrapper).
7. **الماء:** نفس قواعد الأمان: تغيير البلوكات فقط عبر فلتر أنواع، والطبقة كلها بنفس الـ tick.
8. **البركة (puddle) بمادة خاصة**، مو `Material.WATER`: كود الماء الأصلي يقرأ خاصية `level` من أي بلوك مادته ماء (هذا سبب انهيار اكتشفه السيرفر الحقيقي).
9. **إعادة مسح الخزان ما تغير الحجم** إذا السطح بمكانه، والرسم يتوقف أثناء المسح، والمسح المتأخر لنفس الماء يحدّث الخزان الموجود (`docs/TEST_REPORT.md`).
10. **`/rw anchor`:** ماين كرافت 1.12 يوقف الكائنات بعد 15 ثانية بدون لاعبين؛ الـ anchor يخلي المحاكاة تشتغل بمنطقة بدون لاعب (مثل ticking area بـ Bedrock).

## بعدين
المراحل الجاية (PHASE 4 وما بعد) تنبني بالنواة المشتركة بنفس الطريقة، فتطلع للنسختين.
