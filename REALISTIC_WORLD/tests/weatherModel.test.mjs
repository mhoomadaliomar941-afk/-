import test from "node:test";
import assert from "node:assert/strict";
import {
  createState, step, classify, forecast, forceWeather, releaseForce, sanitizeState, vanillaWeather, compass, rainClass,
  FORCEABLE, MAX_STEP_TICKS, TICKS_PER_DAY,
} from "../RealisticWorld_BP/scripts/weather/model.js";

const clone = (o) => JSON.parse(JSON.stringify(o));

/** Run `days` and collect statistics. */
function simulate(seed, days, rainFrequency = "NORMAL") {
  const st = createState({ s: seed });
  const out = { samples: 0, rain: 0, storm: 0, light: 0, moderate: 0, heavy: 0, events: [], dry: [], conditions: new Set() };
  let run = 0;
  let dry = 0;
  let prev = false;
  for (let t = 0; t < days * TICKS_PER_DAY; t += MAX_STEP_TICKS) {
    step(st, MAX_STEP_TICKS, { rainFrequency });
    out.samples++;
    out.conditions.add(classify(st));
    if (st.raining) {
      out.rain++;
      if (st.storm) out.storm++;
      out[{ LIGHT_RAIN: "light", MODERATE_RAIN: "moderate", HEAVY_RAIN: "heavy" }[rainClass(st.I)]]++;
      run += 0.25;
      if (!prev && dry) {
        out.dry.push(dry);
        dry = 0;
      }
    } else {
      dry += 0.25;
      if (prev) {
        out.events.push(run);
        run = 0;
      }
    }
    prev = st.raining;
    for (const k of ["P", "A", "H", "I", "wind", "windDir", "fogG", "dryDays", "rainEma"]) {
      assert.ok(Number.isFinite(st[k]), `${k} became ${st[k]}`);
    }
  }
  return out;
}
const mean = (a) => a.reduce((x, y) => x + y, 0) / a.length;

test("same seed gives the same weather; different seeds differ", () => {
  const a = createState({ s: 42 });
  const b = createState({ s: 42 });
  const c = createState({ s: 43 });
  step(a, 50000);
  step(b, 50000);
  step(c, 50000);
  assert.deepEqual(a, b);
  assert.notDeepEqual(a, c);
});

test("saving and loading mid-way continues exactly the same", () => {
  const a = createState({ s: 7 });
  step(a, 30000);
  const restored = sanitizeState(JSON.parse(JSON.stringify(a)));
  step(a, 30000);
  step(restored, 30000);
  assert.deepEqual(restored, a);
});

test("one big step equals many small ones (catch-up is consistent)", () => {
  const a = createState({ s: 9 });
  const b = createState({ s: 9 });
  step(a, 5000);
  for (let i = 0; i < 20; i++) step(b, 250);
  assert.deepEqual(a, b);
});

test("NORMAL frequency: realistic amounts and rhythm of rain (5 seeds x 60 days)", () => {
  const all = [1, 2, 3, 4, 5].map((s) => simulate(s * 7919, 60));
  const rainFrac = mean(all.map((r) => r.rain / r.samples));
  const stormFrac = mean(all.map((r) => r.storm / r.samples));
  const eventH = mean(all.flatMap((r) => r.events));
  const dryH = mean(all.flatMap((r) => r.dry));
  assert.ok(rainFrac > 0.18 && rainFrac < 0.35, `rain ${rainFrac}`);
  assert.ok(stormFrac > 0.005 && stormFrac < 0.06, `storms ${stormFrac}`);
  assert.ok(eventH > 3 && eventH < 12, `rain events last ${eventH} h`);
  assert.ok(dryH > 8, `dry spells last ${dryH} h`);
  const rainy = all.reduce((n, r) => n + r.rain, 0);
  const light = all.reduce((n, r) => n + r.light, 0) / rainy;
  const heavy = all.reduce((n, r) => n + r.heavy, 0) / rainy;
  assert.ok(light > 0.3 && light < 0.7, `light share ${light}`);
  assert.ok(heavy > 0.08 && heavy < 0.4, `heavy share ${heavy}`);
});

test("rain frequency setting orders LOW < NORMAL < HIGH; droughts come with LOW", () => {
  const frac = (f) => mean([11, 22, 33].map((s) => { const r = simulate(s, 40, f); return r.rain / r.samples; }));
  const low = frac("LOW");
  const normal = frac("NORMAL");
  const high = frac("HIGH");
  assert.ok(low < normal && normal < high, `${low} ${normal} ${high}`);
  const lowConditions = new Set([11, 22, 33, 44, 55].flatMap((s) => [...simulate(s, 60, "LOW").conditions]));
  assert.ok(lowConditions.has("DROUGHT"), "no drought in 300 dry-climate days");
  assert.ok(lowConditions.has("HEATWAVE"));
});

test("every forceable condition is applied and then released naturally", () => {
  for (const type of Object.keys(FORCEABLE)) {
    const st = createState({ s: 5 });
    step(st, 2000);
    forceWeather(st, type, 6000);
    step(st, 3000);
    assert.equal(classify(st), type, `forced ${type} shows as ${classify(st)}`);
    step(st, 3500);
    assert.equal(st.force, null, `${type} still forced after its time`);
  }
});

test("forced rain starts at once; vanilla weather mapping", () => {
  const st = createState({ s: 1 });
  forceWeather(st, "THUNDERSTORM", 1000);
  step(st, 1);
  assert.equal(vanillaWeather(st), "Thunder");
  forceWeather(st, "LIGHT_RAIN", 1000);
  step(st, 1);
  assert.equal(vanillaWeather(st), "Rain");
  forceWeather(st, "CLEAR", 1000);
  assert.equal(vanillaWeather(st), "Clear");
  releaseForce(st);
  assert.equal(st.force, null);
  assert.throws(() => forceWeather(st, "TORNADO", 10));
});

test("sanitizeState rejects broken data and clamps ranges", () => {
  assert.equal(sanitizeState(null), null);
  assert.equal(sanitizeState({ v: 2 }), null);
  const bad = clone(createState({ s: 3 }));
  bad.H = "wet";
  assert.equal(sanitizeState(bad), null);
  const odd = clone(createState({ s: 3 }));
  odd.H = 5;
  odd.force = { type: "NOPE", until: 1 };
  const fixed = sanitizeState(odd);
  assert.equal(fixed.H, 1);
  assert.equal(fixed.force, null);
});

test("forecast: 12 periods, does not change the real state, member 0 is the real future", () => {
  const st = createState({ s: 2024 });
  step(st, 40000);
  const before = clone(st);
  const fc = forecast(st);
  assert.deepEqual(st, before, "forecast must not change the weather");
  assert.equal(fc.length, 12);
  for (const p of fc) {
    assert.ok(p.rainChance >= 0 && p.rainChance <= 100 && p.stormChance <= p.rainChance);
  }
  // Replay the real future: whenever it rains in a period, the forecast gave it a chance.
  for (const p of fc) {
    let rained = false;
    for (let k = 0; k < 24; k++) {
      step(st, 250);
      rained ||= st.raining;
    }
    if (rained) assert.ok(p.rainChance > 0, `it rained at +${p.fromHour}h but forecast said 0%`);
  }
});

test("compass directions", () => {
  assert.equal(compass(0), "N");
  assert.equal(compass(44), "NE");
  assert.equal(compass(270), "W");
  assert.equal(compass(-90), "W");
  assert.equal(compass(359), "N");
});
