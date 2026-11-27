import { useState } from 'react';
import { Pressable, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import * as Clipboard from 'expo-clipboard';
import { Check, Copy } from 'lucide-react-native';
import { Button, Card, ErrorState, Row, Screen, Skeleton, StatusChip, Text } from '../../components/ui';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

const TONE = { awaiting_transfer: 'warning', awaiting_review: 'accent', approved: 'success', rejected: 'danger' } as const;

// The member's CliQ transfer: where to send, the reference, then "I sent it". Updates live when the owner decides.
export default function CliqTopupScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const q = useQuery(() => api.getCliqTopup(id), [id]);
  const s = useQuery(() => api.getSettings());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  if (q.error) return <Screen edges={['bottom']}><ErrorState onRetry={q.reload} /></Screen>;
  if (!q.data || !s.data) return <Screen edges={['bottom']}><Skeleton height={220} r={radius.lg} /></Screen>;
  const r = q.data;

  async function sent() {
    setBusy(true); setError(null);
    try { await api.markCliqSent(r.id); } catch { setError(t.common.loadError); } finally { setBusy(false); }
  }
  return (
    <Screen edges={['bottom']}>
      <StatusChip label={t.cliq.status[r.status]} tone={TONE[r.status]} />
      <Card>
        <Field label={t.cliq.amount} value={formatJD(r.amountFils)} copy={String(r.amountFils / 1000)} big />
        <Field label={t.cliq.to} value={s.data.cliqAlias} copy={s.data.cliqAlias} />
        {s.data.cliqHolder ? <Field label={t.cliq.holder} value={s.data.cliqHolder} /> : null}
        <Field label={t.cliq.reference} value={r.reference} copy={r.reference} big />
      </Card>
      {r.status === 'awaiting_transfer' ? (
        <>
          <View style={{ gap: space(2) }}>
            {t.cliq.steps.map((step, i) => (
              <Row key={i} gap={3} style={{ alignItems: 'flex-start' }}>
                <Text weight={700} tone="accent">{'⁦'}{i + 1}{'⁩'}.</Text>
                <Text style={{ flex: 1 }}>{step}</Text>
              </Row>
            ))}
          </View>
          {error ? <Text tone="danger">{error}</Text> : null}
          <Button full size="lg" title={t.cliq.sent} loading={busy} onPress={sent} />
        </>
      ) : null}
      {r.status === 'awaiting_review' ? <Text tone="text-muted">{t.cliq.reviewNote} {t.cliq.hours(s.data.cliqHours)}</Text> : null}
      {r.status === 'rejected' && r.rejectReason ? <Text tone="danger">{r.rejectReason}</Text> : null}
      <Button title={t.cliq.wallet} variant="ghost" onPress={() => (router.canGoBack() ? router.back() : router.replace('/(tabs)/wallet'))} />
    </Screen>
  );
}

function Field({ label, value, copy, big }: { label: string; value: string; copy?: string; big?: boolean }) {
  const c = useColors();
  const [done, setDone] = useState(false);
  return (
    <Row style={{ justifyContent: 'space-between', minHeight: 48 }}>
      <View style={{ flex: 1 }}>
        <Text variant="sm" tone="text-muted">{label}</Text>
        <Text variant={big ? 'xl' : 'lg'} selectable style={{ writingDirection: 'ltr', textAlign: 'right' }}>{value}</Text>
      </View>
      {copy ? (
        <Pressable role="button" aria-label={`${t.cliq.copy} ${label}`} onPress={async () => { await Clipboard.setStringAsync(copy); setDone(true); setTimeout(() => setDone(false), 1500); }}
          style={{ width: 44, height: 44, borderRadius: radius.md, backgroundColor: c.surface, alignItems: 'center', justifyContent: 'center' }}>
          {done ? <Check color={c.success} size={20} /> : <Copy color={c['text-muted']} size={20} />}
        </Pressable>
      ) : null}
    </Row>
  );
}
