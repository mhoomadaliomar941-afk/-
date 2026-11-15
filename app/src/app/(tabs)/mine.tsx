import { useState } from 'react';
import { Pressable, View } from 'react-native';
import { router } from 'expo-router';
import { Card, EmptyState, ErrorState, ProductArt, productChip, Row, Screen, Skeleton, SpotsProgress, StatusChip, Text } from '../../components/ui';
import { api, Participation, Product } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

type Tab = 'active' | 'closed' | 'owned';
type Item = Participation & { product: Product };

// S08: my spots, grouped by product, in three tabs.
export default function Mine() {
  const c = useColors();
  const [tab, setTab] = useState<Tab>('active');
  const q = useQuery(() => api.listParticipations());
  const groups = new Map<string, Item[]>();
  (q.data ?? []).forEach((x) => groups.set(x.productId, [...(groups.get(x.productId) ?? []), x]));
  const rows = [...groups.values()].filter((g) => {
    const s = g[0].status;
    return tab === 'active' ? s === 'active' : tab === 'owned' ? s === 'owner' : s === 'credited' || s === 'refunded';
  });

  return (
    <Screen>
      <Text variant="2xl" accessibilityRole="header">{t.mine.title}</Text>
      <View accessibilityRole="tablist" style={{ flexDirection: 'row', backgroundColor: c.surface, borderRadius: radius.md, padding: 4 }}>
        {(['active', 'closed', 'owned'] as Tab[]).map((k) => (
          <Pressable key={k} accessibilityRole="tab" accessibilityState={{ selected: tab === k }} onPress={() => setTab(k)}
            style={{ flex: 1, height: 40, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center', backgroundColor: tab === k ? c['surface-raised'] : 'transparent' }}>
            <Text variant="sm" weight={tab === k ? 600 : 400} tone={tab === k ? 'text' : 'text-muted'}>{t.mine[k]}</Text>
          </Pressable>
        ))}
      </View>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? [0, 1].map((i) => <Card key={i}><Row gap={3}><Skeleton width={64} height={64} r={8} /><View style={{ flex: 1, gap: 8 }}><Skeleton width="80%" /><Skeleton width="50%" /></View></Row></Card>)
        : rows.length === 0 ? <EmptyState title={t.mine.empty} action={t.mine.emptyAction} onAction={() => router.push('/(tabs)')} />
        : rows.map((g) => {
          const p = g[0].product;
          const spots = g.reduce((s, x) => s + x.spots, 0);
          const paid = g.reduce((s, x) => s + x.amountFils, 0);
          const chip = productChip(p.status, p.filledSpots, p.totalSpots);
          const done = p.status === 'assigned' || p.status === 'delivered';
          return (
            <Card key={p.id} label={p.titleAr} onPress={() => router.push(done || p.status === 'full' ? `/result/${p.id}` : `/product/${p.id}`)}>
              <Row gap={3} style={{ alignItems: 'flex-start' }}>
                <ProductArt category={p.category} size="thumb" />
                <View style={{ flex: 1, gap: space(1) }}>
                  <Text weight={600} numberOfLines={2}>{p.titleAr}</Text>
                  <Text variant="sm" tone="text-muted">{t.mine.spots(spots)} · {formatJD(paid)}</Text>
                  <StatusChip {...(p.status === 'full' ? { label: t.mine.waiting, tone: 'warning' as const } : chip)} />
                </View>
              </Row>
              {!done ? <SpotsProgress filled={p.filledSpots} total={p.totalSpots} /> : null}
            </Card>
          );
        })}
    </Screen>
  );
}
