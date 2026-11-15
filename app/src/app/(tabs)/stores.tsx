import { useState } from 'react';
import { Pressable, ScrollView, View } from 'react-native';
import { router } from 'expo-router';
import { Store as StoreIcon } from 'lucide-react-native';
import { Card, EmptyState, ErrorState, Row, Screen, Skeleton, Text } from '../../components/ui';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

// S12: partner stores, filtered by governorate.
export default function Stores() {
  const c = useColors();
  const q = useQuery(() => api.listStores());
  const w = useQuery(() => api.getWallet());
  const [gov, setGov] = useState<string | null>(null);
  const govs = [...new Set((q.data ?? []).map((s) => s.governorate))];
  const list = (q.data ?? []).filter((s) => !gov || s.governorate === gov);
  return (
    <Screen>
      <Text variant="2xl" accessibilityRole="header">{t.stores.title}</Text>
      {w.data ? <Text tone="text-muted">{t.wallet.shopping}: <Text weight={600}>{formatJD(w.data.shoppingFils)}</Text></Text> : null}
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: space(2) }} role="radiogroup" aria-label={t.order.governorate}>
        {[null, ...govs].map((g) => {
          const on = gov === g;
          return (
            <Pressable key={g ?? 'all'} role="radio" aria-checked={on} onPress={() => setGov(g)}
              style={{ height: 36, paddingHorizontal: space(4), borderRadius: radius.pill, justifyContent: 'center',
                backgroundColor: on ? c['accent-soft'] : c.surface }}>
              <Text variant="sm" weight={on ? 600 : 400} tone={on ? 'accent-strong' : 'text-muted'}>{g ?? t.stores.all}</Text>
            </Pressable>
          );
        })}
      </ScrollView>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? [0, 1, 2].map((i) => <Skeleton key={i} height={64} r={radius.lg} />)
        : list.length === 0 ? <EmptyState title={t.stores.empty} />
        : list.map((s) => (
          <Card key={s.id} label={`${s.nameAr}، ${s.city}`} onPress={() => router.push(`/redeem/${s.id}`)}>
            <Row gap={3}>
              <View style={{ width: 44, height: 44, borderRadius: radius.md, backgroundColor: c['accent-soft'], alignItems: 'center', justifyContent: 'center' }}>
                <StoreIcon color={c['accent-strong']} size={22} strokeWidth={1.75} />
              </View>
              <View style={{ flex: 1 }}>
                <Text weight={600}>{s.nameAr}</Text>
                <Text variant="sm" tone="text-muted">{s.category} · {s.city}</Text>
              </View>
              <Text variant="sm" tone="accent">{t.stores.redeem}</Text>
            </Row>
          </Card>
        ))}
    </Screen>
  );
}
