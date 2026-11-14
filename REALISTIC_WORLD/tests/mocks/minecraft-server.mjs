// Test double for @minecraft/server 2.10.0. Implements only what the add-on
// uses, with the same names and shapes as the official type definitions, plus a
// `__mc` handle for tests to drive ticks, players and events.
// It enforces the real dynamic-property string limit (32767 bytes) so the
// chunked storage is tested against it.

export const CommandPermissionLevel = Object.freeze({ Any: 0, GameDirectors: 1, Admin: 2, Host: 3, Owner: 4 });
export const CustomCommandStatus = Object.freeze({ Success: 0, Failure: 1 });
export const PlayerPermissionLevel = Object.freeze({ Visitor: 0, Member: 1, Operator: 2 });
export const WeatherType = Object.freeze({ Clear: "Clear", Rain: "Rain", Thunder: "Thunder" });

class Signal {
  constructor() {
    this.handlers = [];
  }
  subscribe(fn) {
    this.handlers.push(fn);
    return fn;
  }
  unsubscribe(fn) {
    this.handlers = this.handlers.filter((h) => h !== fn);
  }
  fire(ev) {
    for (const h of this.handlers) h(ev);
  }
}

const MAX_STRING_BYTES = 32767;

class DynamicProps {
  constructor() {
    this.map = new Map();
  }
  get(id) {
    return this.map.get(id);
  }
  set(id, value) {
    if (value === undefined || value === null) {
      this.map.delete(id);
      return;
    }
    if (typeof value === "string" && Buffer.byteLength(value, "utf8") > MAX_STRING_BYTES) {
      throw new Error(`ArgumentOutOfBoundsError: dynamic property string too long (${id})`);
    }
    this.map.set(id, value);
  }
}

/** Records the player's fog stack the way FogSettings push/remove work with tags. */
class FogSettings {
  constructor() {
    this.stack = []; // { id, tag }
  }
  push(fogId, tag) {
    if (!__mc.knownFogs.has(fogId)) throw new Error(`FogSettingsError: unknown fog ${fogId}`);
    this.stack.push({ id: fogId, tag });
    return this.stack.length;
  }
  remove(tag) {
    const before = this.stack.length;
    this.stack = this.stack.filter((f) => f.tag !== tag);
    return this.stack.length !== before;
  }
  getStack() {
    return this.stack.map((f) => f.id);
  }
}

class ScreenDisplay {
  constructor() {
    this.actionBar = [];
  }
  setActionBar(text) {
    this.actionBar.push(text);
  }
}

export class Player {
  constructor(name, { x = 8, y = 64, z = 8, dim = "minecraft:overworld", operator = true } = {}) {
    this.name = name;
    this.id = `player:${name}`;
    this.typeId = "minecraft:player";
    this.isValid = true;
    this.location = { x, y, z };
    this.dimension = world.getDimension(dim);
    this.playerPermissionLevel = operator ? PlayerPermissionLevel.Operator : PlayerPermissionLevel.Member;
    this.onScreenDisplay = new ScreenDisplay();
    this.fogSettings = new FogSettings();
    this.messages = [];
    this.props = new DynamicProps();
  }
  sendMessage(m) {
    this.messages.push(typeof m === "string" ? m : JSON.stringify(m));
  }
  getDynamicProperty(id) {
    return this.props.get(id);
  }
  setDynamicProperty(id, v) {
    this.props.set(id, v);
  }
  getHeadLocation() {
    return { x: this.location.x, y: this.location.y + 1.62, z: this.location.z };
  }
}

export class LocationInUnloadedChunkError extends Error {}
export class LocationOutOfWorldBoundariesError extends Error {}

/** Arbitrary set of block positions (as in the real API). */
export class ListBlockVolume {
  constructor(locations) {
    this.locations = locations.map((l) => ({ x: l.x, y: l.y, z: l.z }));
  }
}

const DEFAULT_STATES = { "minecraft:water": { liquid_depth: 0 }, "rw:valve": { "rw:open": false } };

class MockPermutation {
  constructor(typeId, states) {
    this.type = { id: typeId };
    this.states = { ...(DEFAULT_STATES[typeId] ?? {}), ...(states ?? {}) };
  }
  getAllStates() {
    return { ...this.states };
  }
  withState(name, value) {
    if (!(name in this.states)) throw new Error(`block ${this.type.id} has no state ${name}`);
    return new MockPermutation(this.type.id, { ...this.states, [name]: value });
  }
}

class MockBlock {
  constructor(dim, x, y, z) {
    this.dimension = dim;
    this.x = x;
    this.y = y;
    this.z = z;
    this.location = { x, y, z };
    this.isValid = true;
  }
  get _cell() {
    return this.dimension._get(this.x, this.y, this.z);
  }
  get typeId() {
    return this._cell.typeId;
  }
  get permutation() {
    return new MockPermutation(this._cell.typeId, this._cell.states);
  }
  get isAir() {
    return this.typeId === "minecraft:air";
  }
  get isLiquid() {
    return this.typeId === "minecraft:water" || this.typeId === "minecraft:flowing_water";
  }
  setPermutation(perm) {
    this.dimension._set(this.x, this.y, this.z, perm.type.id, perm.states);
  }
  setType(typeId) {
    this.dimension._set(this.x, this.y, this.z, typeId);
  }
  below(n = 1) {
    return this.dimension.getBlock({ x: this.x, y: this.y - n, z: this.z });
  }
}

/** Dimension with a voxel world: grass/dirt up to __mc.surfaceFn(x, z), air above, plus edits. */
class Dimension {
  constructor(id) {
    this.id = id;
    this.weatherCalls = [];
    this.cells = new Map();
  }
  _get(x, y, z) {
    const c = this.cells.get(`${x},${y},${z}`);
    if (c) return c;
    const s = __mc.surfaceFn(x, z);
    return { typeId: y > s ? "minecraft:air" : y === s ? "minecraft:grass_block" : "minecraft:dirt", states: {} };
  }
  _set(x, y, z, typeId, states) {
    const from = this._get(x, y, z).typeId;
    this.cells.set(`${x},${y},${z}`, { typeId, states: new MockPermutation(typeId, states).states });
    __mc.blockChanges.push({ dim: this.id, x, y, z, from, to: typeId });
  }
  isChunkLoaded(loc) {
    return __mc.isLoaded(this.id, loc.x, loc.z);
  }
  getBlock(loc) {
    const x = Math.floor(loc.x);
    const y = Math.floor(loc.y);
    const z = Math.floor(loc.z);
    if (y < -64 || y > 319) throw new LocationOutOfWorldBoundariesError("outside the world");
    if (!__mc.isLoaded(this.id, x, z)) return undefined;
    return new MockBlock(this, x, y, z);
  }
  setBlockType(loc, typeId) {
    this._set(loc.x, loc.y, loc.z, typeId);
  }
  fillBlocks(volume, block, options = {}) {
    const include = options.blockFilter?.includeTypes;
    const changed = [];
    for (const l of volume.locations) {
      if (!__mc.isLoaded(this.id, l.x, l.z)) {
        if (options.ignoreChunkBoundErrors) continue;
        throw new LocationInUnloadedChunkError("unloaded");
      }
      if (include && !include.includes(this._get(l.x, l.y, l.z).typeId)) continue;
      this._set(l.x, l.y, l.z, typeof block === "string" ? block : block.type.id);
      changed.push(l);
    }
    __mc.fillCalls.push({ type: block, from: include ? [...include] : null, count: changed.length, cells: changed, tick: currentTick });
    return new ListBlockVolume(changed);
  }
  setWeather(type, duration) {
    if (!(type in WeatherType)) throw new Error(`invalid weather ${type}`);
    this.weatherCalls.push({ type, duration, tick: currentTick });
    const previous = __mc.weather;
    __mc.weather = type;
    if (previous !== type) world.afterEvents.weatherChange.fire({ dimension: this.id, newWeather: type, previousWeather: previous });
  }
  getBiome(loc) {
    if (!__mc.isLoaded(this.id, loc.x, loc.z)) throw new LocationInUnloadedChunkError("unloaded");
    return { id: __mc.biomeFn(this.id, loc.x, loc.z) };
  }
  getTopmostBlock(xz) {
    if (!__mc.isLoaded(this.id, xz.x, xz.z)) throw new LocationInUnloadedChunkError("unloaded");
    for (let y = 160; y >= -64; y--) {
      if (this._get(xz.x, y, xz.z).typeId !== "minecraft:air") return new MockBlock(this, xz.x, y, xz.z);
    }
    return undefined;
  }
  getSkyLightLevel(loc) {
    return __mc.skyLightFn(loc);
  }
}

const worldProps = new DynamicProps();
const dimensions = new Map();
const players = [];

export const world = {
  afterEvents: {
    worldLoad: new Signal(),
    itemUse: new Signal(),
    playerLeave: new Signal(),
    weatherChange: new Signal(),
    playerBreakBlock: new Signal(),
    blockExplode: new Signal(),
    playerPlaceBlock: new Signal(),
    playerInteractWithBlock: new Signal(),
  },
  seed: "123456789",
  getTimeOfDay: () => __mc.timeOfDay,
  getDynamicProperty: (id) => worldProps.get(id),
  setDynamicProperty: (id, v) => worldProps.set(id, v),
  getDynamicPropertyIds: () => [...worldProps.map.keys()],
  getDynamicPropertyTotalByteCount: () => {
    let n = 0;
    for (const [k, v] of worldProps.map) n += k.length + String(v).length;
    return n;
  },
  getAllPlayers: () => players.filter((p) => p.isValid),
  getDimension(id) {
    if (!["minecraft:overworld", "minecraft:nether", "minecraft:the_end"].includes(id)) throw new Error(`no dimension ${id}`);
    if (!dimensions.has(id)) dimensions.set(id, new Dimension(id));
    return dimensions.get(id);
  },
  broadcast: [],
  sendMessage(m) {
    this.broadcast.push(m);
  },
};

const timers = []; // { at, fn, every }
let currentTick = 0;
let waiters = []; // { at, resolve }

export const system = {
  beforeEvents: { startup: new Signal(), shutdown: new Signal() },
  afterEvents: { scriptEventReceive: new Signal() },
  serverSystemInfo: { memoryTier: 2 },
  get currentTick() {
    return currentTick;
  },
  run(fn) {
    timers.push({ at: currentTick + 1, fn, every: 0 });
    return timers.length;
  },
  runInterval(fn, every = 1) {
    timers.push({ at: currentTick + every, fn, every });
    return timers.length;
  },
  runTimeout(fn, delay = 1) {
    timers.push({ at: currentTick + delay, fn, every: 0 });
    return timers.length;
  },
  waitTicks(n) {
    return new Promise((resolve) => waiters.push({ at: currentTick + n, resolve }));
  },
};

/** Test control handle. */
export const __mc = {
  worldProps,
  players,
  commands: new Map(),
  loadedFn: () => true,
  weather: "Clear",
  timeOfDay: 6000,
  biomeFn: () => "minecraft:plains",
  surfaceFn: () => 70,
  skyLightFn: () => 15,
  knownFogs: new Set(),
  blockChanges: [],
  fillCalls: [],
  overworld() {
    return world.getDimension("minecraft:overworld");
  },
  setBlock(x, y, z, typeId, states) {
    this.overworld()._set(x, y, z, typeId, states);
  },
  typeAt(x, y, z) {
    return this.overworld()._get(x, y, z).typeId;
  },
  /** A player breaks a block: it becomes air, then the after-event fires. */
  breakBlock(player, x, y, z) {
    const dim = this.overworld();
    const before = new MockPermutation(dim._get(x, y, z).typeId, dim._get(x, y, z).states);
    dim._set(x, y, z, "minecraft:air");
    world.afterEvents.playerBreakBlock.fire({ player, dimension: dim, block: new MockBlock(dim, x, y, z), brokenBlockPermutation: before });
  },
  placeBlock(player, x, y, z, typeId) {
    const dim = this.overworld();
    dim._set(x, y, z, typeId);
    world.afterEvents.playerPlaceBlock.fire({ player, dimension: dim, block: new MockBlock(dim, x, y, z) });
  },
  interact(player, x, y, z, itemTypeId) {
    const dim = this.overworld();
    world.afterEvents.playerInteractWithBlock.fire({
      player, block: new MockBlock(dim, x, y, z), itemStack: itemTypeId ? { typeId: itemTypeId } : undefined,
      isFirstEvent: true, blockFace: "Up", faceLocation: { x: 0.5, y: 1, z: 0.5 },
    });
  },
  /** A player typing /weather <type>: the game changes weather on its own. */
  typeWeatherCommand(type) {
    world.getDimension("minecraft:overworld");
    const previous = this.weather;
    this.weather = type;
    world.afterEvents.weatherChange.fire({ dimension: "minecraft:overworld", newWeather: type, previousWeather: previous });
  },
  isLoaded(dim, x, z) {
    return this.loadedFn(dim, x, z);
  },
  addPlayer(name, opts) {
    const p = new Player(name, opts);
    players.push(p);
    return p;
  },
  /** Fire startup with a registry that records commands. */
  startup() {
    const registry = {
      registerCommand: (def, cb) => {
        if (!/^[a-z0-9_]+:[a-z0-9_]+$/.test(def.name)) throw new Error(`invalid command name ${def.name}`);
        this.commands.set(def.name, { def, cb });
      },
      registerEnum() {},
    };
    system.beforeEvents.startup.fire({ customCommandRegistry: registry });
  },
  /** Run a registered custom command as a player. */
  runCommand(name, player, ...args) {
    const c = this.commands.get(name);
    if (!c) throw new Error(`unknown command ${name}`);
    return c.cb({ sourceEntity: player, sourceType: "Entity" }, ...args);
  },
  /** Advance game ticks, running timers and letting promises settle. */
  async tick(n = 1) {
    for (let i = 0; i < n; i++) {
      currentTick++;
      const due = timers.filter((t) => t.at <= currentTick);
      for (const t of due) {
        t.fn();
        if (t.every > 0) t.at = currentTick + t.every;
        else timers.splice(timers.indexOf(t), 1);
      }
      const ready = waiters.filter((w) => w.at <= currentTick);
      waiters = waiters.filter((w) => w.at > currentTick);
      for (const w of ready) w.resolve();
      await new Promise((r) => setImmediate(r));
    }
  },
  /** Simulate closing the world: final shutdown event, then drop timers,
   * handlers and commands. Saved dynamic properties are kept. */
  closeWorld() {
    system.beforeEvents.shutdown.fire({});
    timers.length = 0;
    waiters = [];
    const signals = [
      ...Object.values(world.afterEvents),
      ...Object.values(system.beforeEvents),
      ...Object.values(system.afterEvents),
    ];
    for (const sig of signals) sig.handlers = [];
    this.commands.clear();
  },
};
