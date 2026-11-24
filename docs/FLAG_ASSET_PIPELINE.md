# Flags & Asset Pipeline

There are three different ways your faction's flag appears. It's important to know which is which.

## 1. Designed flags (everywhere in the interface)

**F7 > Flag Designer** builds a flag from 12 patterns (solid, bicolors, tricolors, Nordic cross, center cross,
bordered, quartered, canton, five stripes, center band), three colors, and an emblem (monogram, star, crown,
card suits, shield). It's drawn live in the header, previews, presets and government screens. Emblems that use game
textures fall back to the monogram automatically if a texture isn't available in your game build.

## 2. Custom image flags (interface only)

Put an image in `scripts/OrganizationEmpire/flags/`:

- Formats: PNG (recommended), JPG, BMP, DDS
- Size: about 512 x 320 (8:5); anything works, it is stretched to the frame
- Keep it under ~1 MB

Then **Flag Designer > Custom flag image**. The package ships sample images (`example_*.png`) and a
`template_512x320.png` to paint over.

These are drawn with ScriptHookV's texture API. Two consequences:
- ScriptHookV can't unload textures, so the mod loads at most 24 distinct custom images per game session.
- ScriptHookV draws them above all other UI, so while a dialog or popup is open the designed (pattern) version is
  shown instead to avoid covering it.

## 3. The physical flag at your headquarters

GTA V can't apply an arbitrary image to a 3D object at runtime from a script. The HQ flag pole is therefore one of the
**game's own flag models**, selected in **Flag Designer > Flag pole model** (San Andreas, Los Santos, many national
flags, emergency services...). Models missing from your game version are skipped and logged.

### Optional: put your own image on a flag pole (advanced, at your own risk)

If you want your design on the pole in the world, you can replace the texture of one of those flag models with a
texture of your own using **OpenIV**'s *mods folder* feature, which leaves the original game files untouched:

1. Install OpenIV and enable its ASI loader / `mods` folder support (OpenIV > Tools > ASI Manager > *OpenIV.ASI*).
2. Pick a flag model you don't care about (e.g. one of the national flags listed in the Flag Designer).
3. In OpenIV, search the game archives for that model name. Locate the model and its texture dictionary (`.ytd`).
4. Switch OpenIV to *Edit mode* - it will offer to copy the archive into the `mods` folder; accept so the original
   stays intact.
5. Export the flag texture to see its exact size and format, paint your design at the same size, and replace the
   texture in the `.ytd` (same name, same size, same compression where possible).
6. Launch the game, then choose that flag model in the Flag Designer. Your design now flies over the HQ.

Remove the modified archive from the `mods` folder to undo. Game updates can overwrite or invalidate `mods` copies;
redo the replacement after an update. This step is optional and outside the mod itself - the mod never modifies game
files.

## Vehicles, uniforms and blips

- **Vehicles**: faction vehicles are painted with the faction's primary/secondary colors (custom RGB paint) and get a
  faction plate. Livery images aren't possible at runtime.
- **Uniforms**: GTA can't recolor or add patches to ambient NPC clothing from a script, so a "uniform" is a consistent
  set of ped models used for hired members and reinforcements (optionally applied to recruits as well).
- **Blips and territory overlay** use the closest available GTA blip color to your faction color.
