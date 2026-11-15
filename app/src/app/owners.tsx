import { View } from 'react-native';
import { Card, EmptyState, ErrorState, ProductArt, Row, Screen, Skeleton, Text } from '../components/ui';
import { api } from '../data';
import { useQuery } from '../data/useQuery';
import { radius, useColors } from '../theme';
import { t } from '../i18n';

// S11: owners who agreed to be shown. First name + initial only.
export default function Owners() {
  const c = useColors();
  const q = useQuery(() => api.listShowcase());
  return (
    <Screen edges={['bottom']}>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? [0, 1].map((i) => <Skeleton key={i} height={88} r={radius.lg} />)
        : q.data.length === 0 ? <EmptyState title={t.owners.empty} />
        : q.data.map((o) => (
          <Card key={o.id}>
            <Row gap={3}>
              <ProductArt category={o.category} size="thumb" />
              <View style={{ flex: 1 }}>
                <Text weight={600}>{o.displayName}</Text>
                <Text variant="sm" tone="text-muted">{o.productTitle}</Text>
                <Text variant="xs" tone="text-muted">{new Date(o.date).toLocaleDateString('ar-JO', { day: 'numeric', month: 'long' })}</Text>
              </View>
              <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: c.success }} />
            </Row>
          </Card>
        ))}
    </Screen>
  );
}
