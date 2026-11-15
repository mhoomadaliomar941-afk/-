import { View } from 'react-native';
import { router } from 'expo-router';
import { Plus } from 'lucide-react-native';
import { Button, Card, ErrorState, ProductArt, Row, Screen, Skeleton, StatusChip, Text } from '../../components/ui';
import { AdminEmpty, AdminOnly } from '../../components/admin';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

export default function AdminStore() {
  return <AdminOnly><List /></AdminOnly>;
}

function List() {
  const c = useColors();
  const q = useQuery(() => api.adminListStoreProducts());
  return (
    <Screen edges={['bottom']}>
      <Button title={t.admin.newProduct} icon={<Plus color={c['on-accent']} size={18} />} onPress={() => router.push('/admin/store-product/new')} />
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? <Skeleton height={80} r={radius.lg} />
        : q.data.length === 0 ? <AdminEmpty title={t.store.empty} />
        : q.data.map((p) => (
          <Card key={p.id} label={p.titleAr} onPress={() => router.push(`/admin/store-product/${p.id}`)}>
            <Row gap={3}>
              <ProductArt category={p.category} image={p.images[0]} size="thumb" />
              <View style={{ flex: 1, gap: 4 }}>
                <Text weight={600} numberOfLines={1}>{p.titleAr}</Text>
                <Text variant="sm" tone="text-muted">{formatJD(p.priceFils)} · {t.admin.fields.stock}: {'⁦'}{p.stock}{'⁩'}</Text>
                <Row>
                  {!p.active ? <StatusChip label={t.admin.hidden} /> : null}
                  {p.stock === 0 ? <StatusChip label={t.store.outOfStock} tone="danger" /> : null}
                </Row>
              </View>
            </Row>
          </Card>
        ))}
    </Screen>
  );
}
