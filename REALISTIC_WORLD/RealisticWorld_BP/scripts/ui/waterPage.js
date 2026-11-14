// Water Physics section of the control center: bodies near the player with
// their measured volumes, levels, pressures and flows, and water settings.

import { ActionFormData } from "@minecraft/server-ui";
import { ModalBuilder } from "./modal.js";
import { showForm } from "./forms.js";
import { bodyLines } from "./waterText.js";

/**
 * @param {import("./controlCenter.js").ControlCenter} cc
 * @param {import("@minecraft/server").Player} player
 */
export async function waterPage(cc, player) {
  const rt = cc.rt;
  const water = rt.water;
  const pos = cc.prefs.position(player);
  const d = water.describe();
  const near = water.near(pos.dim, pos.x, pos.z, 64).slice(0, 6);
  const lines = [
    `Simulated: ${d.tanks} tank(s), ${d.pools} pool(s), ${d.liters.toLocaleString("en-US")} L in total`,
    rt.config.isSystemEnabled("water") ? "" : "§cWater physics is switched off (Systems).§r",
    "",
    near.length ? "§lNear you§r" : "Nothing simulated near you.",
    ...near.flatMap((b) => [...bodyLines(water.describeBody(b)), ""]),
    "§7Tanks are found when you break a block next to still water, or with the Water Gauge. Lakes and the sea are never drained.§r",
  ];
  const form = new ActionFormData().title("Water Physics").body(lines.filter((l, i, a) => l !== "" || a[i - 1] !== "").join("\n"));
  const actions = [];
  const add = (label, fn) => {
    form.button(label);
    actions.push(fn);
  };
  add("Refresh", () => waterPage(cc, player));
  if (cc.prefs.isOperator(player)) add("Water settings", () => settings(cc, player));
  add("Back", () => cc.mainMenu(player));
  const res = await showForm(player, form);
  if (!res || res.canceled || res.selection === undefined) return;
  return actions[res.selection]();
}

/** @param {import("./controlCenter.js").ControlCenter} cc */
async function settings(cc, player) {
  const rt = cc.rt;
  const m = new ModalBuilder("Water settings")
    .toggle("auto", "Detect tanks when a block next to still water is broken", rt.config.get("water.autoDetect"))
    .toggle("puddles", "Rain on soaked ground forms puddles, ponds and floods in low places", rt.config.get("water.rainPuddles"))
    .submit("Apply");
  const res = await showForm(player, m.form);
  if (!res || res.canceled || !res.formValues) return waterPage(cc, player);
  const v = m.read(res.formValues);
  if (!v) return cc._layoutError(player);
  rt.config.set("water.autoDetect", v.auto === true);
  rt.config.set("water.rainPuddles", v.puddles === true);
  rt.saveNow();
  player.sendMessage("§a[Realistic World] Water settings applied.");
  return waterPage(cc, player);
}
