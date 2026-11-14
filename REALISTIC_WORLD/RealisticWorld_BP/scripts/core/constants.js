// Identity and version constants shared across the add-on.

export const ADDON_NAME = "Realistic World";
export const ADDON_VERSION = "0.2.0";

/** Script API versions this build was written and type-checked against. */
export const TARGET_GAME_VERSION = "1.26.52";
export const SERVER_MODULE_VERSION = "2.10.0";
export const SERVER_UI_MODULE_VERSION = "2.2.0";

/** Prefix for every dynamic property this add-on writes. */
export const PROP_PREFIX = "rw:";

/** Bumped whenever the saved data layout changes (see migrations in config.js). */
export const DATA_SCHEMA_VERSION = 1;

export const CONTROL_ITEM_ID = "rw:control_center";

/** Storage keys (without prefix). */
export const KEYS = Object.freeze({
  settings: "settings",
  clock: "clock",
  errorLog: "errlog",
  regionSector: (dim, sx, sz) => `reg/${dim}/${sx}/${sz}`,
  selfTest: "selftest",
});
