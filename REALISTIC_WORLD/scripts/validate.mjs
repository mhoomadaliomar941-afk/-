#!/usr/bin/env node
// Static checks on both packs before building:
//  - every .json file parses
//  - manifests: format 2, unique UUIDs, BP depends on the RP's UUID,
//    script entry exists, module versions match package.json (the versions
//    the code was type-checked against)
//  - every relative import in scripts resolves to a real file with ".js"
//  - only @minecraft modules listed in the manifest are imported
//  - item icons exist in item_texture.json and as PNG files
//  - item names have a language entry
// Exits with code 1 on any problem.

import { readFileSync, readdirSync, statSync, existsSync } from "node:fs";
import { join, dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const BP = join(ROOT, "RealisticWorld_BP");
const RP = join(ROOT, "RealisticWorld_RP");
const problems = [];
const ok = [];

function walk(dir) {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name);
    return statSync(p).isDirectory() ? walk(p) : [p];
  });
}

/** Bedrock JSON may contain // comments; strip them outside strings. */
function parseJsonFile(file) {
  const text = readFileSync(file, "utf8");
  let out = "";
  let inStr = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inStr) {
      out += c;
      if (c === "\\") out += text[++i];
      else if (c === '"') inStr = false;
    } else if (c === '"') {
      inStr = true;
      out += c;
    } else if (c === "/" && text[i + 1] === "/") {
      while (i < text.length && text[i] !== "\n") i++;
      out += "\n";
    } else out += c;
  }
  return JSON.parse(out);
}

const json = {};
for (const file of [...walk(BP), ...walk(RP)].filter((f) => f.endsWith(".json"))) {
  try {
    json[relative(ROOT, file)] = parseJsonFile(file);
  } catch (e) {
    problems.push(`${relative(ROOT, file)}: invalid JSON (${e.message})`);
  }
}
ok.push(`${Object.keys(json).length} JSON files parsed`);

// ---- manifests ----
const bpm = json["RealisticWorld_BP/manifest.json"];
const rpm = json["RealisticWorld_RP/manifest.json"];
const uuidRe = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
if (!bpm || !rpm) problems.push("a manifest.json is missing");
else {
  const uuids = [bpm.header.uuid, ...bpm.modules.map((m) => m.uuid), rpm.header.uuid, ...rpm.modules.map((m) => m.uuid)];
  if (new Set(uuids).size !== uuids.length) problems.push("manifest UUIDs are not unique");
  for (const u of uuids) if (!uuidRe.test(u)) problems.push(`not a valid v4 UUID: ${u}`);
  for (const m of [bpm, rpm]) if (m.format_version !== 2) problems.push(`${m.header.name}: format_version must be 2`);
  if (!bpm.dependencies.some((d) => d.uuid === rpm.header.uuid)) problems.push("BP does not depend on the RP UUID");
  const script = bpm.modules.find((m) => m.type === "script");
  if (!script) problems.push("BP has no script module");
  else if (!existsSync(join(BP, script.entry))) problems.push(`script entry ${script.entry} not found`);

  const pkg = JSON.parse(readFileSync(join(ROOT, "package.json"), "utf8"));
  for (const mod of ["@minecraft/server", "@minecraft/server-ui"]) {
    const dep = bpm.dependencies.find((d) => d.module_name === mod);
    if (!dep) problems.push(`manifest is missing dependency ${mod}`);
    else if (dep.version !== pkg.devDependencies[mod]) {
      problems.push(`${mod}: manifest ${dep.version} != type-checked ${pkg.devDependencies[mod]}`);
    }
  }
  ok.push(`manifests OK (server ${bpm.dependencies.find((d) => d.module_name === "@minecraft/server")?.version}, min engine ${bpm.header.min_engine_version.join(".")})`);
}

// ---- script imports ----
const allowedModules = new Set((bpm?.dependencies ?? []).filter((d) => d.module_name).map((d) => d.module_name));
const scripts = walk(join(BP, "scripts")).filter((f) => f.endsWith(".js"));
const importRe = /(?:import|export)\s[^"'`]*?from\s*["']([^"']+)["']|import\(\s*["']([^"']+)["']\s*\)/g;
for (const file of scripts) {
  const src = readFileSync(file, "utf8");
  for (const m of src.matchAll(importRe)) {
    const spec = m[1] ?? m[2];
    if (spec.startsWith(".")) {
      if (!spec.endsWith(".js")) problems.push(`${relative(ROOT, file)}: import "${spec}" must end with .js`);
      else if (!existsSync(resolve(dirname(file), spec))) problems.push(`${relative(ROOT, file)}: import "${spec}" not found`);
    } else if (!allowedModules.has(spec)) {
      problems.push(`${relative(ROOT, file)}: imports "${spec}", which is not a manifest dependency`);
    }
  }
}
ok.push(`${scripts.length} script files, imports resolved`);

// ---- items, textures, language ----
const itemTextures = json["RealisticWorld_RP/textures/item_texture.json"]?.texture_data ?? {};
const lang = existsSync(join(RP, "texts/en_US.lang")) ? readFileSync(join(RP, "texts/en_US.lang"), "utf8") : "";
for (const [path, data] of Object.entries(json)) {
  if (!path.startsWith("RealisticWorld_BP/items/")) continue;
  const item = data["minecraft:item"];
  const id = item.description.identifier;
  const icon = item.components["minecraft:icon"];
  const iconKey = typeof icon === "string" ? icon : icon?.textures?.default;
  if (!itemTextures[iconKey]) problems.push(`${id}: icon "${iconKey}" not in item_texture.json`);
  else if (!existsSync(join(RP, `${itemTextures[iconKey].textures}.png`))) problems.push(`${id}: texture PNG missing`);
  const nameKey = item.components["minecraft:display_name"]?.value ?? `item.${id}.name`;
  if (!lang.split("\n").some((l) => l.startsWith(`${nameKey}=`))) problems.push(`${id}: no "${nameKey}" in en_US.lang`);
}
for (const p of ["RealisticWorld_BP/pack_icon.png", "RealisticWorld_RP/pack_icon.png"]) {
  if (!existsSync(join(ROOT, p))) problems.push(`${p} missing`);
}
ok.push("items, textures and language entries checked");

for (const line of ok) console.log(`ok   ${line}`);
for (const line of problems) console.log(`FAIL ${line}`);
if (problems.length) {
  console.log(`\n${problems.length} problem(s) found`);
  process.exit(1);
}
console.log("\nvalidation passed");
