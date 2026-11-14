// End-to-end test of the real main.js against the game doubles in tests/mocks.
// Run with: node --import ./tests/mocks/register.mjs --test tests/
import test from "node:test";
import assert from "node:assert/strict";
import { __mc, world, system } from "@minecraft/server";
import { __ui, pick, submit, close, button1 } from "@minecraft/server-ui";
import { readdirSync, readFileSync } from "node:fs";

// The game knows only the fogs defined in the resource pack.
const FOG_DIR = new URL("../RealisticWorld_RP/fogs/", import.meta.url);
for (const f of readdirSync(FOG_DIR)) {
  __mc.knownFogs.add(JSON.parse(readFileSync(new URL(f, FOG_DIR), "utf8"))["minecraft:fog_settings"].description.identifier);
}

const MAIN = new URL("../RealisticWorld_BP/scripts/main.js", import.meta.url).href;
let loadCount = 0;

/** Load main.js as the game does: import, startup event, world load. */
async function enterWorld() {
  await import(`${MAIN}?load=${++loadCount}`);
  __mc.startup();
  world.afterEvents.worldLoad.fire({});
}

/** Wait until pending UI promises settle. */
async function settle(ticks = 3) {
  await __mc.tick(ticks);
}

const silence = () => {
  const orig = { info: console.info, warn: console.warn, error: console.error };
  console.info = () => {};
  console.warn = () => {};
  return () => Object.assign(console, orig);
};

test("full session: commands, item, control center, saving, reload", async (t) => {
  const restore = silence();
  t.after(restore);

  const steve = __mc.addPlayer("Steve", { operator: true });
  const alex = __mc.addPlayer("Alex", { operator: false, x: 16 * 20, z: 0 });

  // --- before the world loads, commands answer "not ready" ---
  await import(`${MAIN}?load=${++loadCount}`);
  __mc.startup();
  assert.deepEqual([...__mc.commands.keys()].sort(), ["rw:menu", "rw:selftest", "rw:status"]);
  assert.equal(__mc.runCommand("rw:menu", steve).status, 1, "fails cleanly before world load");
  assert.equal(__mc.commands.get("rw:menu").def.cheatsRequired, false);
  world.afterEvents.worldLoad.fire({});

  await t.test("engine ticks and tracks regions for both players", async () => {
    await __mc.tick(45);
    const status = __mc.runCommand("rw:status", steve);
    assert.equal(status.status, 0);
    assert.match(status.message, /RUNNING/);
    // MEDIUM profile (memoryTier 2): two separate 13x13 areas
    assert.match(status.message, /NEAR 50 MEDIUM 112 FAR 176/);
  });

  await t.test("/rw:menu opens the control center (after a busy chat screen)", async () => {
    __ui.busyTimes = 2; // chat still open for two attempts
    __ui.answer(close());
    const res = __mc.runCommand("rw:menu", steve);
    assert.equal(res.status, 0);
    await settle(30);
    const form = __ui.last();
    assert.equal(form.title, "REALISTIC WORLD CONTROL CENTER");
    assert.deepEqual(
      form.elements.filter((e) => e.type === "button").map((e) => e.text),
      ["Dashboard", "Weather", "Systems", "Performance", "General Settings", "Error Log (0)", "Run Self-Test"],
    );
    assert.ok(form.elements.some((e) => e.type === "label" && e.text.includes("Water Physics (phase 3)")), "planned sections are text, not buttons");
  });

  await t.test("dashboard shows live engine data", async () => {
    __ui.answer(pick("Dashboard"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(5);
    const dash = __ui.last();
    assert.equal(dash.title, "Dashboard");
    assert.match(dash.body, /NEAR 50 {3}MEDIUM 112 {3}FAR 176/);
    assert.match(dash.body, /not FPS/);
  });

  await t.test("operator changes profile to LOW; it applies and is saved", async () => {
    __ui.answer(pick("Performance"));
    __ui.answer(submit({ "Performance profile": 0, "Show status HUD": true }));
    __ui.answer(close()); // main menu again
    __mc.runCommand("rw:menu", steve);
    await settle(5);
    assert.ok(steve.messages.some((m) => m.includes("Performance settings applied")));
    await __mc.tick(25);
    assert.match(__mc.runCommand("rw:status", steve).message, /profile LOW/);
    // LOW = 9x9 per player
    assert.match(__mc.runCommand("rw:status", steve).message, /NEAR 18 MEDIUM 32 FAR 112/);
    // HUD switched on for Steve only
    await __mc.tick(25);
    assert.ok(steve.onScreenDisplay.actionBar.length > 0);
    assert.match(steve.onScreenDisplay.actionBar.at(-1), /RW.*N18 M32 F112/);
    assert.equal(alex.onScreenDisplay.actionBar.length, 0);
  });

  await t.test("modal values are read correctly if labels take no slot", async () => {
    __ui.labelsTakeSlot = false;
    __ui.answer(pick("Performance"));
    __ui.answer(submit({ "Performance profile": 1 }));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(5);
    __ui.labelsTakeSlot = true;
    assert.match(__mc.runCommand("rw:status", steve).message, /profile MEDIUM/);
  });

  await t.test("non-operator gets read-only pages", async () => {
    __ui.answer(pick("Performance"));
    __ui.answer(submit({ "Show status HUD": true }));
    __ui.answer(close());
    __mc.runCommand("rw:menu", alex);
    await settle(5);
    const perfForm = __ui.shown.at(-2);
    assert.ok(!perfForm.elements.some((e) => e.text?.includes("Performance profile")), "no profile control for non-operators");
    assert.match(__mc.runCommand("rw:status", alex).message, /profile MEDIUM/, "unchanged");
    assert.equal(alex.getDynamicProperty("rw:hud"), true, "own HUD preference still allowed");

    __ui.answer(pick("General Settings"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", alex);
    await settle(5);
    assert.ok(alex.messages.some((m) => m.includes("Only operators")));
  });

  await t.test("systems page switches a system off and on", async () => {
    __ui.answer(pick("Systems"));
    __ui.answer(submit({ "Status HUD": false }));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(5);
    const before = steve.onScreenDisplay.actionBar.length;
    await __mc.tick(40);
    assert.equal(steve.onScreenDisplay.actionBar.length, before, "HUD stopped");
    __ui.answer(pick("Systems"));
    __ui.answer(submit({ "Status HUD": true }));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(5);
    await __mc.tick(40);
    assert.ok(steve.onScreenDisplay.actionBar.length > before, "HUD running again");
  });

  await t.test("using the control center item opens the menu", async () => {
    const n = __ui.shown.length;
    __ui.answer(close());
    world.afterEvents.itemUse.fire({ source: steve, itemStack: { typeId: "rw:control_center" } });
    world.afterEvents.itemUse.fire({ source: steve, itemStack: { typeId: "minecraft:stick" } });
    await settle(3);
    assert.equal(__ui.shown.length, n + 1, "only our item opens it");
  });

  await t.test("/scriptevent rw:menu opens the menu", async () => {
    const n = __ui.shown.length;
    __ui.answer(close());
    system.afterEvents.scriptEventReceive.fire({ id: "rw:menu", message: "", sourceEntity: steve });
    system.afterEvents.scriptEventReceive.fire({ id: "other:thing", message: "", sourceEntity: steve });
    await settle(3);
    assert.equal(__ui.shown.length, n + 1);
  });

  await t.test("self-test passes in the running world (command and UI)", async () => {
    __mc.runCommand("rw:selftest", steve);
    await settle(2);
    __mc.runCommand("rw:selftest", steve);
    await settle(2);
    const report = steve.messages.at(-1);
    assert.match(report, /self-test: 5\/5 passed/, report);
    __ui.answer(pick("Run Self-Test"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(3);
    assert.match(__ui.last().body, /5\/5 checks passed/);
  });

  const overworld = world.getDimension("minecraft:overworld");
  const openWeather = async (...answers) => {
    __ui.answer(pick("Weather"));
    for (const a of answers) __ui.answer(a);
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(5);
  };

  await t.test("weather: engine sets the game's weather; climate data builds up", async () => {
    await __mc.tick(200);
    assert.ok(overworld.weatherCalls.length > 0, "engine sent the vanilla weather");
    assert.ok(overworld.weatherCalls.every((c) => c.duration >= 6000));
    __ui.answer(pick("Dashboard"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(3);
    const n = Number(/Regions with climate data: (\d+)/.exec(__ui.last().body)[1]);
    assert.ok(n > 50, `regions with climate data: ${n}`);
  });

  await t.test("weather: operator forces a thunderstorm from the Weather page", async () => {
    await openWeather(pick("Force weather"), submit({ Condition: 4, Duration: 5 }));
    assert.equal(overworld.weatherCalls.at(-1).type, "Thunder");
    assert.equal(__mc.weather, "Thunder");
    assert.ok(steve.messages.at(-1).includes("thunderstorm for 5 min"));
    await openWeather();
    const page = __ui.last();
    assert.equal(page.title, "Weather");
    assert.match(page.body, /Overall§r: thunderstorm/);
    assert.match(page.body, /Forced: thunderstorm/);
    assert.match(page.body, /Temperature: §f-?\d+\.\d°C/);
    assert.match(page.body, /Soil moisture: \d+%/);
  });

  await t.test("weather: a /weather command typed by a player is followed, not fought", async () => {
    const calls = overworld.weatherCalls.length;
    __mc.typeWeatherCommand("Clear");
    await __mc.tick(300);
    assert.ok(overworld.weatherCalls.slice(calls).every((c) => c.type === "Clear"), "engine never switched it back to rain");
    await openWeather();
    assert.match(__ui.last().body, /Forced: clear/);
  });

  await t.test("weather: forecast page lists 12 periods", async () => {
    await openWeather(pick("Forecast"));
    const fc = __ui.last();
    assert.equal(fc.title, "Forecast");
    assert.equal((fc.body.match(/^Day \d/gm) ?? []).length, 12);
  });

  await t.test("weather: personal weather HUD line", async () => {
    await openWeather(pick("Weather on my HUD: OFF"));
    await __mc.tick(20);
    assert.match(steve.onScreenDisplay.actionBar.at(-1), /°C.*hum \d+%.*wind/);
    assert.ok(!alex.onScreenDisplay.actionBar.at(-1)?.includes("°C"), "other players unaffected");
  });

  await t.test("weather: fog is applied outdoors and removed underground", async () => {
    await openWeather(pick("Force weather"), submit({ Condition: 5, Duration: 10 }));
    await __mc.tick(200);
    assert.deepEqual(steve.fogSettings.getStack(), ["rw:fog_dense"]);
    __mc.skyLightFn = () => 0; // in a cave
    await __mc.tick(100);
    assert.deepEqual(steve.fogSettings.getStack(), []);
    __mc.skyLightFn = () => 15;
    await __mc.tick(100);
    assert.deepEqual(steve.fogSettings.getStack(), ["rw:fog_dense"], "back outside");
  });

  await t.test("settings disable pauses the engine", async () => {
    __ui.answer(pick("General Settings"));
    __ui.answer(pick("Edit settings"));
    __ui.answer(submit({ "Simulation enabled": false }));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(5);
    assert.match(__mc.runCommand("rw:status", steve).message, /PAUSED/);
  });

  let tickBeforeClose = 0;
  await t.test("close and re-enter the world: everything is restored", async () => {
    tickBeforeClose = Number(/tick (\d+)/.exec(__mc.runCommand("rw:status", steve).message)[1]);
    __mc.closeWorld();
    await enterWorld();
    const msg = __mc.runCommand("rw:status", steve).message;
    assert.match(msg, /PAUSED/, "paused setting restored");
    const tickAfter = Number(/tick (\d+)/.exec(msg)[1]);
    assert.equal(tickAfter, tickBeforeClose, "simulation clock continues where it stopped");
    assert.equal(steve.getDynamicProperty("rw:hud"), true, "per-player HUD preference kept");
    __ui.answer(pick("Weather"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(3);
    assert.match(__ui.last().body, /Forced: fog/, "weather state (forced fog) restored after reload");
  });

  await t.test("after reload, regions resume with their away time", async () => {
    // re-enable the simulation through the menu
    __ui.answer(pick("General Settings"));
    __ui.answer(pick("Edit settings"));
    __ui.answer(submit({ "Simulation enabled": true }));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(30);
    __ui.answer(pick("Dashboard"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(3);
    const resumed = Number(/Regions resumed: (\d+)/.exec(__ui.last().body)[1]);
    assert.ok(resumed > 0, "known regions were recognised after reload");
  });

  await t.test("saved data never exceeds the game's per-property limit", () => {
    for (const [, v] of __mc.worldProps.map) {
      if (typeof v === "string") assert.ok(Buffer.byteLength(v) <= 32767);
    }
    assert.ok([...__mc.worldProps.map.keys()].every((k) => k.startsWith("rw:")), "only rw: properties written");
  });

  await t.test("a system crash is isolated and shows in the error log", async () => {
    // Make the region tracker throw by breaking the world player list.
    const orig = world.getAllPlayers;
    world.getAllPlayers = () => {
      throw new Error("simulated engine failure");
    };
    await __mc.tick(20 * 6);
    world.getAllPlayers = orig;
    __ui.answer(pick("Error Log"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(3);
    const log = __ui.last();
    assert.match(log.title, /Error Log \(\d+\)/);
    assert.match(log.body, /simulated engine failure/);
    assert.match(__mc.runCommand("rw:status", steve).message, /RUNNING/, "engine itself still runs");
    __ui.answer(pick("Systems"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(3);
    assert.ok(__ui.last().elements.some((e) => e.text?.includes("FAULTED")), "faulted system visible");
  });

  await t.test("reset settings restores defaults after confirmation", async () => {
    __ui.answer(pick("General Settings"));
    __ui.answer(pick("Reset all settings"));
    __ui.answer(button1());
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await settle(5);
    assert.ok(steve.messages.some((m) => m.includes("reset to defaults")));
    assert.match(__mc.runCommand("rw:status", steve).message, /RUNNING .*\n.*profile MEDIUM/);
  });
});
