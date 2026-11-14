# اختبار المود على سيرفر Forge حقيقي

الأدوات اللي انفحص بيها المود (النتائج بـ `docs/TEST_REPORT.md`).

## التجهيز (مرة وحدة)
```bash
mkdir server && cd server
curl -LO https://maven.minecraftforge.net/net/minecraftforge/forge/1.12.2-14.23.5.2860/forge-1.12.2-14.23.5.2860-installer.jar
java -jar forge-1.12.2-14.23.5.2860-installer.jar --installServer .      # Java 8
echo eula=true > eula.txt
cp ../tools/server-test/server.properties .                              # seed ثابت، بدون حماية spawn
mkdir mods && cp ../release/realisticworld-1.12.2-0.3.1.jar mods/
```

## التشغيل
```bash
tools/server-test/start-server.sh server                 # يشغّل السيرفر بالخلفية، الكونسول من server/in
SERVER=server tools/server-test/console.sh "rw status" "rw selftest"
cd tools/server-test && npm install && node playerTest.js ../../server   # لاعب حقيقي: 21 فحص
SERVER=server tools/server-test/console.sh stop
```

`playerTest.js` يبني خزاناته قرب نقطة الـ spawn لهذا الـ seed (-124, 256) فشغّله على عالم جديد.
