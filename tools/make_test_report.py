#!/usr/bin/env python3
"""Builds docs/TEST_REPORT.md from tests/results/{server,client}_results.json and the screenshots."""
import json
import os
import shutil

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
RES = os.path.join(ROOT, 'tests', 'results')
DOCS = os.path.join(ROOT, 'docs')
SHOTS_OUT = os.path.join(DOCS, 'screenshots')


def load(name):
    p = os.path.join(RES, name)
    return json.load(open(p)) if os.path.exists(p) else []


server = load('server_results.json')
client = load('client_results.json')
persist = load('persistence_results.json')
os.makedirs(SHOTS_OUT, exist_ok=True)
shots = []
sd = os.path.join(RES, 'screenshots')
if os.path.isdir(sd):
    for f in sorted(os.listdir(sd)):
        shutil.copy(os.path.join(sd, f), os.path.join(SHOTS_OUT, f))
        shots.append(f)

REQUIRED = [
    ('1', 'وضع دلو ماء على الأرض', ['1 bucket on the ground', 'singleplayer']),
    ('2', 'ملء خزان بالمياه', ['2 fill a 10000 L tank with buckets']),
    ('3', 'ثقب جدار الخزان', ['3-5,7 hole in a tank wall']),
    ('4', 'خروج المياه من الثقب', ['3-5,7 hole in a tank wall']),
    ('5', 'انخفاض مستوى المياه', ['3-5,7 hole in a tank wall']),
    ('6', 'اختلاف سرعة التسرب حسب حجم الثقب', ['6 leak speed depends on hole size', '6b several holes share the outflow by depth']),
    ('7', 'إغلاق الثقب', ['3-5,7 hole in a tank wall']),
    ('8', 'توصيل خزانين بأنبوب', ['8 two tanks connected by a pipe']),
    ('9', 'إعادة تشغيل العالم والتأكد من حفظ حالة المياه', ['9 restart persistence', 'persistence stress (3 restarts while water moves)']),
]


def table(rows):
    out = ['| الاختبار / Test | الفحص / Check | النتيجة | القياس الفعلي / Measured |', '|---|---|---|---|']
    for r in rows:
        out.append('| %s | %s | %s | %s |' % (r['test'], r['check'], '✅ PASS' if r['pass'] else '❌ FAIL', r['detail'].replace('|', '/')))
    return out


ps = sum(r['pass'] for r in server)
pc = sum(r['pass'] for r in client)
L = ['# تقرير الاختبارات — Test report', '',
     'كل الاختبارات أدناه نُفّذت آليًا على **Minecraft Forge 1.12.2-14.23.5.2860 حقيقي** (Java 8): سيرفر مخصص (dedicated server)، '
     'وعميل Minecraft حقيقي يعمل دون شاشة (Xvfb + Mesa llvmpipe OpenGL 4.5) في وضع اللعب الفردي (integrated server) واللعب الجماعي (متصل بالسيرفر عبر الشبكة). '
     'المود المُختبر هو ملف `build/libs/UltraRealisticWaterPhysics-1.12.2-1.0.0.jar` نفسه (بعد إعادة التعمية إلى أسماء SRG) موضوعًا في مجلد `mods`.', '',
     '- السيرفر: **%d/%d** فحصًا ناجحًا — `tests/test_server.py`' % (ps, len(server)),
     '- العميل (فردي + جماعي): **%d/%d** فحصًا ناجحًا — `tests/test_client.py`' % (pc, len(client)),
     '- اختبار الحفظ المتكرر (3 إعادات تشغيل والماء يتحرك): **%d/%d** — `tests/test_persistence.py`' % (sum(r['pass'] for r in persist), len(persist)), '',
     'طريقة القياس: كل سيناريو يُبنى بأوامر حقيقية (`/fill`, `/setblock`) واستخدام الدلو يتم عبر كود الدلو الأصلي في Minecraft (لاعب وهمي على السيرفر، ويد اللاعب الحقيقي في العميل). '
     'القياسات من الأمر `/water` (مخرجات key=value)، والمعدلات محسوبة بعدد tick السيرفر (20 tick = 1 ثانية) فلا يؤثر بطء الجهاز على النتائج. '
     'السجلات الكاملة في `tests/results/` (ملفات .log تُنشأ عند التشغيل).', '',
     '## الحالات التسع المطلوبة', '', '| # | الحالة | النتيجة |', '|---|---|---|']
for num, name, tests in REQUIRED:
    rows = [r for r in server + client + persist if r['test'] in tests]
    ok = rows and all(r['pass'] for r in rows)
    L.append('| %s | %s | %s (%d فحوص) |' % (num, name, '✅' if ok else '❌', len(rows)))
L += ['', '## نتائج السيرفر المخصص (Dedicated server)', ''] + table(server)
L += ['', '## نتائج العميل (Singleplayer + Multiplayer)', ''] + table(client)
L += ['', '## اختبار الحفظ المتكرر', ''] + table(persist)
L += ['', '## لقطات الشاشة (من العميل الحقيقي أثناء الاختبار)', '']
captions = {
    'sp_02': 'خزانات زجاجية ممتلئة + بركة الدلو على الأرض',
    'sp_03': 'لحظة فتح ثقب في جدار الخزان: تيار ماء ساقط ورذاذ',
    'sp_04': 'استمرار التسرب: انتشار الماء على الأرض',
    'sp_05': 'المستوى ينخفض والماء ينتشر حسب التضاريس',
    'sp_06': 'مصرف في أرضية خزان: بداية الدوامة',
    'sp_07': 'الدوامة: فقاعات تدور نحو المصرف وعمود ماء ساقط',
    'sp_08': 'مضخة ترفع الماء في أنبوب زجاجي (الماء ظاهر داخل الأنبوب) ويخرج من طرفه',
    'sp_09': 'شاشة مقياس المياه: الحجم، المستوى، الضغط، السرعة',
    'sp_10': 'واجهة الإعدادات داخل اللعبة',
    'mp_01': 'لعب جماعي: العميل يعرض تسرب خزان يحاكيه السيرفر',
    'mp_02': 'لعب جماعي: بعد ثوانٍ',
}
for f in shots:
    cap = captions.get(f[:5], f)
    L += ['**%s** — `%s`' % (cap, f), '', '![%s](screenshots/%s)' % (cap, f), '']
L += ['## ملاحظات صريحة', '',
      '- الأصوات غير متاحة في بيئة الاختبار (لا يوجد جهاز صوت) — لم تُختبر الأصوات.',
      '- الأداء مُقاس على 4 أنوية دون كرت شاشة (رسم برمجي llvmpipe)، لذلك أرقام FPS في العميل منخفضة ولا تمثل جهازًا حقيقيًا.',
      '- لم يُختبر مع OptiFine أو shaders أو مودات أخرى.', '']
os.makedirs(DOCS, exist_ok=True)
with open(os.path.join(DOCS, 'TEST_REPORT.md'), 'w') as f:
    f.write('\n'.join(L) + '\n')
print('wrote docs/TEST_REPORT.md: server %d/%d, client %d/%d, %d screenshots' % (ps, len(server), pc, len(client), len(shots)))
