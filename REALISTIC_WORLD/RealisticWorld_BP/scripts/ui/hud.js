// Optional status line on the action bar for players who switch it on.
// Shows measured script time, throttling, active regions and queue size, so the
// performance can be watched on a phone while playing.

/**
 * @typedef {{ name: string, hudEnabled: () => boolean, show: (text: string) => void }} HudPlayer
 */

export class StatusHud {
  /**
   * @param {{
   *   getPlayers: () => HudPlayer[],
   *   status: () => { avgTickMs: number, budgetMs: number, throttle: number, near: number, medium: number, far: number, queue: number, paused: boolean },
   * }} deps
   */
  constructor({ getPlayers, status }) {
    this.id = "hud";
    this.name = "Status HUD (action bar)";
    this.intervalTicks = 10;
    this.scalable = true;
    this.getPlayers = getPlayers;
    this.status = status;
    this.shownTo = 0;
  }

  static format(s) {
    if (s.paused) return "§eRW§r PAUSED";
    const load = s.avgTickMs > s.budgetMs ? "§c" : s.avgTickMs > s.budgetMs * 0.6 ? "§e" : "§a";
    const thr = s.throttle > 1 ? ` §6x${s.throttle}§r` : "";
    return `§bRW§r ${load}${s.avgTickMs.toFixed(2)}§r/${s.budgetMs}ms${thr} | N${s.near} M${s.medium} F${s.far} | Q${s.queue}`;
  }

  update() {
    const viewers = this.getPlayers().filter((p) => p.hudEnabled());
    this.shownTo = viewers.length;
    if (viewers.length === 0) return 0;
    const text = StatusHud.format(this.status());
    for (const p of viewers) p.show(text);
    return viewers.length;
  }

  describe() {
    return { viewers: this.shownTo };
  }
}
