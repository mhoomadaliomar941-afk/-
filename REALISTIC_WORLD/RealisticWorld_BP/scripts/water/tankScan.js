// Finds the shape of a body of still water by flood-filling its source blocks,
// a few at a time (the scan can be spread over many ticks), and records every
// opening in its walls and floor: holes, open valves, pipes and drains.
// Bodies larger than the cap are treated as natural water (lakes, rivers, the
// sea) and are never drained.

/**
 * What the scanner needs to know about one block.
 * @typedef {"source" | "flow" | "air" | "solid" | "valve_open" | "valve_closed" | "pipe" | "drain" | "unloaded"} CellKind
 * @typedef {(x: number, y: number, z: number) => CellKind} CellFn
 *
 * @typedef {{ kind: "hole" | "floor" | "valve" | "drain", x: number, y: number, z: number, bottom: number, dx?: number, dz?: number }} Opening
 *   bottom: for side openings the y of the opening block; for floor openings
 *   the top of the floor (the water cell's y). dx/dz: outward direction of a
 *   side opening (the water leaves the wall at x+dx, z+dz).
 * @typedef {{ x: number, y: number, z: number, fromY: number }} PipeStart
 *
 * @typedef {{
 *   cells: number, waterTop: number, baseY: number, layers: number[],
 *   columns: [number, number, number, number][],   // [x, z, minY, maxY] incl. empty space inside the walls
 *   openTop: number,                                // cells with open air above (rain, evaporation)
 *   openings: Opening[], valves: { x: number, y: number, z: number }[], pipes: PipeStart[],
 * }} TankShape
 */

export const MAX_TANK_CELLS = 4096;
const SIDES = [[1, 0, 0], [-1, 0, 0], [0, 0, 1], [0, 0, -1]];
const k3 = (x, y, z) => `${x},${y},${z}`;

export class TankScan {
  /**
   * @param {CellFn} cellAt
   * @param {{ x: number, y: number, z: number }} seed a water source block
   * @param {number} [maxCells]
   */
  constructor(cellAt, seed, maxCells = MAX_TANK_CELLS) {
    this.cellAt = cellAt;
    this.maxCells = maxCells;
    /** @type {"running" | "done" | "too_big" | "unloaded" | "not_water"} */
    this.status = "running";
    this.stack = [seed];
    this.seen = new Set([k3(seed.x, seed.y, seed.z)]);
    /** @type {{ x: number, y: number, z: number }[]} */
    this.cells = [];
    /** @type {Map<string, Opening>} */
    this.openings = new Map();
    /** @type {Map<string, { x: number, y: number, z: number }>} */
    this.valves = new Map();
    /** @type {Map<string, PipeStart>} */
    this.pipes = new Map();
    this.openTop = 0;
    this.blockReads = 0;
    if (cellAt(seed.x, seed.y, seed.z) !== "source") this.status = "not_water";
  }

  _read(x, y, z) {
    this.blockReads++;
    return this.cellAt(x, y, z);
  }

  _visit(x, y, z) {
    const k = k3(x, y, z);
    if (this.seen.has(k)) return;
    this.seen.add(k);
    this.stack.push({ x, y, z });
  }

  _neighbour(x, y, z, kind, fromY, side, dx = 0, dz = 0) {
    const k = k3(x, y, z);
    switch (kind) {
      case "source":
        this._visit(x, y, z);
        break;
      case "air":
      case "flow":
        if (!this.openings.has(k)) {
          this.openings.set(k, side ? { kind: "hole", x, y, z, bottom: y, dx, dz } : { kind: "floor", x, y, z, bottom: fromY });
        }
        break;
      case "valve_open":
        this.valves.set(k, { x, y, z });
        if (!this.openings.has(k)) this.openings.set(k, side ? { kind: "valve", x, y, z, bottom: y, dx, dz } : { kind: "valve", x, y, z, bottom: fromY });
        break;
      case "valve_closed":
        this.valves.set(k, { x, y, z });
        break;
      case "drain":
        if (!this.openings.has(k)) this.openings.set(k, { kind: "drain", x, y, z, bottom: side ? y : fromY });
        break;
      case "pipe":
        if (!this.pipes.has(k)) this.pipes.set(k, { x, y, z, fromY });
        break;
      case "unloaded":
        this.status = "unloaded";
        break;
      default:
        break; // solid wall or floor
    }
  }

  /**
   * Process up to `maxCells` water cells.
   * @returns {boolean} true when finished (check `status`)
   */
  step(maxCells = 64) {
    let n = 0;
    while (this.status === "running" && this.stack.length > 0 && n < maxCells) {
      const c = /** @type {{ x: number, y: number, z: number }} */ (this.stack.pop());
      n++;
      this.cells.push(c);
      if (this.cells.length > this.maxCells) {
        this.status = "too_big";
        break;
      }
      for (const [dx, , dz] of SIDES) {
        this._neighbour(c.x + dx, c.y, c.z + dz, this._read(c.x + dx, c.y, c.z + dz), c.y, true, dx, dz);
      }
      this._neighbour(c.x, c.y - 1, c.z, this._read(c.x, c.y - 1, c.z), c.y, false);
      const up = this._read(c.x, c.y + 1, c.z);
      if (up === "source") this._visit(c.x, c.y + 1, c.z);
      else if (up === "air") this.openTop++;
      else if (up === "unloaded") this.status = "unloaded";
    }
    if (this.status === "running" && this.stack.length === 0) this.status = "done";
    return this.status !== "running";
  }

  /**
   * Empty space inside the walls above the water: layer by layer, the air
   * cells over the water's columns count as tank space while every side of
   * the layer is closed by blocks. The first layer open to the outside is the
   * rim: water above it would overflow, so capacity stops below it.
   * @returns {{ x: number, y: number, z: number }[]}
   */
  headroom(maxLayers = 8, maxColumns = 1024) {
    /** @type {Map<string, number>} column -> top water y */
    const tops = new Map();
    for (const c of this.cells) {
      const k = `${c.x},${c.z}`;
      tops.set(k, Math.max(tops.get(k) ?? -Infinity, c.y));
    }
    if (tops.size > maxColumns) return [];
    const surfaceY = Math.max(...tops.values());
    // Only columns whose water reaches the surface layer continue upward.
    let layer = [...tops].filter(([, y]) => y === surfaceY).map(([k]) => k.split(",").map(Number));
    const extra = [];
    for (let y = surfaceY + 1; y <= surfaceY + maxLayers && layer.length; y++) {
      const set = new Set(layer.map(([x, z]) => `${x},${z}`));
      let closed = true;
      for (const [x, z] of layer) {
        if (this._read(x, y, z) !== "air") {
          closed = false;
          break;
        }
        for (const [dx, , dz] of SIDES) {
          if (set.has(`${x + dx},${z + dz}`)) continue;
          if (this._read(x + dx, y, z + dz) !== "solid") {
            closed = false;
            break;
          }
        }
        if (!closed) break;
      }
      if (!closed) break;
      for (const [x, z] of layer) extra.push({ x, y, z });
    }
    return extra;
  }

  /** @returns {TankShape} */
  result() {
    const space = [...this.cells, ...this.headroom()];
    let waterTop = -Infinity;
    for (const c of this.cells) waterTop = Math.max(waterTop, c.y);
    let minY = Infinity;
    let maxY = -Infinity;
    /** @type {Map<string, [number, number, number, number]>} */
    const cols = new Map();
    for (const c of space) {
      minY = Math.min(minY, c.y);
      maxY = Math.max(maxY, c.y);
      const k = `${c.x},${c.z}`;
      const col = cols.get(k);
      if (!col) cols.set(k, [c.x, c.z, c.y, c.y]);
      else {
        col[2] = Math.min(col[2], c.y);
        col[3] = Math.max(col[3], c.y);
      }
    }
    const layers = new Array(Math.max(0, maxY - minY + 1)).fill(0);
    for (const c of space) layers[c.y - minY]++;
    return {
      cells: this.cells.length,
      waterTop,
      baseY: minY,
      layers,
      columns: [...cols.values()],
      openTop: this.openTop,
      openings: [...this.openings.values()],
      valves: [...this.valves.values()],
      pipes: [...this.pipes.values()],
    };
  }
}

/**
 * Follow a chain of pipe blocks from `start` to its far end.
 * @param {CellFn} cellAt
 * @param {{ x: number, y: number, z: number }} start first pipe block (next to the tank)
 * @param {(x: number, y: number, z: number) => boolean} isOwnWater water cell of the starting tank
 * @returns {{ end: { x: number, y: number, z: number }, out: { kind: "water" | "air" | "drain", x: number, y: number, z: number } | null, length: number } | null}
 */
export function tracePipe(cellAt, start, isOwnWater, maxLength = 64) {
  const DIRS = [[1, 0, 0], [-1, 0, 0], [0, 0, 1], [0, 0, -1], [0, -1, 0], [0, 1, 0]];
  const seen = new Set([k3(start.x, start.y, start.z)]);
  let cur = start;
  for (let length = 1; length <= maxLength; length++) {
    let next = null;
    /** @type {{ kind: "water" | "air" | "drain", x: number, y: number, z: number } | null} */
    let out = null;
    for (const [dx, dy, dz] of DIRS) {
      const x = cur.x + dx;
      const y = cur.y + dy;
      const z = cur.z + dz;
      if (seen.has(k3(x, y, z))) continue;
      const kind = cellAt(x, y, z);
      if (kind === "pipe") {
        if (!next) next = { x, y, z };
      } else if (kind === "source" && !isOwnWater(x, y, z)) {
        out ??= { kind: "water", x, y, z };
      } else if (kind === "drain") {
        out ??= { kind: "drain", x, y, z };
      } else if ((kind === "air" || kind === "flow") && !out) {
        out = { kind: "air", x, y, z };
      }
    }
    if (next) {
      seen.add(k3(next.x, next.y, next.z));
      cur = next;
      continue;
    }
    return { end: cur, out, length };
  }
  return null; // too long or a loop: ignored
}
