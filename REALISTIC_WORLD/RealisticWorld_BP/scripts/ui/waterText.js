// Text for water readings (gauge messages and the Water page). Pure.

/** @param {ReturnType<import("../water/waterSystem.js").WaterSystem["describeBody"]>} i */
export function bodyLines(i) {
  if (i.type === "tank") {
    const pct = i.capacityLiters ? Math.round((i.liters / i.capacityLiters) * 100) : 0;
    const lines = [
      `§bTank ${i.id}§r: ${i.liters.toLocaleString("en-US")} L of ${i.capacityLiters.toLocaleString("en-US")} L (${pct}%)`,
      `Depth ${i.depth} m, pressure at the bottom ${i.bottomPressureKPa} kPa`,
    ];
    if (i.outflowLps > 0) lines.push(`§cLeaking ${i.outflowLps} L/s§r through ${i.openings} opening(s); ${i.leakedLiters.toLocaleString("en-US")} L lost so far`);
    else if (i.openings > 0) lines.push(`Openings: ${i.openings} (water is at or below them)`);
    else lines.push("Sealed: no openings");
    if (i.closedValves > 0) lines.push(`Closed valves: ${i.closedValves}`);
    if (i.pipes > 0) lines.push(`Pipes: ${i.pipes}`);
    if (i.frozen) lines.push("§bSurface frozen§r");
    return lines;
  }
  const lines = [
    `§9${i.flood ? "Flood" : "Pool"} ${i.id}§r: ${i.liters.toLocaleString("en-US")} L over ${i.areaM2} m², deepest ${i.maxDepth} m`,
    `Soaked into the ground: ${i.absorbedLiters.toLocaleString("en-US")} L, evaporated: ${i.evaporatedLiters.toLocaleString("en-US")} L`,
  ];
  if (i.frozen) lines.push("§bFrozen§r");
  return lines;
}
