import { View } from 'react-native';
import { Card, EmptyState, ErrorState, Row, Screen, Skeleton, StatusChip, Text } from '../components/ui';
import { api } from '../data';
import { useQuery } from '../data/useQuery';
import { radius, space } from '../theme';
import { formatJD } from '../lib/money';
import { t } from '../i18n';

const TONE = { pending: 'warning', shipped: 'accent', delivered: 'success', cancelled: 'danger' } as const;

// My store orders.
export default function Orders() {
  const q = useQuery(() => api.listMyStoreOrders());
  return (
    <Screen edges={['bottom']}>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? [0, 1].map((i) => <Skeleton key={i} height={88} r={radius.lg} />)
        : q.data.length === 0 ? <EmptyState title={t.orders.empty} />
        : q.data.map((o) => (
          <Card key={o.id}>
            <Row style={{ justifyContent: 'space-between' }}>
              <Text variant="sm" tone="text-muted">{new Date(o.createdAt).toLocaleDateString('ar-JO', { day: 'numeric', month: 'long' })}</Text>
              <StatusChip label={t.orders.status[o.status]} tone={TONE[o.status]} />
            </Row>
            <View style={{ gap: space(1) }}>
              {o.items.map((i) => <Text key={i.productId}>{i.titleAr || '…'} × {'⁦'}{i.qty}{'⁩'}</Text>)}
            </View>
            <Row style={{ justifyContent: 'space-between' }}>
              <Text tone="text-muted" variant="sm">{t.store.fromShopping} {formatJD(o.paidShoppingFils)} · {t.store.fromCash} {formatJD(o.paidCashFils)}</Text>
              <Text weight={700}>{formatJD(o.totalFils)}</Text>
            </Row>
          </Card>
        ))}
    </Screen>
  );
}
