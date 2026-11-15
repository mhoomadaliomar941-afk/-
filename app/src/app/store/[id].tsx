import { useState } from 'react';
import { View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import { Button, Divider, ErrorState, ProductArt, QuantityStepper, Row, Screen, Skeleton, StatusChip, Text } from '../../components/ui';
import { Sheet, StickyBar } from '../../components/sheet';
import { AddressPicker } from '../../components/address';
import { api, ApiError, StoreProduct } from '../../data';
import { useQuery } from '../../data/useQuery';
import { newKey } from '../../lib/session';
import { radius } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

export default function StoreProductScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const q = useQuery(() => api.getStoreProduct(id), [id]);
  const [qty, setQty] = useState(1);
  const [sheet, setSheet] = useState(false);
  if (q.error) return <Screen edges={[]}><ErrorState onRetry={q.reload} /></Screen>;
  if (!q.data) return <Screen edges={[]}><Skeleton height={300} r={radius.md} /><Skeleton width="60%" /></Screen>;
  const p = q.data;
  const max = Math.min(p.stock, 20);
  const n = Math.min(qty, Math.max(1, max));
  return (
    <View style={{ flex: 1 }}>
      <Screen edges={[]}>
        <ProductArt category={p.category} image={p.images[0]} size="hero" />
        <Text variant="xl" accessibilityRole="header">{p.titleAr}</Text>
        <Row style={{ justifyContent: 'space-between' }}>
          <Text variant="xl" tone="accent">{formatJD(p.priceFils)}</Text>
          {p.stock === 0 ? <StatusChip label={t.store.outOfStock} tone="danger" /> : p.stock <= 3 ? <StatusChip label={t.store.left(p.stock)} tone="warning" /> : null}
        </Row>
        {p.descriptionAr ? <Text tone="text-muted">{p.descriptionAr}</Text> : null}
        {max > 0 ? (
          <Row style={{ justifyContent: 'space-between' }}>
            <Text weight={600}>{t.store.qty}</Text>
            <QuantityStepper value={n} max={max} onChange={setQty} />
          </Row>
        ) : null}
        <View style={{ height: 80 }} />
      </Screen>
      <StickyBar>
        <Button full size="lg" disabled={max === 0} title={max === 0 ? t.store.outOfStock : `${t.store.buy} · ${formatJD(p.priceFils * n)}`} onPress={() => setSheet(true)} />
      </StickyBar>
      {sheet ? <CheckoutSheet product={p} qty={n} onClose={() => setSheet(false)} /> : null}
    </View>
  );
}

function CheckoutSheet({ product: p, qty, onClose }: { product: StoreProduct; qty: number; onClose: () => void }) {
  const w = useQuery(() => api.getWallet());
  const [address, setAddress] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [key] = useState(newKey); // one key per checkout: a retry never charges twice
  const total = p.priceFils * qty;
  const fromShopping = Math.min(w.data?.shoppingFils ?? 0, total);
  const fromCash = total - fromShopping;
  const low = !!w.data && fromCash > w.data.cashFils;

  async function pay() {
    if (!address) return;
    setBusy(true); setError(null);
    try { await api.checkoutStore([{ productId: p.id, qty }], address, key); onClose(); router.push('/orders'); }
    catch (e) {
      const code = e instanceof ApiError ? e.code : null;
      setError(code === 'insufficient' ? t.store.low : code === 'out_of_stock' ? t.store.stockChanged : t.common.loadError);
    } finally { setBusy(false); }
  }
  const line = (label: string, value: string) => (
    <Row style={{ justifyContent: 'space-between' }}><Text tone="text-muted">{label}</Text><Text weight={600}>{value}</Text></Row>
  );
  return (
    <Sheet visible onClose={onClose} title={t.store.checkout}>
      <Text weight={600}>{p.titleAr} × {'⁦'}{qty}{'⁩'}</Text>
      {line(t.store.total, formatJD(total))}
      <Divider />
      {w.data ? <>{line(t.store.fromShopping, formatJD(fromShopping))}{line(t.store.fromCash, formatJD(fromCash))}</> : null}
      <Text variant="sm" tone="text-muted">{t.store.note}</Text>
      {low ? <Text tone="danger" variant="sm">{t.store.low}</Text> : null}
      <AddressPicker value={address} onChange={setAddress} />
      {error ? <Text tone="danger" variant="sm" accessibilityLiveRegion="assertive">{error}</Text> : null}
      {low ? <Button full size="lg" title={t.confirm.topUp} onPress={() => { onClose(); router.push('/topup'); }} />
        : <Button full size="lg" title={t.store.confirm} loading={busy} disabled={!address || !w.data} onPress={pay} />}
    </Sheet>
  );
}
