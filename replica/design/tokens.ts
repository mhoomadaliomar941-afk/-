// Generated from tokens.json. Do not edit by hand: change the JSON and regenerate.
// Components import roles from here and never use a raw hex value.
export const tokens = {
  "color": {
    "bg": "#fff8ec",
    "surface": "#ffffff",
    "scene": "#eaf4ff",
    "border": "#e4d9c6",
    "border-input": "#857a66",
    "text": "#1d2433",
    "text-muted": "#565d6e",
    "accent": "#2f5bea",
    "on-accent": "#ffffff",
    "reward": "#ffc93c",
    "on-reward": "#3a2a00",
    "danger": "#c62f2f",
    "success": "#1f7a4d",
    "on-status": "#ffffff",
    "locked": "#ece6da",
    "on-locked": "#6a6252"
  },
  "font": {
    "sans": "Baloo Bhaijaan 2, system-ui, Roboto, sans-serif",
    "mono": "ui-monospace, Menlo, monospace"
  },
  "type": {
    "sm": {
      "size": 16,
      "line": 24,
      "weight": 500
    },
    "base": {
      "size": 18,
      "line": 28,
      "weight": 500
    },
    "lg": {
      "size": 22,
      "line": 32,
      "weight": 600
    },
    "xl": {
      "size": 28,
      "line": 38,
      "weight": 700
    },
    "display": {
      "size": 40,
      "line": 52,
      "weight": 800
    }
  },
  "space": [
    0,
    4,
    8,
    12,
    16,
    24,
    32,
    48,
    64
  ],
  "radius": {
    "sm": 10,
    "md": 16,
    "lg": 24,
    "pill": 999
  },
  "size": {
    "touch-min": 56,
    "icon-button": 56,
    "button": 60,
    "level-tile": 64,
    "content-max": 520
  },
  "shadow": {
    "card": "0 2px 0 rgba(29,36,51,.10)",
    "button": "0 4px 0 rgba(29,36,51,.22)",
    "pop": "0 12px 32px rgba(29,36,51,.20)"
  },
  "motion": {
    "fast": "120ms",
    "base": "220ms",
    "celebrate": "600ms",
    "ease": "cubic-bezier(.2,.8,.2,1)"
  }
} as const;

export type ColorRole = keyof typeof tokens.color;
