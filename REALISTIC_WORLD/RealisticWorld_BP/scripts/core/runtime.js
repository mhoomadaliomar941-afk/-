// Builds the whole add-on from its parts. Nothing here imports Minecraft
// modules: the game is reached only through the `host` adapter, so the full
// runtime also runs in Node tests.

import { ADDON_VERSION, KEYS, PROP_PREFIX, SERVER_MODULE_VERSION, SERVER_UI_MODULE_VERSION, TARGET_GAME_VERSION } from "./constants.js";
import { Logger } from "./logger.js";
import { Storage } from "./storage.js";
import { Config } from "./config.js";
import { PerformanceManager, suggestProfile } from "./performance.js";
import { Scheduler } from "./scheduler.js";
import { runSelfTest } from "./selfTest.js";
import { RegionStore, RegionTracker } from "../simulation/regions.js";
import { StatusHud } from "../ui/hud.js";
import { WeatherSystem } from "../weather/weatherSystem.js";
import { ClimateSystem } from "../weather/climateSystem.js";
import { FogSystem } from "../weather/fogSystem.js";
import { RAIN_FREQUENCY } from "../weather/model.js";
import { WaterSystem } from "../water/waterSystem.js";

/**
 * Everything the runtime needs from the game.
 * @typedef {{
 *   backend: import("./storage.js").StorageBackend,
 *   memoryTier?: number,
 *   getPlayerPositions: () => { dim: string, x: number, z: number }[],
 *   isChunkLoaded: (dim: string, x: number, z: number) => boolean,
 *   getHudPlayers: () => import("../ui/hud.js").HudPlayer[],
 *   worldSeed: string | number,
 *   setVanillaWeather: (type: "Clear" | "Rain" | "Thunder", durationTicks: number) => void,
 *   getBiomeId: (dim: string, x: number, y: number, z: number) => string,
 *   getSurfaceY: (dim: string, x: number, z: number) => number | undefined,
 *   getTimeOfDay: () => number,
 *   getFogPlayers: () => import("../weather/fogSystem.js").FogPlayer[],
 *   water: import("../water/waterSystem.js").WaterHost,
 *   clock?: () => number,
 *   log?: { info: Function, warn: Function, error: Function },
 * }} Host
 */

/** Interval of the save cycle in ticks (10 s). */
export const SAVE_INTERVAL_TICKS = 200;
/** Every this many save cycles (1 min), active regions record the current time. */
export const CHECKPOINT_EVERY_SAVES = 6;

/** @param {Host} host */
export function createRuntime(host) {
  const logger = new Logger({ sink: host.log ?? console });
  const storage = new Storage(host.backend, PROP_PREFIX);
  const config = new Config(storage, logger);

  // Restore errors from the last session so they can still be read.
  const savedErrors = storage.load(KEYS.errorLog);
  if (savedErrors.ok) logger.restoreErrors(savedErrors.value);
  let errorsDirty = false;
  logger.onError(() => {
    errorsDirty = true;
  });

  // Per-system switches must exist before settings are loaded.
  config.defineSystem("regions", "Region tracker (LOD)", true);
  config.defineSystem("hud", "Status HUD (action bar)", true);
  config.defineSystem("weather", "Weather engine", true);
  config.defineSystem("climate", "Regional climate (temperature, soil moisture)", true);
  config.defineSystem("fog", "Weather fog", true);
  config.define("weather.rainFrequency", {
    type: "enum", values: Object.keys(RAIN_FREQUENCY), default: "NORMAL", label: "Rain frequency",
  });
  config.define("weather.syncVanilla", { type: "bool", default: true, label: "Show the engine's weather in the game (rain, thunder)" });
  config.defineSystem("water", "Water physics", true);
  config.define("water.autoDetect", { type: "bool", default: true, label: "Detect tanks when a block next to water is broken" });
  config.define("water.rainPuddles", { type: "bool", default: true, label: "Rain on wet ground forms puddles and floods" });
  config.setDefault("profile", suggestProfile(host.memoryTier));
  const loadInfo = config.load();
  logger.setLevel(config.get("logLevel"));

  const perf = new PerformanceManager(config.get("profile"));
  perf.setAdaptive(config.get("adaptive"));

  const savedClock = storage.load(KEYS.clock);
  const startTick = savedClock.ok && Number.isInteger(savedClock.value?.tick) ? savedClock.value.tick : 0;
  const scheduler = new Scheduler({ perf, logger, clock: host.clock, startTick });
  logger.tickSource = () => scheduler.ticks;

  const regionStore = new RegionStore(storage, logger, perf.profile.maxCachedRegions);
  const tracker = new RegionTracker({
    store: regionStore,
    perf,
    getPlayers: host.getPlayerPositions,
    isLoaded: host.isChunkLoaded,
  });
  tracker.onResume((r, away) => logger.debug("regions", `${r.dim} ${r.cx},${r.cz} resumed after ${away} ticks`));

  const weather = new WeatherSystem({
    storage,
    config,
    logger,
    setVanillaWeather: host.setVanillaWeather,
    worldSeed: host.worldSeed,
  });
  const climate = new ClimateSystem({
    tracker,
    store: regionStore,
    weather,
    getBiomeId: host.getBiomeId,
    getSurfaceY: host.getSurfaceY,
    getTimeOfDay: host.getTimeOfDay,
    logger,
  });
  const fog = new FogSystem({ getPlayers: host.getFogPlayers, climate, logger });
  const water = new WaterSystem({
    storage, config, logger, tracker, store: regionStore, climate, weather, perf,
    enqueue: (key, owner, fn) => scheduler.enqueue(key, owner, fn),
    host: host.water,
    now: () => scheduler.ticks,
  });

  const hud = new StatusHud({
    getPlayers: host.getHudPlayers,
    weatherLine: (pos) => {
      const c = climate.sample(pos.dim, pos.x, pos.y, pos.z);
      return c ? StatusHud.formatWeather(weather.summary(), c) : null;
    },
    status: () => ({
      avgTickMs: perf.avgTickMs,
      budgetMs: perf.profile.tickBudgetMs,
      throttle: perf.throttle,
      near: tracker.counts.NEAR,
      medium: tracker.counts.MEDIUM,
      far: tracker.counts.FAR,
      queue: scheduler.queueSize(),
      paused: scheduler.paused,
    }),
  });

  /**
   * Save everything that changed. Region sectors are capped per call by the
   * profile so a periodic save never stalls a tick; pass Infinity for a full
   * save (world closing, player leaving, "Save now").
   */
  function saveNow(maxSectors = perf.profile.sectorWritesPerSave) {
    const t0 = (host.clock ?? Date.now)();
    const full = maxSectors === Infinity;
    if (full || persistence.saves % CHECKPOINT_EVERY_SAVES === 0) tracker.checkpoint(scheduler.ticks);
    let writes = 0;
    if (storage.save(KEYS.clock, { tick: scheduler.ticks }).ok) writes++;
    if (config.flush()) writes++;
    if (weather.save()) writes++;
    writes += water.save(full ? Infinity : 8);
    writes += regionStore.flush(maxSectors);
    if (errorsDirty) {
      if (storage.save(KEYS.errorLog, logger.errors).ok) writes++;
      errorsDirty = false;
    }
    persistence.lastSaveMs = (host.clock ?? Date.now)() - t0;
    persistence.saves++;
    return writes;
  }

  // Core system: always on, cannot be switched off from the UI.
  const persistence = {
    id: "persistence",
    name: "Save cycle",
    core: true,
    intervalTicks: SAVE_INTERVAL_TICKS,
    scalable: false,
    saves: 0,
    lastSaveMs: 0,
    update: () => saveNow(),
    describe: () => ({ saves: persistence.saves, lastSaveMs: persistence.lastSaveMs, dirtySectors: regionStore.dirtyCount() }),
  };

  scheduler.register(persistence, true);
  scheduler.register(tracker, config.isSystemEnabled("regions"));
  scheduler.register(weather, config.isSystemEnabled("weather"));
  scheduler.register(climate, config.isSystemEnabled("climate"));
  scheduler.register(fog, config.isSystemEnabled("fog"));
  scheduler.register(water, config.isSystemEnabled("water"));
  scheduler.register(hud, config.isSystemEnabled("hud"));
  if (!config.get("enabled")) scheduler.pause();

  config.onChange((key, value) => {
    if (key === "profile") {
      perf.setProfile(value);
      regionStore.setCapacity(perf.profile.maxCachedRegions);
    } else if (key === "adaptive") perf.setAdaptive(value);
    else if (key === "logLevel") logger.setLevel(value);
    else if (key === "enabled") value ? scheduler.resume() : scheduler.pause();
    else if (key.startsWith("system.")) scheduler.setEnabled(key.slice(7), value);
    else if (key === "weather.syncVanilla" && value) weather.syncVanilla(scheduler.ticks, true);
    else if (key === "weather.rainFrequency") weather.forecastCache = null;
  });

  let tickAtLastSelfTest;
  const runtime = {
    logger,
    storage,
    config,
    perf,
    scheduler,
    regionStore,
    tracker,
    weather,
    climate,
    fog,
    water,
    loadInfo,
    version: { addon: ADDON_VERSION, game: TARGET_GAME_VERSION, server: SERVER_MODULE_VERSION, serverUi: SERVER_UI_MODULE_VERSION },
    tick: () => scheduler.tick(),
    saveNow,
    /** Systems the player may switch on/off (core systems excluded). */
    toggleableSystems: () => scheduler.list().filter((s) => s.id !== "persistence"),
    selfTest() {
      const results = runSelfTest({ storage, scheduler, tickAtLastCheck: tickAtLastSelfTest });
      tickAtLastSelfTest = scheduler.ticks;
      const failed = results.filter((r) => !r.ok);
      if (failed.length) logger.warn("selftest", `${failed.length} check(s) failed: ${failed.map((f) => f.name).join(", ")}`);
      else logger.info("selftest", "all checks passed");
      return results;
    },
    dashboard() {
      return {
        enabled: config.get("enabled"),
        paused: scheduler.paused,
        tick: scheduler.ticks,
        perf: perf.snapshot(),
        regions: tracker.describe(),
        queue: scheduler.queueSize(),
        queueRejected: scheduler.stats.queueRejected,
        deferredRuns: scheduler.stats.deferredRuns,
        systems: scheduler.list(),
        storage: {
          ownProperties: storage.countOwnProperties(),
          totalBytes: storage.totalBytes(),
          saves: storage.stats.saves,
          recoveries: storage.stats.recoveries,
          failures: storage.stats.failures,
        },
        errors: logger.errors.length,
        settingsSource: loadInfo.source,
        weather: weather.summary(),
        climate: climate.describe(),
        water: water.describe(),
      };
    },
  };

  logger.info(
    "core",
    `Realistic World ${ADDON_VERSION} started (settings: ${loadInfo.source}, profile ${perf.profileName}, sim tick ${startTick}, weather ${weather.restored ? "restored" : "new"})`,
  );
  return runtime;
}
