import { useState } from 'react';
import { Pressable, View } from 'react-native';
import { Button, Card, ErrorState, Row, Screen, Skeleton, StatusChip, Text } from '../../components/ui';
import { AdminEmpty, AdminOnly } from '../../components/admin';
import { AdminOrder, api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

type Filter = 'all' | 'draw' | 'store';
const LABEL: Record<string, string> = { ...t.orders.status, awaiting_address: t.order.steps.awaiting_address, failed: 'فشل' };
const TONE: Record<string, 'warning' | 'accent' | 'success' | 'danger' | 'neutral'> =
  { pending: 'warning', awaiting_address: 'neutral', shipped: 'accent', delivered: 'success', cancelled: 'danger', failed: 'danger' };

export default function AdminOrders() {
  return <AdminOnly><List /></AdminOnly>;
}

function List() {
  const c = useColors();
  const [filter, setFilter] = useState<Filter>('all');
  const q = useQuery(() => api.adminListOrders());
  const rows = (q.data ?? []).filter((o) => filter === 'all' || o.kind === filter);
  return (
    <Screen edges={['bottom']}>
      <View role="tablist" style={{ flexDirection: 'row', backgroundColor: c.surface, borderRadius: radius.md, padding: 4 }}>
        {(['all', 'draw', 'store'] as Filter[]).map((k) => (
          <Pressable key={k} role="tab" aria-selected={filter === k} onPress={() => setFilter(k)}
            style={{ flex: 1, height: 40, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center', backgroundColor: filter === k ? c['surface-raised'] : 'transparent' }}>
            <Text variant="sm" weight={filter === k ? 600 : 400} tone={filter === k ? 'text' : 'text-muted'}>{t.admin.filter[k]}</Text>
          </Pressable>
        ))}
      </View>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? <Skeleton height={100} r={radius.lg} />
        : rows.length === 0 ? <AdminEmpty title={t.orders.empty} />
        : rows.map((o) => <OrderCard key={o.kind + o.id} o={o} />)}
    </Screen>
  );
}

function OrderCard({ o }: { o: AdminOrder }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const next: string[] = o.status === 'pending' ? ['shipped'] : o.status === 'shipped' ? ['delivered'] : [];
  if (o.kind === 'store' && (o.status === 'pending' || o.status === 'shipped')) next.push('cancelled');
  async function set(s: string) {
    setBusy(true); setError(null);
    try { await api.adminSetOrderStatus(o.kind, o.id, s); } catch { setError(t.common.loadError); } finally { setBusy(false); }
  }
  return (
    <Card>
      <Row style={{ justifyContent: 'space-between' }}>
        <StatusChip label={t.admin.filter[o.kind]} />
        <StatusChip label={LABEL[o.status] ?? o.status} tone={TONE[o.status] ?? 'neutral'} />
      </Row>
      <Text weight={600}>{o.title}</Text>
      {o.totalFils ? <Text>{formatJD(o.totalFils)}</Text> : null}
      <View style={{ gap: 2 }}>
        <Text variant="sm" tone="text-muted">{t.admin.customer}: <Text variant="sm" style={{ writingDirection: 'ltr' }}>{o.customer.phone}</Text></Text>
        <Text variant="sm" tone="text-muted">{o.customer.address ?? t.admin.noAddress}</Text>
      </View>
      {error ? <Text variant="sm" tone="danger">{error}</Text> : null}
      {o.kind === 'draw' && o.status === 'awaiting_address' ? null : (
        <Row style={{ flexWrap: 'wrap', gap: space(2) }}>
          {next.map((s) => <Button key={s} size="sm" title={t.admin.next[s]} variant={s === 'cancelled' ? 'danger' : 'secondary'} loading={busy} onPress={() => set(s)} />)}
        </Row>
      )}
    </Card>
  );
}
