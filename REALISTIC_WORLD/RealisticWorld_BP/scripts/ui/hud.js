// Optional status lines on the action bar, switched on per player:
//  - performance line: script time, throttling, active regions, queue
//  - weather line: local condition, temperature, humidity, wind, soil moisture

/**
 * @typedef {{
 *   name: string,
 *   hudEnabled: () => boolean,
 *   weatherHudEnabled: () => boolean,
 *   position: () => { dim: string, x: number, y: number, z: number },
 *   show: (text: string) => void,
 * }} HudPlayer
 */

const PRECIP_LABEL = { rain: "Rain", sleet: "Sleet", snow: "Snow", blizzard: "Blizzard" };

export class StatusHud {
  /**
   * @param {{
   *   getPlayers: () => HudPlayer[],
   *   status: () => { avgTickMs: number, budgetMs: number, throttle: number, near: number, medium: number, far: number, queue: number, paused: boolean },
   *   weatherLine?: (pos: { dim: string, x: number, y: number, z: number }) => string | null,
   * }} deps
   */
  constructor({ getPlayers, status, weatherLine }) {
    this.id = "hud";
    this.name = "Status HUD (action bar)";
    this.intervalTicks = 10;
    this.scalable = true;
    this.getPlayers = getPlayers;
    this.status = status;
    this.weatherLine = weatherLine ?? (() => null);
    this.shownTo = 0;
  }

  static format(s) {
    if (s.paused) return "§eRW§r PAUSED";
    const load = s.avgTickMs > s.budgetMs ? "§c" : s.avgTickMs > s.budgetMs * 0.6 ? "§e" : "§a";
    const thr = s.throttle > 1 ? ` §6x${s.throttle}§r` : "";
    return `§bRW§r ${load}${s.avgTickMs.toFixed(2)}§r/${s.budgetMs}ms${thr} | N${s.near} M${s.medium} F${s.far} | Q${s.queue}`;
  }

  /**
   * @param {{ condition: string, windFrom: string, raining: boolean, intensity: number }} w global weather summary
   * @param {{ tempC: number, humidity: number, wind: number, soil: number | null, precip: string, fog: string | null }} c local sample
   */
  static formatWeather(w, c) {
    const precip = PRECIP_LABEL[/** @type {keyof typeof PRECIP_LABEL} */ (c.precip)];
    const sky = precip ? `${precip} ${Math.round(w.intensity * 100)}%` : w.condition.replace("_", " ").toLowerCase();
    const t = c.tempC <= 0 ? "§b" : c.tempC >= 30 ? "§c" : "§f";
    const soil = c.soil === null ? "" : ` | soil ${Math.round(c.soil * 100)}%`;
    const fog = c.fog ? " | fog" : "";
    return `${t}${c.tempC.toFixed(1)}°C§r ${sky} | hum ${c.humidity}% | wind ${c.wind} m/s ${w.windFrom}${soil}${fog}`;
  }

  update() {
    let shown = 0;
    let perfText = null;
    for (const p of this.getPlayers()) {
      const lines = [];
      if (p.hudEnabled()) {
        perfText ??= StatusHud.format(this.status());
        lines.push(perfText);
      }
      if (p.weatherHudEnabled()) {
        const line = this.weatherLine(p.position());
        if (line) lines.push(line);
      }
      if (lines.length === 0) continue;
      p.show(lines.join("\n"));
      shown++;
    }
    this.shownTo = shown;
    return shown;
  }

  describe() {
    return { viewers: this.shownTo };
  }
}
