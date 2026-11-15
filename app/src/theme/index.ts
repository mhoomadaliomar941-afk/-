import { useColorScheme } from 'react-native';
import { tokens, type ColorRole } from './tokens.generated';

export type Colors = Record<ColorRole, string>;
export type TypeRole = keyof typeof tokens.type;

export const space = (n: number) => tokens.space[n] ?? n * 4;
export const radius = tokens.radius;
export const layout = tokens.layout;

export const fonts = {
  400: 'IBMPlexSansArabic_400Regular',
  500: 'IBMPlexSansArabic_500Medium',
  600: 'IBMPlexSansArabic_600SemiBold',
  700: 'IBMPlexSansArabic_700Bold',
} as const;

export function typeStyle(role: TypeRole) {
  const t = tokens.type[role];
  return { fontSize: t.size, lineHeight: t.line, fontFamily: fonts[t.weight as keyof typeof fonts] };
}

export function useColors(): Colors {
  return useColorScheme() === 'dark' ? tokens.dark : tokens.light;
}
