// Shared form helper.

import { FormCancelationReason } from "@minecraft/server-ui";
import { system } from "@minecraft/server";

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
