// Weather section of the control center: local conditions, 3-day forecast,
// personal weather HUD, and operator controls (force / release / settings).

import { ActionFormData } from "@minecraft/server-ui";
import { FORCEABLE, RAIN_FREQUENCY } from "../weather/model.js";
import { ModalBuilder } from "./modal.js";
import { showForm } from "./forms.js";

const FORCE_TYPES = Object.keys(FORCEABLE);
const FREQUENCIES = Object.keys(RAIN_FREQUENCY);
/** One real minute in ticks. */
const MINUTE = 1200;

const PRECIP = { none: "none", rain: "rain", sleet: "sleet (rain and snow)", snow: "snow", blizzard: "blizzard" };
const FOG = { "rw:fog_mist": "mist", "rw:fog_dense": "dense fog", "rw:fog_rain": "rain haze", "rw:fog_blizzard": "whiteout", "rw:fog_haze": "heat haze" };

const pretty = (s) => s.replace(/_/g, " ").toLowerCase();

function duration(ticks) {
  const s = Math.round(ticks / 20);
  return s >= 60 ? `${Math.floor(s / 60)}m ${s % 60}s` : `${s}s`;
}

/**
 * @param {import("./controlCenter.js").ControlCenter} cc
 * @param {import("@minecraft/server").Player} player
 */
export async function weatherPage(cc, player) {
  const rt = cc.rt;
  const op = cc.prefs.isOperator(player);
  const w = rt.weather.summary();
  const pos = cc.prefs.position(player);
  const local = rt.climate.sample(pos.dim, pos.x, pos.y, pos.z);
  const lines = [];
  if (local) {
    lines.push(
      `§lHere§r (${pretty(local.biome)}${local.tier ? `, ${local.tier}` : ""})`,
      `Temperature: §f${local.tempC.toFixed(1)}°C§r${local.frost ? " §b(frost)§r" : ""}`,
      `Precipitation: ${PRECIP[/** @type {keyof typeof PRECIP} */ (local.precip)] ?? local.precip}`,
      `Air humidity: ${local.humidity}%   Soil moisture: ${local.soil === null ? "-" : `${Math.round(local.soil * 100)}%`}`,
      `Wind: ${local.wind} m/s from ${w.windFrom}   Fog: ${local.fog ? FOG[/** @type {keyof typeof FOG} */ (local.fog)] : "none"}`,
      local.drought ? "§6Drought: the ground here is very dry§r" : "",
      "",
    );
  }
  lines.push(
    `§lOverall§r: ${pretty(w.condition)}`,
    `Pressure: ${w.pressure} (${w.pressure < 0.4 ? "low" : w.pressure > 0.6 ? "high" : "normal"})   Temperature anomaly: ${w.anomaly >= 0 ? "+" : ""}${w.anomaly}°C`,
    `Days without rain: ${w.dryDays}`,
    w.forced ? `§eForced: ${pretty(w.forced.type)} (${duration(w.forced.ticksLeft)} left)§r` : "",
    rt.config.isSystemEnabled("weather") ? "" : "§cThe weather engine is switched off (Systems).§r",
  );

  const hudOn = cc.prefs.getWeatherHud(player);
  const form = new ActionFormData().title("Weather").body(lines.filter((l, i, a) => l !== "" || a[i - 1] !== "").join("\n"));
  const actions = [];
  const add = (label, fn) => {
    form.button(label);
    actions.push(fn);
  };
  add("Forecast (3 days)", () => forecastPage(cc, player));
  add(`Weather on my HUD: ${hudOn ? "ON" : "OFF"}`, () => {
    cc.prefs.setWeatherHud(player, !hudOn);
    return weatherPage(cc, player);
  });
  if (op) {
    add("Force weather...", () => forcePage(cc, player));
    if (w.forced) {
      add("Release forced weather", () => {
        rt.weather.release(rt.scheduler.ticks);
        player.sendMessage("§a[Realistic World] Weather returns to natural behaviour.");
        return weatherPage(cc, player);
      });
    }
    add("Weather settings", () => settingsPage(cc, player));
  }
  add("Back", () => cc.mainMenu(player));
  const res = await showForm(player, form);
  if (!res || res.canceled || res.selection === undefined) return;
  return actions[res.selection]();
}

/** @param {import("./controlCenter.js").ControlCenter} cc */
async function forecastPage(cc, player) {
  const rt = cc.rt;
  const periods = rt.weather.getForecast();
  const lines = [
    "Chance of rain and storms in each 6 in-game hours (1 in-game hour = 50 s).",
    "Worked out by replaying the weather model forward 6 times.",
    "",
  ];
  for (const p of periods) {
    const day = Math.floor(p.fromHour / 24) + 1;
    const rain = p.rainChance >= 50 ? `§9${p.rainChance}%§r` : `${p.rainChance}%`;
    const storm = p.stormChance > 0 ? `  storm §e${p.stormChance}%§r` : "";
    const temp = `${p.anomalyMin >= 0 ? "+" : ""}${p.anomalyMin}..${p.anomalyMax >= 0 ? "+" : ""}${p.anomalyMax}°C`;
    lines.push(`Day ${day} +${p.fromHour}-${p.toHour}h: rain ${rain}${storm}  wind ≤${p.maxWind}  temp ${temp}`);
  }
  if (rt.weather.summary().forced) lines.push("", "§eA forced condition is active; the forecast includes it until it ends.§r");
  const res = await showForm(player, new ActionFormData().title("Forecast").body(lines.join("\n")).button("Back"));
  if (res && !res.canceled) return weatherPage(cc, player);
}

/** @param {import("./controlCenter.js").ControlCenter} cc */
async function forcePage(cc, player) {
  const rt = cc.rt;
  const m = new ModalBuilder("Force weather")
    .label("Holds a condition, then the weather continues naturally from there. The world's rain and thunder follow it.")
    .dropdown("type", "Condition", FORCE_TYPES.map(pretty), 0)
    .slider("minutes", "Duration (real minutes)", 1, 60, 5)
    .submit("Force");
  const res = await showForm(player, m.form);
  if (!res || res.canceled || !res.formValues) return weatherPage(cc, player);
  const v = m.read(res.formValues);
  if (!v) return cc._layoutError(player);
  const type = FORCE_TYPES[/** @type {number} */ (v.type)];
  const minutes = Number(v.minutes);
  rt.weather.force(type, minutes * MINUTE, rt.scheduler.ticks);
  player.sendMessage(`§a[Realistic World] ${pretty(type)} for ${minutes} min.`);
  return weatherPage(cc, player);
}

/** @param {import("./controlCenter.js").ControlCenter} cc */
async function settingsPage(cc, player) {
  const rt = cc.rt;
  const m = new ModalBuilder("Weather settings")
    .dropdown("freq", "Rain frequency (LOW: ~20% of the time, NORMAL: ~26%, HIGH: ~33%)", FREQUENCIES, Math.max(0, FREQUENCIES.indexOf(rt.config.get("weather.rainFrequency"))))
    .toggle("sync", "Show the engine's weather in the world (rain, thunder, snow). Off: vanilla weather runs on its own and does not match the simulation.", rt.config.get("weather.syncVanilla"))
    .submit("Apply");
  const res = await showForm(player, m.form);
  if (!res || res.canceled || !res.formValues) return weatherPage(cc, player);
  const v = m.read(res.formValues);
  if (!v) return cc._layoutError(player);
  rt.config.set("weather.rainFrequency", FREQUENCIES[/** @type {number} */ (v.freq)]);
  rt.config.set("weather.syncVanilla", v.sync === true);
  rt.saveNow();
  player.sendMessage("§a[Realistic World] Weather settings applied.");
  return weatherPage(cc, player);
}
