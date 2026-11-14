// Load with: node --import ./tests/mocks/register.mjs
// Redirects the game's script modules to the test doubles in this folder.
import { register } from "node:module";
register("./hooks.mjs", import.meta.url);
