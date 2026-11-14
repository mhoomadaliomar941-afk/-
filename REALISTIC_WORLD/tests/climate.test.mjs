import test from "node:test";
import assert from "node:assert/strict";
import {
  biomeInfo, vanillaToCelsius, temperatureAt, meanTemperature, precipitationType, soilRates, stepSoil, catchUpSoil, localFog,
  LAPSE_PER_BLOCK,
} from "../RealisticWorld_BP/scripts/weather/climate.js";
import { BIOME_CLIMATE } from "../RealisticWorld_BP/scripts/weather/biomeClimate.js";

const calm = { A: 0, I: 0, H: 0.5, raining: false, wind: 3, fogG: 0, dryDays: 0 };
const NOON = 6000;
const AFTERNOON = 8000;
const DAWN = 23500;

test("vanilla temperature maps to sensible °C, monotonic", () => {
  assert.equal(vanillaToCelsius(0.15), 0);
  assert.equal(vanillaToCelsius(-5), -20);
  assert.equal(vanillaToCelsius(9), 32);
  let prev = -Infinity;
  for (let t = -1; t <= 2.5; t += 0.05) {
    const c = vanillaToCelsius(t);
    assert.ok(c >= prev);
    prev = c;
  }
});

test("all 89 official biomes are covered; unknown biomes fall back safely", () => {
  assert.equal(Object.keys(BIOME_CLIMATE).length, 89);
  for (const id of Object.keys(BIOME_CLIMATE)) assert.equal(biomeInfo(id).known, true);
  const custom = biomeInfo("mymod:crystal_valley");
  assert.equal(custom.known, false);
  assert.ok(Number.isFinite(custom.baseC));
  assert.equal(biomeInfo("plains").id, "minecraft:plains", "short ids accepted");
});

test("biome ordering: desert > jungle > plains > taiga > ice plains", () => {
  const t = (id) => meanTemperature(biomeInfo(id), 64, calm);
  assert.ok(t("minecraft:desert") > t("minecraft:jungle"));
  assert.ok(t("minecraft:jungle") > t("minecraft:plains"));
  assert.ok(t("minecraft:plains") > t("minecraft:taiga"));
  assert.ok(t("minecraft:taiga") > t("minecraft:ice_plains"));
  assert.ok(t("minecraft:ice_plains") < 0);
});

test("day/night swing: dry biomes swing more than water", () => {
  const swing = (id) => temperatureAt(biomeInfo(id), 64, AFTERNOON, calm) - temperatureAt(biomeInfo(id), 64, AFTERNOON + 12000, calm);
  assert.ok(swing("minecraft:desert") > 15, `desert ${swing("minecraft:desert")}`);
  assert.ok(swing("minecraft:ocean") < 5);
  assert.ok(temperatureAt(biomeInfo("minecraft:plains"), 64, AFTERNOON, calm) > temperatureAt(biomeInfo("minecraft:plains"), 64, DAWN, calm));
});

test("mountains are colder, rain cools, anomaly adds", () => {
  const p = biomeInfo("minecraft:plains");
  const low = temperatureAt(p, 64, NOON, calm);
  assert.ok(Math.abs(low - temperatureAt(p, 200, NOON, calm) - 136 * LAPSE_PER_BLOCK) < 1e-9);
  assert.ok(temperatureAt(p, 64, NOON, { ...calm, raining: true, I: 0.5 }) < low);
  assert.ok(Math.abs(temperatureAt(p, 64, NOON, { ...calm, A: 5 }) - low - 5) < 1e-9);
});

test("precipitation type follows temperature", () => {
  const rain = { raining: true, I: 0.2, wind: 3 };
  assert.equal(precipitationType(10, rain), "rain");
  assert.equal(precipitationType(1, rain), "sleet");
  assert.equal(precipitationType(-3, rain), "snow");
  assert.equal(precipitationType(-3, { raining: true, I: 0.5, wind: 14 }), "blizzard");
  assert.equal(precipitationType(-3, { raining: false, I: 0, wind: 0 }), "none");
});

test("soil equation: exact, so splitting time does not change the result", () => {
  const rates = soilRates(biomeInfo("minecraft:plains"), 18, { raining: true, I: 0.3, H: 0.7 });
  const once = stepSoil(0.2, rates, 10);
  const split = stepSoil(stepSoil(0.2, rates, 3.5), rates, 6.5);
  assert.ok(Math.abs(once - split) < 1e-12);
});

test("rain wets the soil, dry heat dries it, frozen ground keeps it", () => {
  const p = biomeInfo("minecraft:plains");
  const wet = stepSoil(0.3, soilRates(p, 15, { raining: true, I: 0.3, H: 0.8 }), 6);
  assert.ok(wet > 0.6, `after 6 h of rain: ${wet}`);
  const dry = stepSoil(0.6, soilRates(p, 32, { raining: false, I: 0, H: 0.3 }), 48);
  assert.ok(dry < 0.5, `after 2 hot days: ${dry}`);
  const frozen = soilRates(p, -5, { raining: true, I: 0.5, H: 0.9 });
  assert.equal(frozen.gain, 0, "snow does not soak in");
  assert.equal(frozen.loss, 0, "frozen ground does not evaporate");
  for (const sm of [0, 0.5, 1]) {
    const v = stepSoil(sm, soilRates(p, 40, { raining: true, I: 1, H: 0 }), 1000);
    assert.ok(v >= 0 && v <= 1);
  }
});

test("sand dries faster than swamp", () => {
  const w = { raining: false, I: 0, H: 0.4 };
  const desert = stepSoil(0.5, soilRates(biomeInfo("minecraft:desert"), 30, w), 24);
  const swamp = stepSoil(0.5, soilRates(biomeInfo("minecraft:swampland"), 30, w), 24);
  assert.ok(desert < swamp);
});

test("catch-up: a long absence reaches the climate's balance in one step", () => {
  const w = { A: 0, I: 0, H: 0.6, raining: false, rainEma: 0.25 };
  const swamp = biomeInfo("minecraft:swampland");
  const desert = biomeInfo("minecraft:desert");
  const a = catchUpSoil(0.05, swamp, 64, w, 24 * 30);
  const b = catchUpSoil(0.95, swamp, 64, w, 24 * 30);
  assert.ok(Math.abs(a - b) < 0.01, "start value forgotten after a month");
  assert.ok(catchUpSoil(0.5, desert, 64, w, 24 * 30) < a, "desert balance is drier");
  assert.equal(catchUpSoil(0.5, swamp, 64, w, 0), 0.5, "no time, no change");
});

test("local fog: dawn over water, heavy rain, blizzard, heat haze, wind clears it", () => {
  const river = biomeInfo("minecraft:river");
  const plains = biomeInfo("minecraft:plains");
  const desert = biomeInfo("minecraft:desert");
  assert.equal(localFog(river, DAWN, 10, { ...calm, H: 0.8 }).fog, "rw:fog_mist", "calm humid dawn over water: mist");
  assert.equal(localFog(river, DAWN, 10, { ...calm, H: 0.8, fogG: 0.3 }).fog, "rw:fog_dense", "plus a foggy air mass: dense");
  assert.equal(localFog(plains, NOON, 15, { ...calm, H: 0.8 }).fog, null, "same air at noon inland: clear");
  assert.equal(localFog(plains, NOON, 15, { ...calm, wind: 12 }).fog, null);
  assert.equal(localFog(plains, NOON, 15, { ...calm, raining: true, I: 0.5 }).fog, "rw:fog_rain");
  assert.equal(localFog(plains, NOON, -8, { ...calm, raining: true, I: 0.5, wind: 14 }).fog, "rw:fog_blizzard");
  assert.equal(localFog(desert, NOON, 38, { ...calm, A: 6 }).fog, "rw:fog_haze");
  assert.equal(localFog(plains, NOON, 15, { ...calm, fogG: 0.6 }).fog, "rw:fog_mist");
});
