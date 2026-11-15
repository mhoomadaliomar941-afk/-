import { router } from 'expo-router';
import { Plus } from 'lucide-react-native';
import { Button, Card, ErrorState, ProductArt, productChip, Row, Screen, Skeleton, StatusChip, Text } from '../../components/ui';
import { AdminEmpty, AdminOnly } from '../../components/admin';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';
import { View } from 'react-native';

export default function AdminProducts() {
  return <AdminOnly><List /></AdminOnly>;
}

function List() {
  const c = useColors();
  const q = useQuery(() => api.adminListProducts());
  return (
    <Screen edges={['bottom']}>
      <Button title={t.admin.newProduct} icon={<Plus color={c['on-accent']} size={18} />} onPress={() => router.push('/admin/product/new')} />
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? <Skeleton height={80} r={radius.lg} />
        : q.data.length === 0 ? <AdminEmpty title={t.feed.empty} />
        : q.data.map((p) => (
          <Card key={p.id} label={p.titleAr} onPress={() => router.push(`/admin/product/${p.id}`)}>
            <Row gap={3}>
              <ProductArt category={p.category} image={p.images[0]} size="thumb" />
              <View style={{ flex: 1, gap: 4 }}>
                <Text weight={600} numberOfLines={1}>{p.titleAr}</Text>
                <Text variant="sm" tone="text-muted">{'⁦'}{p.filledSpots} / {p.totalSpots}{'⁩'} · {formatJD(p.spotPriceFils)}</Text>
                <StatusChip {...(p.status === 'draft' ? { label: t.admin.statusDraft, tone: 'neutral' as const } : productChip(p.status, p.filledSpots, p.totalSpots))} />
              </View>
            </Row>
          </Card>
        ))}
    </Screen>
  );
}
