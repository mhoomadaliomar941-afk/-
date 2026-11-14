#!/usr/bin/env node
// Packages the add-on into dist/:
//   REALISTIC_WORLD.mcaddon   both packs; open it on Android to import
//   RealisticWorld_BP.mcpack  behavior pack only
//   RealisticWorld_RP.mcpack  resource pack only
// Uses the system `zip` tool. Run `npm run check` first.

import { execFileSync } from "node:child_process";
import { mkdirSync, rmSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const DIST = join(ROOT, "dist");
rmSync(DIST, { recursive: true, force: true });
mkdirSync(DIST);

const zip = (out, cwd, ...paths) => execFileSync("zip", ["-q", "-r", "-X", out, ...paths], { cwd, stdio: "inherit" });

// .mcpack: manifest.json at the archive root
zip(join(DIST, "RealisticWorld_BP.mcpack"), join(ROOT, "RealisticWorld_BP"), ".");
zip(join(DIST, "RealisticWorld_RP.mcpack"), join(ROOT, "RealisticWorld_RP"), ".");
// .mcaddon: one folder per pack
zip(join(DIST, "REALISTIC_WORLD.mcaddon"), ROOT, "RealisticWorld_BP", "RealisticWorld_RP");

for (const f of ["REALISTIC_WORLD.mcaddon", "RealisticWorld_BP.mcpack", "RealisticWorld_RP.mcpack"]) {
  if (!existsSync(join(DIST, f))) throw new Error(`${f} was not created`);
}
console.log("built dist/REALISTIC_WORLD.mcaddon, dist/RealisticWorld_BP.mcpack, dist/RealisticWorld_RP.mcpack");
