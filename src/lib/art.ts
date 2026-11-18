import type { IconKey } from './types';

// Line-art glyphs (24x24 viewBox) used to render branded product visuals
// when a product has no uploaded photo.
export const ICON_PATHS: Record<IconKey, string> = {
  shirt: 'M8 3l-5 3 2 4 2-1v12h10V9l2 1 2-4-5-3c0 1.7-1.3 3-4 3S8 4.7 8 3z',
  dress: 'M9 2h6l-1 5 4 14H6l4-14-1-5zM10 7h4',
  headphones: 'M4 15v-3a8 8 0 0116 0v3M4 15h3v6H5a1 1 0 01-1-1v-5zm16 0h-3v6h2a1 1 0 001-1v-5z',
  watch: 'M9 2h6l1 4H8l1-4zm-1 16h8l-1 4H9l-1-4zM12 6a6 6 0 110 12 6 6 0 010-12zm0 3v3l2 1.5',
  perfume: 'M10 2h4v3h-4zM9 5h6v2H9zM6 9a2 2 0 012-2h8a2 2 0 012 2v10a3 3 0 01-3 3H9a3 3 0 01-3-3V9zm4 5h4',
  lamp: 'M8 3h8l3 8H5l3-8zm4 8v8m-4 2h8',
  sneaker: 'M2 17v-5l5-1 3-4 3 4c2 1 5 1 7 2s2 3 2 4v1H2zm0 0h20M9 11l1 2m2-3l1 2',
  bag: 'M5 8h14l-1 13H6L5 8zm4 0V6a3 3 0 016 0v2',
  phone: 'M7 2h10a1 1 0 011 1v18a1 1 0 01-1 1H7a1 1 0 01-1-1V3a1 1 0 011-1zm4 17h2',
  glasses: 'M2 12h2l1-4h3m14 4h-2l-1-4h-3M6 12a3 3 0 106 0 3 3 0 10-6 0zm6 0a3 3 0 106 0 3 3 0 10-6 0z'
};

export function productArt(icon: IconKey, hue: number): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 400">
<defs>
<linearGradient id="b" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue} 45% 94%)"/><stop offset="1" stop-color="hsl(${hue} 40% 82%)"/></linearGradient>
<radialGradient id="s" cx=".5" cy=".55" r=".5"><stop offset="0" stop-color="hsl(${hue} 60% 99%)" stop-opacity=".9"/><stop offset="1" stop-color="hsl(${hue} 60% 99%)" stop-opacity="0"/></radialGradient>
</defs>
<rect width="400" height="400" fill="url(#b)"/>
<circle cx="200" cy="210" r="150" fill="url(#s)"/>
<ellipse cx="200" cy="320" rx="90" ry="12" fill="hsl(${hue} 30% 40%)" opacity=".12"/>
<g transform="translate(80 80) scale(10)" fill="none" stroke="hsl(${hue} 35% 28%)" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round"><path d="${ICON_PATHS[icon]}"/></g>
</svg>`;
  return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
}
