import { useState } from 'react';
import { View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import { Button, Card, ErrorState, Screen, Skeleton, StatusChip, Text, TextField } from '../../../components/ui';
import { AdminOnly, CategoryPicker, confirmAsk, PhotoField } from '../../../components/admin';
import { api, Category, Product } from '../../../data';
import { useQuery } from '../../../data/useQuery';
import { radius, space } from '../../../theme';
import { filsToInput, formatJD, parseJD } from '../../../lib/money';
import { t } from '../../../i18n';

const CATS: readonly Category[] = ['phone', 'console', 'watch', 'audio', 'laptop', 'tablet'];

export default function AdminProduct() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <AdminOnly>{id === 'new' ? <Form /> : <Load id={id} />}</AdminOnly>;
}

function Load({ id }: { id: string }) {
  const q = useQuery(() => api.adminListProducts().then((ps) => ps.find((p) => p.id === id) ?? null), [id]);
  if (q.error) return <Screen edges={[]}><ErrorState onRetry={q.reload} /></Screen>;
  if (q.data === undefined) return <Screen edges={[]}><Skeleton height={200} r={radius.lg} /></Screen>;
  if (q.data === null) return <Screen edges={[]}><Text>{t.common.loadError}</Text></Screen>;
  return <Form product={q.data} />;
}

function Form({ product }: { product?: Product }) {
  const locked = !!product && product.status !== 'draft';
  const [f, setF] = useState({
    titleAr: product?.titleAr ?? '', descriptionAr: product?.descriptionAr ?? '', category: (product?.category ?? 'phone') as Category,
    retail: product ? filsToInput(product.retailPriceFils) : '', spot: product ? filsToInput(product.spotPriceFils) : '1',
    spots: product ? String(product.totalSpots) : '', maxPer: product?.maxSpotsPerUser ? String(product.maxSpotsPerUser) : '',
    image: product?.images[0] ?? null as string | null,
  });
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);
  const retail = parseJD(f.retail), spot = parseJD(f.spot), spots = /^\d{1,6}$/.test(f.spots) ? Number(f.spots) : 0;
  const maxPer = f.maxPer.trim() === '' ? null : /^\d{1,4}$/.test(f.maxPer) ? Number(f.maxPer) : NaN;
  const valid = f.titleAr.trim() && retail && spot && spot >= 1000 && spots >= 2 && !Number.isNaN(maxPer);

  async function run(action: () => Promise<unknown>, ok: string, back = false) {
    setBusy(true); setMsg(null);
    try { await action(); setMsg({ text: ok, ok: true }); if (back) router.back(); }
    catch { setMsg({ text: t.common.loadError, ok: false }); }
    finally { setBusy(false); }
  }
  const save = () => {
    if (!valid) { setMsg({ text: t.admin.invalid, ok: false }); return; }
    run(() => api.adminSaveProduct({ id: product?.id, titleAr: f.titleAr, descriptionAr: f.descriptionAr, category: f.category,
      retailPriceFils: retail!, spotPriceFils: spot!, totalSpots: spots, maxSpotsPerUser: maxPer, images: f.image ? [f.image] : [] }), t.admin.saved, !product);
  };

  return (
    <Screen edges={['bottom']}>
      {product ? <StatusChip label={product.status === 'draft' ? t.admin.statusDraft : t.product[product.status === 'open' ? 'open' : product.status === 'cancelled' ? 'cancelled' : 'assigned']} /> : null}
      {locked ? <Text tone="text-muted" variant="sm">{t.admin.lockedNote}</Text> : null}
      <View pointerEvents={locked ? 'none' : 'auto'} style={{ gap: space(4), opacity: locked ? 0.6 : 1 }}>
        <TextField label={t.admin.fields.title} value={f.titleAr} onChangeText={(v) => setF({ ...f, titleAr: v })} />
        <TextField label={t.admin.fields.description} value={f.descriptionAr} onChangeText={(v) => setF({ ...f, descriptionAr: v })} />
        <CategoryPicker value={f.category} options={CATS} onChange={(v) => setF({ ...f, category: v })} />
        <TextField label={t.admin.fields.retail} value={f.retail} onChangeText={(v) => setF({ ...f, retail: v })} keyboardType="decimal-pad" ltr />
        <TextField label={t.admin.fields.spotPrice} value={f.spot} onChangeText={(v) => setF({ ...f, spot: v })} keyboardType="decimal-pad" ltr />
        <TextField label={t.admin.fields.spots} value={f.spots} onChangeText={(v) => setF({ ...f, spots: v.replace(/\D/g, '') })} keyboardType="number-pad" ltr />
        <TextField label={t.admin.fields.maxPer} value={f.maxPer} onChangeText={(v) => setF({ ...f, maxPer: v.replace(/\D/g, '') })} keyboardType="number-pad" ltr />
        <PhotoField value={f.image} onChange={(v) => setF({ ...f, image: v })} />
      </View>
      {retail && spot && spots ? (
        <Card><Text variant="sm">{t.admin.pool(formatJD(spot * spots), formatJD(retail))}</Text></Card>
      ) : null}
      {msg ? <Text tone={msg.ok ? 'success' : 'danger'} accessibilityLiveRegion="polite">{msg.text}</Text> : null}
      {!locked ? <Button full size="lg" title={t.admin.save} loading={busy} onPress={save} /> : null}
      {product?.status === 'draft' ? (
        <Button full title={t.admin.open} variant="secondary" disabled={busy}
          onPress={() => confirmAsk(t.admin.openConfirm, () => run(() => api.adminOpenProduct(product.id), t.admin.saved, true))} />
      ) : null}
      {product && (product.status === 'draft' || product.status === 'open') ? (
        <Button full title={t.admin.cancel} variant="danger" disabled={busy}
          onPress={() => confirmAsk(t.admin.cancelConfirm, () => run(() => api.adminCancelProduct(product.id), t.admin.saved, true))} />
      ) : null}
    </Screen>
  );
}
