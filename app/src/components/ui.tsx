// Primitives from replica/design/components.md. Colours and sizes come only from tokens.
import { ReactNode, useEffect, useState } from 'react';
import {
  ActivityIndicator, Animated, Platform, Pressable, PressableStateCallbackType, ScrollView, StyleProp, Text as RNText, TextInput, TextProps,
  TextStyle, View, ViewStyle,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CircleAlert, Gamepad2, Headphones, Inbox, Laptop, Minus, Plus, Smartphone, Tablet, Watch } from 'lucide-react-native';
import { layout, radius, space, typeStyle, TypeRole, useColors, Colors } from '../theme';
import { t } from '../i18n';
import type { Category, ProductStatus } from '../data';

// ---------------------------------------------------------------- text

type TextTone = 'text' | 'text-muted' | 'accent' | 'accent-strong' | 'danger' | 'success' | 'warning' | 'on-accent';
export function Text({ variant = 'base', tone = 'text', weight, style, ...rest }:
  TextProps & { variant?: TypeRole; tone?: TextTone; weight?: 400 | 500 | 600 | 700 }) {
  const c = useColors();
  const ts = typeStyle(variant);
  const w = weight ? { fontFamily: fontFor(weight) } : null;
  return <RNText {...rest} style={[ts, w, { color: c[tone], textAlign: 'auto', writingDirection: 'rtl' } as TextStyle, style]} />;
}
const fontFor = (w: 400 | 500 | 600 | 700) =>
  ({ 400: 'IBMPlexSansArabic_400Regular', 500: 'IBMPlexSansArabic_500Medium', 600: 'IBMPlexSansArabic_600SemiBold', 700: 'IBMPlexSansArabic_700Bold' })[w];

// ---------------------------------------------------------------- layout

export function Screen({ children, scroll = true, padded = true, edges = ['top'] }:
  { children: ReactNode; scroll?: boolean; padded?: boolean; edges?: ('top' | 'bottom')[] }) {
  const c = useColors();
  const inner = { padding: padded ? layout['screen-gutter'] : 0, gap: space(4), maxWidth: 640, width: '100%', alignSelf: 'center' } as ViewStyle;
  return (
    <SafeAreaView edges={edges} style={{ flex: 1, backgroundColor: c.bg }}>
      {scroll ? <ScrollView contentContainerStyle={inner} keyboardShouldPersistTaps="handled">{children}</ScrollView>
        : <View style={[inner, { flex: 1 }]}>{children}</View>}
    </SafeAreaView>
  );
}

export function Row({ children, style, gap = 2 }: { children: ReactNode; style?: StyleProp<ViewStyle>; gap?: number }) {
  return <View style={[{ flexDirection: 'row', alignItems: 'center', gap: space(gap) }, style]}>{children}</View>;
}

export function Card({ children, style, onPress, label }:
  { children: ReactNode; style?: StyleProp<ViewStyle>; onPress?: () => void; label?: string }) {
  const c = useColors();
  const base: ViewStyle = { backgroundColor: c['surface-raised'], borderRadius: radius.lg, borderWidth: 1, borderColor: c.border, padding: space(3), gap: space(2) };
  if (!onPress) return <View style={[base, style]}>{children}</View>;
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={label}
      style={(st) => [base, st.pressed && { opacity: 0.9 }, isFocused(st) && focusRing(c), style]}>
      {children}
    </Pressable>
  );
}

// react-native-web adds `focused` to the Pressable state; native has no keyboard focus ring.
const isFocused = (st: PressableStateCallbackType) => !!(st as PressableStateCallbackType & { focused?: boolean }).focused;

const focusRing = (c: Colors): ViewStyle => (Platform.OS === 'web'
  ? ({ outlineColor: c.focus, outlineWidth: 2, outlineStyle: 'solid', outlineOffset: 2 } as unknown as ViewStyle)
  : { borderColor: c.focus, borderWidth: 2 });

// ---------------------------------------------------------------- button

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
export function Button({ title, onPress, variant = 'primary', size = 'md', disabled, loading, full, icon }:
  { title: string; onPress?: () => void; variant?: Variant; size?: 'sm' | 'md' | 'lg'; disabled?: boolean; loading?: boolean; full?: boolean; icon?: ReactNode }) {
  const c = useColors();
  const bg = { primary: c.accent, secondary: c['accent-soft'], ghost: 'transparent', danger: c.danger }[variant];
  const fg = { primary: c['on-accent'], secondary: c['accent-strong'], ghost: c.accent, danger: c['on-danger'] }[variant];
  const h = { sm: 36, md: 44, lg: 52 }[size];
  const off = disabled || loading;
  return (
    <Pressable onPress={off ? undefined : onPress} accessibilityRole="button" accessibilityLabel={title}
      disabled={!!disabled} aria-busy={!!loading}
      style={(st) => [{
        height: h, paddingHorizontal: space(4), borderRadius: radius.md, backgroundColor: bg, flexDirection: 'row',
        alignItems: 'center', justifyContent: 'center', gap: space(2), opacity: disabled ? 0.4 : st.pressed ? 0.85 : 1,
        alignSelf: full ? 'stretch' : 'flex-start', transform: [{ scale: st.pressed && !off ? 0.98 : 1 }],
      }, isFocused(st) && focusRing(c)]}>
      {loading ? <ActivityIndicator color={fg} size="small" /> : icon}
      <RNText style={[typeStyle('base'), { fontFamily: fontFor(600), color: fg }]}>{title}</RNText>
    </Pressable>
  );
}

// ---------------------------------------------------------------- chips & progress

type ChipTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger';
export function StatusChip({ label, tone = 'neutral' }: { label: string; tone?: ChipTone }) {
  const c = useColors();
  const map: Record<ChipTone, [string, string]> = {
    neutral: [c.surface, c['text-muted']], accent: [c['accent-soft'], c['accent-strong']],
    success: [c['success-soft'], c.success], warning: [c['warning-soft'], c.warning], danger: [c['danger-soft'], c.danger],
  };
  const [bg, fg] = map[tone];
  return (
    <View style={{ backgroundColor: bg, borderRadius: radius.pill, paddingHorizontal: space(2) + 2, height: 24, justifyContent: 'center', alignSelf: 'flex-start' }}>
      <Text variant="xs" style={{ color: fg }}>{label}</Text>
    </View>
  );
}

export function productChip(status: ProductStatus, filled: number, total: number): { label: string; tone: ChipTone } {
  if (status === 'assigned' || status === 'delivered') return { label: t.product.assigned, tone: 'success' };
  if (status === 'full') return { label: t.product.full, tone: 'accent' };
  if (status === 'cancelled') return { label: t.product.cancelled, tone: 'danger' };
  const left = total - filled;
  if (left <= Math.max(3, Math.ceil(total * 0.1))) return { label: t.product.left(left), tone: 'warning' };
  return { label: t.product.open, tone: 'accent' };
}

export function SpotsProgress({ filled, total, size = 'thin', done }: { filled: number; total: number; size?: 'thin' | 'large'; done?: boolean }) {
  const label = t.product.spotsTaken(String(filled), String(total));
  const c = useColors();
  const pct = Math.min(1, filled / total);
  const [anim] = useState(() => new Animated.Value(pct));
  useEffect(() => { Animated.timing(anim, { toValue: pct, duration: 220, useNativeDriver: false }).start(); }, [pct, anim]);
  return (
    <View role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={total} aria-valuenow={filled}
      style={{ height: size === 'thin' ? 6 : 10, borderRadius: radius.pill, backgroundColor: c['progress-track'], overflow: 'hidden' }}>
      <Animated.View style={{ height: '100%', borderRadius: radius.pill, backgroundColor: done || pct >= 1 ? c.success : c['progress-fill'],
        width: anim.interpolate({ inputRange: [0, 1], outputRange: ['0%', '100%'] }) }} />
    </View>
  );
}

// ---------------------------------------------------------------- product art (no photos of anyone's products)

const ART: Record<Category, typeof Smartphone> = { phone: Smartphone, console: Gamepad2, watch: Watch, audio: Headphones, laptop: Laptop, tablet: Tablet };
export function ProductArt({ category, size = 'card' }: { category: Category; size?: 'card' | 'thumb' | 'hero' }) {
  const c = useColors();
  const Icon = ART[category];
  const dims: ViewStyle = size === 'thumb' ? { width: 64, height: 64 } : { width: '100%', aspectRatio: size === 'hero' ? 1.2 : 4 / 3 };
  return (
    <View accessible={false} style={[dims, { borderRadius: size === 'thumb' ? radius.sm : radius.md, backgroundColor: c['accent-soft'], alignItems: 'center', justifyContent: 'center' }]}>
      <Icon color={c['accent-strong']} size={size === 'thumb' ? 28 : 72} strokeWidth={1.25} />
    </View>
  );
}

// ---------------------------------------------------------------- stepper

export function QuantityStepper({ value, min = 1, max, onChange, disabled }:
  { value: number; min?: number; max: number; onChange: (n: number) => void; disabled?: boolean }) {
  const c = useColors();
  const btn = (label: string, Icon: typeof Plus, next: number, off: boolean) => (
    <Pressable accessibilityRole="button" accessibilityLabel={label} disabled={off} onPress={off ? undefined : () => onChange(next)}
      style={(st) => [{ width: 44, height: 44, borderRadius: radius.md, borderWidth: 1, borderColor: c.border, backgroundColor: c.surface,
        alignItems: 'center', justifyContent: 'center', opacity: off ? 0.4 : 1 }, isFocused(st) && focusRing(c)]}>
      <Icon color={c.text} size={20} />
    </Pressable>
  );
  return (
    <Row gap={3} >
      {btn('زيادة', Plus, value + 1, !!disabled || value >= max)}
      <Text variant="xl" accessibilityLiveRegion="polite" style={{ minWidth: 32, textAlign: 'center', fontVariant: ['tabular-nums'] }}>{value}</Text>
      {btn('إنقاص', Minus, value - 1, !!disabled || value <= min)}
    </Row>
  );
}

// ---------------------------------------------------------------- fields

export function TextField({ label, value, onChangeText, error, prefix, keyboardType, placeholder, ltr, autoFocus, maxLength, testID }:
  { label: string; value: string; onChangeText: (s: string) => void; error?: string | null; prefix?: string; keyboardType?: 'default' | 'phone-pad' | 'number-pad' | 'decimal-pad';
    placeholder?: string; ltr?: boolean; autoFocus?: boolean; maxLength?: number; testID?: string }) {
  const c = useColors();
  const [focused, setFocused] = useState(!!autoFocus);
  return (
    <View style={{ gap: 6 }}>
      <Text variant="sm" weight={600}>{label}</Text>
      <View style={{ flexDirection: 'row', alignItems: 'center', height: 52, borderRadius: radius.md, borderWidth: error || focused ? 2 : 1,
        borderColor: error ? c.danger : focused ? c.accent : c['border-input'], backgroundColor: c.bg, paddingHorizontal: space(3), gap: space(2) }}>
        <TextInput value={value} onChangeText={onChangeText} accessibilityLabel={label} keyboardType={keyboardType} placeholder={placeholder}
          placeholderTextColor={c['text-muted']} autoFocus={autoFocus} maxLength={maxLength} testID={testID}
          onFocus={() => setFocused(true)} onBlur={() => setFocused(false)}
          style={[typeStyle('base'), { flex: 1, minWidth: 0, color: c.text, height: '100%', textAlign: ltr ? 'left' : 'right', writingDirection: ltr ? 'ltr' : 'rtl' } as TextStyle,
            Platform.OS === 'web' && ({ outlineStyle: 'none' } as unknown as TextStyle)]} />
        {prefix ? <Text tone="text-muted" style={{ writingDirection: 'ltr' }}>{prefix}</Text> : null}
      </View>
      {error ? <Text variant="sm" tone="danger" accessibilityLiveRegion="assertive">{error}</Text> : null}
    </View>
  );
}

// ---------------------------------------------------------------- states

export function EmptyState({ title, body, action, onAction }: { title: string; body?: string; action?: string; onAction?: () => void }) {
  const c = useColors();
  return (
    <View style={{ alignItems: 'center', paddingVertical: space(8), gap: space(3) }}>
      <View style={{ width: 72, height: 72, borderRadius: radius.pill, backgroundColor: c.surface, alignItems: 'center', justifyContent: 'center' }}>
        <Inbox color={c['text-muted']} size={32} strokeWidth={1.5} />
      </View>
      <Text variant="lg" style={{ textAlign: 'center' }}>{title}</Text>
      {body ? <Text tone="text-muted" style={{ textAlign: 'center' }}>{body}</Text> : null}
      {action ? <Button title={action} variant="secondary" onPress={onAction} /> : null}
    </View>
  );
}

export function ErrorState({ onRetry }: { onRetry: () => void }) {
  const c = useColors();
  return (
    <View style={{ alignItems: 'center', paddingVertical: space(8), gap: space(3) }} accessibilityLiveRegion="assertive">
      <CircleAlert color={c.danger} size={32} />
      <Text variant="lg">{t.common.loadError}</Text>
      <Button title={t.common.retry} variant="secondary" onPress={onRetry} />
    </View>
  );
}

export function Skeleton({ width = '100%', height = 14, r = 6 }: { width?: number | `${number}%`; height?: number; r?: number }) {
  const c = useColors();
  return <View style={{ width, height, borderRadius: r, backgroundColor: c.surface }} />;
}

export function CardSkeleton() {
  return (
    <Card>
      <Skeleton height={180} r={radius.md} />
      <Skeleton width="70%" />
      <Skeleton width="40%" />
    </Card>
  );
}

export function SectionTitle({ children }: { children: ReactNode }) {
  return <Text variant="lg" accessibilityRole="header">{children}</Text>;
}

export function Divider() {
  const c = useColors();
  return <View style={{ height: 1, backgroundColor: c.border }} />;
}
