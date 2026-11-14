# REALISTIC WORLD – ULTIMATE EARTH SIMULATION

Add-on لماين كرافت Bedrock يحوّل العالم لبيئة طبيعية تتفاعل أنظمتها مع بعض: الطقس، المياه، النباتات، الحيوانات، الحرائق، الكوارث، والبقاء. يستهدف الهواتف، والأداء أولوية.

**النسخة الحالية: 0.3.0 (PHASE 1 الأساس + PHASE 2 الطقس والمناخ + PHASE 3 فيزياء المياه).** راجع `PROJECT_STATUS.md` لمعرفة ما انبنى وما تبقى.

| | |
|---|---|
| اللعبة | Minecraft Bedrock 1.26.50+ (الهدف 1.26.52 / Android) |
| Script API | `@minecraft/server` 2.10.0، `@minecraft/server-ui` 2.2.0 (مستقرة، بدون Beta) |
| التجارب (Experiments) | غير مطلوبة |

## التثبيت
نزّل `dist/REALISTIC_WORLD.mcaddon` وافتحه بماين كرافت. التفاصيل: [docs/INSTALL_AR.md](docs/INSTALL_AR.md).

## الوثائق
- [دليل التثبيت](docs/INSTALL_AR.md)
- [دليل مركز التحكم](docs/CONTROL_CENTER_AR.md)
- [حل المشاكل](docs/TROUBLESHOOTING_AR.md)
- [تقرير الاختبارات](docs/TEST_REPORT.md)
- [حالة المشروع](PROJECT_STATUS.md)

## الأنظمة

| النظام | الحالة |
|---|---|
| المحرك، الإعدادات، التخزين المحمي، مدير الأداء، مناطق LOD | ✅ PHASE 1 |
| مركز التحكم، HUD، الفحص الذاتي | ✅ PHASE 1 |
| الطقس والمناخ: نموذج طقس، توقعات، مناخ 89 بايوم، رطوبة التربة، ضباب | ✅ PHASE 2 |
| فيزياء المياه: خزانات، تسرب (تورشيلي)، ضغط، صمامات، أنابيب، مصارف، برك، فيضانات، امتصاص، تبخر، تجمد | ✅ PHASE 3 |
| النباتات، التربة، الفصول | ⏳ PHASE 4 |
| الحيوانات والنظام البيئي | ⏳ PHASE 5 |
| الحرائق، الكوارث، الجيولوجيا | ⏳ PHASE 6 |
| البقاء، الغلاف الجوي، الأصوات | ⏳ PHASE 7 |

## هيكل المشروع
```
REALISTIC_WORLD/
├── RealisticWorld_BP/
│   ├── manifest.json
│   ├── blocks/                        Valve, Pipe, Drain, Puddle
│   ├── items/                         Control Center, Water Gauge
│   ├── recipes/
│   ├── loot_tables/
│   └── scripts/
│       ├── main.js                    نقطة البداية: الأوامر، الأحداث، حلقة المحرك
│       ├── core/
│       │   ├── constants.js
│       │   ├── host.js                الربط مع اللعبة (الوحيد اللي يلمس world/players)
│       │   ├── runtime.js             تجميع كل الأنظمة
│       │   ├── scheduler.js           المحرك المركزي
│       │   ├── performance.js         ملفات الأداء والإبطاء التلقائي
│       │   ├── config.js              الإعدادات
│       │   ├── storage.js             تخزين محمي من التلف
│       │   ├── logger.js
│       │   └── selfTest.js
│       ├── simulation/regions.js      مناطق المحاكاة و LOD
│       ├── weather/                   نموذج الطقس، المناخ، الضباب، جدول البايومات
│       ├── water/                     فيزياء المياه، مسح الخزانات، الملء والفيضان، الرسم
│       ├── ui/                        مركز التحكم، النماذج، HUD
│       └── utils/math.js
├── RealisticWorld_RP/                 manifest، أيقونة، textures، models، نصوص، fogs/
├── tests/                             135 اختبار + محاكي للعبة (tests/mocks)
├── scripts/                           validate، build، benchmark، make_textures، gen_biome_climate
├── types/                             تعريف console لمحرك Bedrock
└── docs/
```

## للمطوّر
```bash
npm install
npm run check     # validate + typecheck على تعريفات Mojang الرسمية + 135 اختبار
npm run build     # dist/*.mcaddon و *.mcpack
```
