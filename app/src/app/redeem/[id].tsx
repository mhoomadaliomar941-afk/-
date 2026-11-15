import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { useLocalSearchParams } from 'expo-router';
import { QrCode } from 'lucide-react-native';
import { Button, Card, ErrorState, Screen, Skeleton, StatusChip, Text, TextField } from '../../components/ui';
import { api, ApiError, Redemption } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space, useColors } from '../../theme';
import { formatJD, JD } from '../../lib/money';
import { t } from '../../i18n';

// S13: pay at a partner store with shopping balance. A code the cashier scans.
export default function Redeem() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useColors();
  const store = useQuery(() => api.getStore(id), [id]);
  const w = useQuery(() => api.getWallet());
  const [amount, setAmount] = useState('');
  const [r, setR] = useState<Redemption | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [nowMs, setNow] = useState(() => Date.now());
  useEffect(() => { const i = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(i); }, []);

  const left = r ? Math.max(0, new Date(r.expiresAt).getTime() - nowMs) : 0;
  useEffect(() => { if (r && r.status === 'pending' && left === 0) api.getRedemption(r.id).then(setR); }, [left, r]);

  if (store.error) return <Screen edges={[]}><ErrorState onRetry={store.reload} /></Screen>;
  if (!store.data) return <Screen edges={[]}><Skeleton height={120} r={radius.lg} /></Screen>;
  const fils = /^\d{1,3}(\.\d{0,3})?$/.test(amount) ? Math.round(Number(amount) * JD) : 0;

  async function create() {
    setBusy(true); setError(null);
    try { setR(await api.createRedemption(id, fils)); }
    catch (e) { setError(e instanceof ApiError && e.code === 'insufficient' ? t.redeem.low : t.common.loadError); }
    finally { setBusy(false); }
  }
  const mm = `${Math.floor(left / 60000)}:${String(Math.floor((left % 60000) / 1000)).padStart(2, '0')}`;

  return (
    <Screen edges={['bottom']}>
      <Text variant="xl" accessibilityRole="header">{store.data.nameAr}</Text>
      <Text tone="text-muted">{t.wallet.shopping}: {w.data ? formatJD(w.data.shoppingFils) : '…'}</Text>
      {!r ? (
        <>
          <TextField label={t.redeem.amount} value={amount} onChangeText={(s) => setAmount(s.replace(/[^\d.]/g, ''))} keyboardType="decimal-pad" prefix="د.ا" placeholder="0.000" error={error} />
          <Button full size="lg" title={t.redeem.create} loading={busy} disabled={fils <= 0} onPress={create} />
        </>
      ) : (
        <Card style={{ alignItems: 'center', gap: space(3), paddingVertical: space(6) }}>
          {r.status === 'pending' ? <StatusChip tone="warning" label={t.redeem.expires(`⁦${mm}⁩`)} />
            : <StatusChip tone={r.status === 'confirmed' ? 'success' : 'neutral'} label={r.status === 'confirmed' ? t.redeem.confirmed : t.redeem.expired} />}
          <View style={{ opacity: r.status === 'pending' ? 1 : 0.3, width: 180, height: 180, borderRadius: radius.lg, backgroundColor: c.surface, alignItems: 'center', justifyContent: 'center' }}>
            <QrCode color={c.text} size={140} strokeWidth={1} />
          </View>
          <Text variant="2xl" selectable style={{ letterSpacing: 2, writingDirection: 'ltr' }}>{r.code.slice(0, 5)} {r.code.slice(5)}</Text>
          <Text tone="text-muted">{t.redeem.show} · {formatJD(r.amountFils)}</Text>
          {r.status !== 'pending' ? <Button title={t.redeem.again} variant="secondary" onPress={() => { setR(null); setAmount(''); }} /> : null}
        </Card>
      )}
    </Screen>
  );
}
