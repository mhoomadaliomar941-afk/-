// REALISTIC WORLD CONTROL CENTER. Built only from the official server-ui forms
// (ActionFormData, ModalFormData, MessageFormData). Every button here does
// something; systems that are not built yet are listed as text, not buttons.
// Settings can be changed by operators; other players get read-only pages.

import { ActionFormData, FormCancelationReason, MessageFormData } from "@minecraft/server-ui";
import { system } from "@minecraft/server";
import { PROFILES } from "../core/performance.js";
import { Logger } from "../core/logger.js";
import { ModalBuilder } from "./modal.js";

const TITLE = "REALISTIC WORLD CONTROL CENTER";
const PROFILE_NAMES = /** @type {("LOW"|"MEDIUM"|"ULTRA")[]} */ (Object.keys(PROFILES));
const LOG_LEVELS = ["debug", "info", "warn", "error"];
/** Sections of later phases, shown as plain text until they really exist. */
const PLANNED = [
  "Weather (phase 2)",
  "Water Physics (phase 3)",
  "Vegetation & Seasons (phase 4)",
  "Ecosystem & Wildlife (phase 5)",
  "Fire, Disasters & Geology (phase 6)",
  "Survival & Atmosphere (phase 7)",
];

/**
 * @typedef {ReturnType<typeof import("../core/runtime.js").createRuntime>} Runtime
 * @typedef {{
 *   isOperator: (p: import("@minecraft/server").Player) => boolean,
 *   getHud: (p: import("@minecraft/server").Player) => boolean,
 *   setHud: (p: import("@minecraft/server").Player, on: boolean) => void,
 * }} PlayerPrefs
 */

/**
 * Show a form, retrying while the player is busy (e.g. chat screen still open
 * after typing a command). Returns undefined if it could not be shown.
 * @template {{ canceled: boolean, cancelationReason?: string }} R
 * @param {import("@minecraft/server").Player} player
 * @param {{ show: (p: import("@minecraft/server").Player) => Promise<R> }} form
 * @returns {Promise<R | undefined>}
 */
export async function showForm(player, form, attempts = 30) {
  for (let i = 0; i < attempts; i++) {
    const res = await form.show(player);
    if (!res.canceled || res.cancelationReason !== FormCancelationReason.UserBusy) return res;
    await system.waitTicks(10);
  }
  return undefined;
}

function yesNo(v) {
  return v ? "§aON§r" : "§cOFF§r";
}

export class ControlCenter {
  /** @param {Runtime} rt @param {PlayerPrefs} prefs */
  constructor(rt, prefs) {
    this.rt = rt;
    this.prefs = prefs;
  }

  /** Entry point. Never throws: errors are logged and reported to the player. */
  open(player) {
    this.mainMenu(player).catch((e) => {
      this.rt.logger.error("ui", "control center failed", e);
      try {
        player.sendMessage("§c[Realistic World] The control center hit an error; see Error Log.");
      } catch {
        // player may have left
      }
    });
  }

  async mainMenu(player) {
    const d = this.rt.dashboard();
    const op = this.prefs.isOperator(player);
    const form = new ActionFormData()
      .title(TITLE)
      .body(
        `Simulation: ${d.paused ? "§ePAUSED§r" : "§aRUNNING§r"}  |  Profile: §b${d.perf.profile}§r\n` +
          `Errors logged: ${d.errors > 0 ? `§c${d.errors}§r` : "0"}` +
          (op ? "" : "\n§7Read-only: only operators can change settings.§r"),
      );
    const actions = [];
    const add = (label, fn) => {
      form.button(label);
      actions.push(fn);
    };
    add("Dashboard", () => this.dashboard(player));
    add("Systems", () => this.systems(player));
    add("Performance", () => this.performance(player));
    add("General Settings", () => this.general(player));
    add(`Error Log (${d.errors})`, () => this.errorLog(player));
    add("Run Self-Test", () => this.selfTest(player));
    form.divider();
    form.label(`§7Coming later:§r\n${PLANNED.map((p) => `§7- ${p}§r`).join("\n")}`);

    const res = await showForm(player, form);
    if (!res || res.canceled || res.selection === undefined) return;
    await actions[res.selection]();
  }

  async dashboard(player) {
    const d = this.rt.dashboard();
    const p = d.perf;
    const r = d.regions;
    const lines = [
      `§lEngine§r`,
      `State: ${d.paused ? "§ePAUSED" : "§aRUNNING"}§r   Sim tick: ${d.tick}`,
      `Settings loaded from: ${d.settingsSource}`,
      ``,
      `§lPerformance (script time, not FPS)§r`,
      `Profile: ${p.profile}   Budget: ${p.budgetMs} ms/tick`,
      `Average: ${p.avgTickMs} ms   Peak: ${p.peakTickMs} ms`,
      `Ticks over budget: ${p.overBudgetTicks} / ${p.ticksMeasured}`,
      `Adaptive: ${yesNo(p.adaptive)}   Throttle: x${p.throttle}`,
      `Queue: ${d.queue} (rejected ${d.queueRejected})   Deferred runs: ${d.deferredRuns}`,
      ``,
      `§lSimulation regions§r`,
      `NEAR ${r.near}   MEDIUM ${r.medium}   FAR ${r.far}`,
      `Sectors in memory: ${r.cachedSectors} (unsaved ${r.dirtySectors})`,
      `Regions resumed: ${r.resumed} (last away ${r.lastResumeAwayTicks} ticks)`,
      ``,
      `§lSaved data§r`,
      `Add-on properties: ${d.storage.ownProperties}   World total: ${d.storage.totalBytes} bytes`,
      `Saves: ${d.storage.saves}   Recovered: ${d.storage.recoveries}   Failed: ${d.storage.failures}`,
      ``,
      `§lSystems§r`,
      ...d.systems.map((s) => `${s.name}: ${s.state}  avg ${s.avgMs} ms, every ${s.intervalTicks} t, runs ${s.runs}`),
    ];
    const form = new ActionFormData().title("Dashboard").body(lines.join("\n")).button("Refresh").button("Back");
    const res = await showForm(player, form);
    if (!res || res.canceled) return;
    if (res.selection === 0) return this.dashboard(player);
    return this.mainMenu(player);
  }

  async systems(player) {
    const list = this.rt.toggleableSystems();
    if (!this.prefs.isOperator(player)) {
      const body = list.map((s) => `${s.name}: ${s.state}${s.lastError ? `\n  §7last error: ${s.lastError}§r` : ""}`).join("\n");
      const res = await showForm(player, new ActionFormData().title("Systems").body(body).button("Back"));
      if (res && !res.canceled) return this.mainMenu(player);
      return;
    }
    const m = new ModalBuilder("Systems").label(
      "Switch each system on or off. A FAULTED system stopped itself after repeated errors; switching it on again restarts it.",
    );
    for (const s of list) m.toggle(s.id, `${s.name}${s.faulted ? " §c(FAULTED)§r" : ""}`, s.enabled && !s.faulted);
    m.submit("Apply");
    const res = await showForm(player, m.form);
    if (!res || res.canceled || !res.formValues) return;
    const values = m.read(res.formValues);
    if (!values) return this._layoutError(player);
    for (const s of list) {
      const want = values[s.id] === true;
      if (want !== (s.enabled && !s.faulted)) {
        if (want && s.faulted) this.rt.scheduler.setEnabled(s.id, true); // clears the fault
        this.rt.config.setSystemEnabled(s.id, want);
      }
    }
    this.rt.saveNow();
    player.sendMessage("§a[Realistic World] Systems updated.");
    return this.mainMenu(player);
  }

  async performance(player) {
    const op = this.prefs.isOperator(player);
    const p = this.rt.perf.snapshot();
    const m = new ModalBuilder("Performance").label(
      `Average script time: ${p.avgTickMs} ms of ${p.budgetMs} ms budget, throttle x${p.throttle}.\n` +
        `LOW: 2 ms, slower updates, small radius. MEDIUM: 4 ms. ULTRA: 6 ms, biggest radius - use only on strong phones.`,
    );
    if (op) {
      m.dropdown("profile", "Performance profile", PROFILE_NAMES, Math.max(0, PROFILE_NAMES.indexOf(/** @type {any} */ (p.profile))));
      m.toggle("adaptive", "Adaptive throttling (slow updates down when over budget)", p.adaptive);
    }
    m.toggle("hud", "Show status HUD for me", this.prefs.getHud(player)).submit("Apply");
    const res = await showForm(player, m.form);
    if (!res || res.canceled || !res.formValues) return;
    const v = m.read(res.formValues);
    if (!v) return this._layoutError(player);
    if (op) {
      this.rt.config.set("profile", PROFILE_NAMES[/** @type {number} */ (v.profile)]);
      this.rt.config.set("adaptive", v.adaptive === true);
      this.rt.perf.resetPeak();
      this.rt.saveNow();
    }
    this.prefs.setHud(player, v.hud === true);
    player.sendMessage("§a[Realistic World] Performance settings applied.");
    return this.mainMenu(player);
  }

  async general(player) {
    if (!this.prefs.isOperator(player)) {
      player.sendMessage("§e[Realistic World] Only operators can change general settings.");
      return this.mainMenu(player);
    }
    const form = new ActionFormData()
      .title("General Settings")
      .body(`Simulation: ${yesNo(this.rt.config.get("enabled"))}   Log level: ${this.rt.config.get("logLevel")}`)
      .button("Edit settings")
      .button("Save now")
      .button("Reset all settings")
      .button("Back");
    const res = await showForm(player, form);
    if (!res || res.canceled) return;
    if (res.selection === 0) return this.editGeneral(player);
    if (res.selection === 1) {
      const writes = this.rt.saveNow(Infinity);
      player.sendMessage(`§a[Realistic World] Saved (${writes} writes).`);
      return this.general(player);
    }
    if (res.selection === 2) return this.confirmReset(player);
    return this.mainMenu(player);
  }

  async editGeneral(player) {
    const m = new ModalBuilder("General Settings")
      .toggle("enabled", "Simulation enabled (off pauses every system)", this.rt.config.get("enabled"))
      .dropdown("logLevel", "Log level (content log)", LOG_LEVELS, Math.max(0, LOG_LEVELS.indexOf(this.rt.config.get("logLevel"))))
      .submit("Apply");
    const res = await showForm(player, m.form);
    if (!res || res.canceled || !res.formValues) return;
    const v = m.read(res.formValues);
    if (!v) return this._layoutError(player);
    this.rt.config.set("enabled", v.enabled === true);
    this.rt.config.set("logLevel", LOG_LEVELS[/** @type {number} */ (v.logLevel)]);
    this.rt.saveNow();
    player.sendMessage("§a[Realistic World] Settings applied.");
    return this.general(player);
  }

  async confirmReset(player) {
    const form = new MessageFormData()
      .title("Reset all settings?")
      .body("Every setting goes back to its default. Saved simulation data (regions, clock) is kept.")
      .button1("Reset")
      .button2("Cancel");
    const res = await showForm(player, form);
    if (!res || res.canceled) return;
    if (res.selection === 0) {
      this.rt.config.reset();
      this.rt.saveNow();
      player.sendMessage("§a[Realistic World] Settings reset to defaults.");
    }
    return this.general(player);
  }

  async errorLog(player) {
    const errors = this.rt.logger.errors;
    const body = errors.length
      ? errors.slice(-10).reverse().map((e) => `§c${Logger.format(e)}§r`).join("\n\n")
      : "No errors recorded.";
    const op = this.prefs.isOperator(player);
    const form = new ActionFormData().title(`Error Log (${errors.length})`).body(body);
    if (op && errors.length) form.button("Clear log");
    form.button("Back");
    const res = await showForm(player, form);
    if (!res || res.canceled) return;
    if (op && errors.length && res.selection === 0) {
      this.rt.logger.clearErrors();
      this.rt.saveNow();
      return this.errorLog(player);
    }
    return this.mainMenu(player);
  }

  _layoutError(player) {
    this.rt.logger.error("ui", "form answer had an unexpected number of values; nothing was changed");
    player.sendMessage("§c[Realistic World] Could not read the form answer; nothing was changed. See Error Log.");
  }

  async selfTest(player) {
    const results = this.rt.selfTest();
    const passed = results.filter((r) => r.ok).length;
    const body =
      `${passed}/${results.length} checks passed.\n` +
      "§7Run it twice: the engine-loop check compares with the previous run.§r\n\n" +
      results.map((r) => `${r.ok ? "§aPASS" : "§cFAIL"}§r ${r.name}\n  §7${r.detail}§r`).join("\n");
    const res = await showForm(player, new ActionFormData().title("Self-Test").body(body).button("Run again").button("Back"));
    if (!res || res.canceled) return;
    if (res.selection === 0) return this.selfTest(player);
    return this.mainMenu(player);
  }
}
