import { useState } from 'react';
import { View } from 'react-native';
import { Button, Card, ErrorState, Row, Screen, Skeleton, StatusChip, Text, TextField } from '../../components/ui';
import { AdminEmpty, AdminOnly, confirmAsk } from '../../components/admin';
import { api, CliqTopup } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

const TONE = { awaiting_transfer: 'neutral', awaiting_review: 'warning', approved: 'success', rejected: 'danger' } as const;

export default function AdminCliq() {
  return <AdminOnly><List /></AdminOnly>;
}

function List() {
  const q = useQuery(() => api.adminListCliqTopups());
  return (
    <Screen edges={['bottom']}>
      <Card><Text variant="sm">{t.admin.checkBank}</Text></Card>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? <Skeleton height={120} r={radius.lg} />
        : q.data.length === 0 ? <AdminEmpty title={t.orders.empty} />
        : q.data.map((r) => <Request key={r.id} r={r} />)}
    </Screen>
  );
}

function Request({ r }: { r: CliqTopup }) {
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const open = r.status === 'awaiting_review' || r.status === 'awaiting_transfer';
  async function review(approve: boolean) {
    setBusy(true); setError(null);
    try { await api.adminReviewCliq(r.id, approve, approve ? undefined : reason); } catch { setError(t.common.loadError); } finally { setBusy(false); }
  }
  return (
    <Card label={`${r.reference} ${formatJD(r.amountFils)}`}>
      <Row style={{ justifyContent: 'space-between' }}>
        <Text variant="xl" style={{ writingDirection: 'ltr' }} selectable>{r.reference}</Text>
        <StatusChip label={t.cliq.status[r.status]} tone={TONE[r.status]} />
      </Row>
      <Text variant="lg" weight={700}>{formatJD(r.amountFils)}</Text>
      <Text variant="sm" tone="text-muted">{t.admin.member}: <Text variant="sm" style={{ writingDirection: 'ltr' }}>{r.member?.phone}</Text> · {new Date(r.createdAt).toLocaleString('ar-JO', { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })}</Text>
      {r.rejectReason ? <Text variant="sm" tone="danger">{r.rejectReason}</Text> : null}
      {error ? <Text variant="sm" tone="danger">{error}</Text> : null}
      {open && !rejecting ? (
        <Row style={{ flexWrap: 'wrap', gap: space(2) }}>
          <Button size="sm" title={t.admin.approve} loading={busy}
            onPress={() => confirmAsk(t.admin.approveConfirm(formatJD(r.amountFils), r.reference), () => review(true))} />
          <Button size="sm" title={t.admin.reject} variant="danger" disabled={busy} onPress={() => setRejecting(true)} />
        </Row>
      ) : null}
      {open && rejecting ? (
        <View style={{ gap: space(2) }}>
          <TextField label={t.admin.rejectReason} value={reason} onChangeText={setReason} />
          <Row style={{ gap: space(2) }}>
            <Button size="sm" title={t.admin.confirmReject} variant="danger" loading={busy} onPress={() => review(false)} />
            <Button size="sm" title={t.common.cancel} variant="ghost" onPress={() => setRejecting(false)} />
          </Row>
        </View>
      ) : null}
    </Card>
  );
}
