import { View } from 'react-native';
import { router } from 'expo-router';
import { Card, EmptyState, ErrorState, ProductArt, Row, Screen, Skeleton, StatusChip, Text } from '../../components/ui';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

// The owner's store: where shopping balance is spent.
export default function Store() {
  const q = useQuery(() => api.listStoreProducts());
  const w = useQuery(() => api.getWallet());
  return (
    <Screen>
      <View>
        <Text variant="2xl" accessibilityRole="header">{t.store.title}</Text>
        <Text tone="text-muted">{t.store.subtitle}</Text>
      </View>
      {w.data ? <Text>{t.store.balance}: <Text weight={700} tone="accent">{formatJD(w.data.shoppingFils)}</Text></Text> : null}
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? [0, 1].map((i) => <Skeleton key={i} height={120} r={radius.lg} />)
        : q.data.length === 0 ? <EmptyState title={t.store.empty} />
        : (
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space(3) }}>
            {q.data.map((p) => (
              <Card key={p.id} label={`${p.titleAr}، ${formatJD(p.priceFils)}`} onPress={() => router.push(`/store/${p.id}`)}
                style={{ width: '47%', flexGrow: 1, maxWidth: 300 }}>
                <ProductArt category={p.category} image={p.images[0]} />
                <Text weight={600} numberOfLines={2}>{p.titleAr}</Text>
                <Row style={{ justifyContent: 'space-between', flexWrap: 'wrap' }}>
                  <Text weight={700} tone="accent">{formatJD(p.priceFils)}</Text>
                  {p.stock === 0 ? <StatusChip label={t.store.outOfStock} tone="danger" />
                    : p.stock <= 3 ? <StatusChip label={t.store.left(p.stock)} tone="warning" /> : null}
                </Row>
              </Card>
            ))}
          </View>
        )}
    </Screen>
  );
}
