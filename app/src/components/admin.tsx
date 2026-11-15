import { ReactNode, useState } from 'react';
import { Alert, Image, Platform, Pressable, ScrollView, View } from 'react-native';
import * as ImagePicker from 'expo-image-picker';
import { ImagePlus, ShieldAlert, X } from 'lucide-react-native';
import { Button, EmptyState, Screen, Text } from './ui';
import { api } from '../data';
import { useSession } from '../lib/session';
import { radius, space, useColors } from '../theme';
import { t } from '../i18n';

/** Renders children only for admins. The database refuses non-admins anyway; this is just the screen. */
export function AdminOnly({ children }: { children: ReactNode }) {
  const { user, ready } = useSession();
  const c = useColors();
  if (!ready) return null;
  if (!user?.isAdmin) {
    return (
      <Screen edges={[]}>
        <View style={{ alignItems: 'center', gap: space(3), paddingVertical: space(8) }}>
          <ShieldAlert color={c.danger} size={36} />
          <Text variant="lg">{t.admin.only}</Text>
        </View>
      </Screen>
    );
  }
  return <>{children}</>;
}

/** One photo per product for now: pick from the library, upload, keep the URL. */
export function PhotoField({ value, onChange }: { value: string | null; onChange: (url: string | null) => void }) {
  const c = useColors();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function pick() {
    setError(null);
    const r = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], allowsEditing: true, aspect: [4, 3], quality: 0.7 });
    if (r.canceled || !r.assets?.[0]) return;
    setBusy(true);
    try { onChange(await api.uploadProductImage(r.assets[0].uri)); } catch { setError(t.common.loadError); } finally { setBusy(false); }
  }
  return (
    <View style={{ gap: space(2) }}>
      <Text variant="sm" weight={600}>{t.admin.fields.photo}</Text>
      {value ? (
        <View>
          <Image source={{ uri: value }} style={{ width: '100%', aspectRatio: 4 / 3, borderRadius: radius.md }} accessibilityLabel={t.admin.fields.photo} />
          <Pressable role="button" aria-label={t.admin.fields.removePhoto} onPress={() => onChange(null)}
            style={{ position: 'absolute', top: 8, end: 8, width: 36, height: 36, borderRadius: 18, backgroundColor: c['surface-raised'], alignItems: 'center', justifyContent: 'center' }}>
            <X color={c.text} size={18} />
          </Pressable>
        </View>
      ) : <Button title={t.admin.fields.addPhoto} variant="secondary" loading={busy} icon={<ImagePlus color={c['accent-strong']} size={18} />} onPress={pick} />}
      {error ? <Text variant="sm" tone="danger">{error}</Text> : null}
    </View>
  );
}

export function CategoryPicker<T extends string>({ value, options, onChange }: { value: T; options: readonly T[]; onChange: (v: T) => void }) {
  const c = useColors();
  return (
    <View style={{ gap: space(2) }}>
      <Text variant="sm" weight={600}>{t.admin.fields.category}</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} role="radiogroup" aria-label={t.admin.fields.category} contentContainerStyle={{ gap: space(2) }}>
        {options.map((o) => {
          const on = o === value;
          return (
            <Pressable key={o} role="radio" aria-checked={on} onPress={() => onChange(o)}
              style={{ height: 36, paddingHorizontal: space(4), borderRadius: radius.pill, justifyContent: 'center', backgroundColor: on ? c['accent-soft'] : c.surface }}>
              <Text variant="sm" weight={on ? 600 : 400} tone={on ? 'accent-strong' : 'text-muted'}>{t.admin.categories[o]}</Text>
            </Pressable>
          );
        })}
      </ScrollView>
    </View>
  );
}

export const AdminEmpty = ({ title }: { title: string }) => <EmptyState title={title} />;

/** A yes/no question that works on phones and on the web build. */
export function confirmAsk(message: string, onYes: () => void) {
  if (Platform.OS === 'web') { if (window.confirm(message)) onYes(); return; }
  Alert.alert('', message, [{ text: t.common.cancel, style: 'cancel' }, { text: 'نعم', style: 'destructive', onPress: onYes }]);
}
