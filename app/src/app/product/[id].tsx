import { useState } from 'react';
import { View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import { ShieldCheck } from 'lucide-react-native';
import { Sheet, StickyBar } from '../../components/sheet';
import {
  Button, Card, Divider, ErrorState, ProductArt, productChip, QuantityStepper, Row, Screen, SectionTitle, Skeleton,
  SpotsProgress, StatusChip, Text,
} from '../../components/ui';
import { api, ApiError, Product } from '../../data';
import { useQuery } from '../../data/useQuery';
import { newKey } from '../../lib/session';
import { radius, space, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

const num = (n: number) => `⁦${n}⁩`;

// S04 product detail + S05 confirm sheet.
export default function ProductScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useColors();
  const q = useQuery(() => api.getProduct(id), [id]);
  const mine = useQuery(() => api.mySpotsIn(id), [id]);
  const [qty, setQty] = useState(1);
  const [sheet, setSheet] = useState(false);
  const [fair, setFair] = useState(false);

  if (q.error) return <Screen edges={[]}><ErrorState onRetry={q.reload} /></Screen>;
  if (!q.data) return <Screen edges={[]}><Skeleton height={300} r={radius.md} /><Skeleton width="70%" height={22} /><Skeleton width="40%" /></Screen>;

  const p = q.data;
  const left = p.totalSpots - p.filledSpots;
  const myCap = p.maxSpotsPerUser ? p.maxSpotsPerUser - (mine.data ?? 0) : left;
  const max = Math.max(0, Math.min(left, myCap));
  const open = p.status === 'open' && max > 0;
  const chip = productChip(p.status, p.filledSpots, p.totalSpots);
  const n = Math.min(qty, Math.max(1, max));

  return (
    <View style={{ flex: 1 }}>
      <Screen edges={[]}>
        <ProductArt category={p.category} size="hero" />
        <StatusChip {...chip} />
        <Text variant="xl" accessibilityRole="header">{p.titleAr}</Text>
        <Row style={{ justifyContent: 'space-between' }}>
          <View>
            <Text variant="sm" tone="text-muted">{t.product.spotPrice}</Text>
            <Text variant="xl" tone="accent">{formatJD(p.spotPriceFils)}</Text>
          </View>
          <View style={{ alignItems: 'flex-end' }}>
            <Text variant="sm" tone="text-muted">{t.product.retail}</Text>
            <Text weight={600}>{formatJD(p.retailPriceFils)}</Text>
          </View>
        </Row>
        <SpotsProgress filled={p.filledSpots} total={p.totalSpots} size="large" />
        <Text variant="sm" tone="text-muted">
          {t.product.spotsTaken(num(p.filledSpots), num(p.totalSpots))}
          {mine.data ? ` · لك ${num(mine.data)}` : ''}
        </Text>

        {open ? (
          <Card>
            <Text weight={600}>{t.product.howMany}</Text>
            <Row style={{ justifyContent: 'space-between' }}>
              <QuantityStepper value={n} max={max} onChange={setQty} />
              <View style={{ alignItems: 'flex-end' }}>
                <Text variant="sm" tone="text-muted">{t.product.total}</Text>
                <Text variant="lg">{formatJD(p.spotPriceFils * n)}</Text>
              </View>
            </Row>
            {p.maxSpotsPerUser ? <Text variant="xs" tone="text-muted">{t.product.maxPer(p.maxSpotsPerUser)}</Text> : null}
          </Card>
        ) : null}

        <Text tone="text-muted">{p.descriptionAr}</Text>
        <SectionTitle>{t.product.details}</SectionTitle>
        <Card style={{ gap: 0, paddingVertical: space(1) }}>
          {Object.entries(p.specs).map(([k, v], i) => (
            <View key={k}>
              {i > 0 ? <Divider /> : null}
              <Row style={{ justifyContent: 'space-between', minHeight: 44 }}>
                <Text tone="text-muted">{k}</Text>
                <Text weight={500}>{v}</Text>
              </Row>
            </View>
          ))}
        </Card>
        <Button title={t.product.fairness} variant="ghost" icon={<ShieldCheck color={c.accent} size={18} />} onPress={() => setFair(true)} />
        <View style={{ height: 80 }} />
      </Screen>

      <StickyBar>
        {p.status === 'open'
          ? <Button full size="lg" title={max > 0 ? `${t.product.join} · ${formatJD(p.spotPriceFils * n)}` : t.product.joinFull} disabled={max === 0} onPress={() => setSheet(true)} />
          : <Button full size="lg" variant="secondary" title={t.mine.waiting} onPress={() => router.push(`/result/${p.id}`)} />}
      </StickyBar>

      {/* mounted only while open: a fresh idempotency key and error state per checkout */}
      {sheet ? <ConfirmSheet product={p} spots={n} onClose={() => setSheet(false)} /> : null}
      <Sheet visible={fair} onClose={() => setFair(false)} title={t.product.fairness}>
        <Text tone="text-muted">{t.product.fairnessBody}</Text>
        <Text variant="xs" tone="text-muted">{t.result.seedHash}</Text>
        <Text variant="xs" style={{ fontFamily: undefined, writingDirection: 'ltr', textAlign: 'left' }} selectable>{p.seedHash}</Text>
      </Sheet>
    </View>
  );
}

function ConfirmSheet({ product: p, spots, onClose }: { product: Product; spots: number; onClose: () => void }) {
  const w = useQuery(() => api.getWallet());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [key] = useState(newKey);

  const cost = p.spotPriceFils * spots;
  const bal = w.data?.cashFils ?? 0;
  const low = !!w.data && bal < cost;

  async function pay() {
    setBusy(true); setError(null);
    try {
      await api.buySpots(p.id, spots, key); // same key on retry: never charged twice
      onClose();
      router.push(p.filledSpots + spots >= p.totalSpots ? `/result/${p.id}` : '/(tabs)/mine');
    } catch (e) {
      const code = e instanceof ApiError ? e.code : null;
      setError(code === 'sold_out' || code === 'not_open' ? t.confirm.soldOut : code === 'insufficient' ? t.confirm.low : code === 'limit' ? t.product.maxPer(p.maxSpotsPerUser ?? 0) : t.common.loadError);
    } finally { setBusy(false); }
  }

  const line = (label: string, value: string, tone: 'text' | 'danger' = 'text') => (
    <Row style={{ justifyContent: 'space-between' }}>
      <Text tone="text-muted">{label}</Text>
      <Text weight={600} tone={tone}>{value}</Text>
    </Row>
  );
  return (
    <Sheet visible onClose={onClose} title={t.confirm.title}>
      <Text weight={600}>{p.titleAr}</Text>
      {line(t.confirm.spots, `⁦${spots}⁩ × ${formatJD(p.spotPriceFils)}`)}
      {line(t.product.total, formatJD(cost))}
      <Divider />
      {line(t.confirm.balance, w.data ? formatJD(bal) : '…', low ? 'danger' : 'text')}
      {w.data && !low ? line(t.confirm.after, formatJD(bal - cost)) : null}
      <Text variant="sm" tone="text-muted">{t.confirm.ifNot}</Text>
      {low ? <Text tone="danger" variant="sm">{t.confirm.low}</Text> : null}
      {error ? <Text tone="danger" variant="sm" accessibilityLiveRegion="assertive">{error}</Text> : null}
      {low
        ? <Button full size="lg" title={t.confirm.topUp} onPress={() => { onClose(); router.push('/topup'); }} />
        : <Button full size="lg" title={t.confirm.pay} loading={busy} disabled={!w.data} onPress={pay} />}
    </Sheet>
  );
}
