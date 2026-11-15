import { useState } from 'react';
import { Switch } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import { Button, ErrorState, Row, Screen, Skeleton, Text, TextField } from '../../../components/ui';
import { AdminOnly, CategoryPicker, PhotoField } from '../../../components/admin';
import { api, StoreCategory, StoreProduct } from '../../../data';
import { useQuery } from '../../../data/useQuery';
import { radius, useColors } from '../../../theme';
import { filsToInput, parseJD } from '../../../lib/money';
import { t } from '../../../i18n';

const CATS: readonly StoreCategory[] = ['phone', 'audio', 'watch', 'console', 'laptop', 'tablet', 'home', 'beauty', 'other'];

export default function AdminStoreProduct() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <AdminOnly>{id === 'new' ? <Form /> : <Load id={id} />}</AdminOnly>;
}

function Load({ id }: { id: string }) {
  const q = useQuery(() => api.adminListStoreProducts().then((ps) => ps.find((p) => p.id === id) ?? null), [id]);
  if (q.error) return <Screen edges={[]}><ErrorState onRetry={q.reload} /></Screen>;
  if (q.data === undefined) return <Screen edges={[]}><Skeleton height={200} r={radius.lg} /></Screen>;
  if (q.data === null) return <Screen edges={[]}><Text>{t.common.loadError}</Text></Screen>;
  return <Form product={q.data} />;
}

function Form({ product }: { product?: StoreProduct }) {
  const c = useColors();
  const [f, setF] = useState({
    titleAr: product?.titleAr ?? '', descriptionAr: product?.descriptionAr ?? '', category: (product?.category ?? 'other') as StoreCategory,
    price: product ? filsToInput(product.priceFils) : '', stock: product ? String(product.stock) : '', active: product?.active ?? true,
    image: product?.images[0] ?? null as string | null,
  });
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);
  const price = parseJD(f.price), stock = /^\d{1,6}$/.test(f.stock) ? Number(f.stock) : -1;

  async function save() {
    if (!f.titleAr.trim() || !price || stock < 0) { setMsg({ text: t.admin.invalid, ok: false }); return; }
    setBusy(true); setMsg(null);
    try {
      await api.adminSaveStoreProduct({ id: product?.id, titleAr: f.titleAr, descriptionAr: f.descriptionAr, category: f.category,
        priceFils: price, stock, active: f.active, images: f.image ? [f.image] : [] });
      setMsg({ text: t.admin.saved, ok: true });
      if (!product) router.back();
    } catch { setMsg({ text: t.common.loadError, ok: false }); } finally { setBusy(false); }
  }

  return (
    <Screen edges={['bottom']}>
      <TextField label={t.admin.fields.title} value={f.titleAr} onChangeText={(v) => setF({ ...f, titleAr: v })} />
      <TextField label={t.admin.fields.description} value={f.descriptionAr} onChangeText={(v) => setF({ ...f, descriptionAr: v })} />
      <CategoryPicker value={f.category} options={CATS} onChange={(v) => setF({ ...f, category: v })} />
      <TextField label={t.admin.fields.price} value={f.price} onChangeText={(v) => setF({ ...f, price: v })} keyboardType="decimal-pad" ltr />
      <TextField label={t.admin.fields.stock} value={f.stock} onChangeText={(v) => setF({ ...f, stock: v.replace(/\D/g, '') })} keyboardType="number-pad" ltr />
      <Row style={{ justifyContent: 'space-between' }}>
        <Text weight={600}>{t.admin.fields.active}</Text>
        <Switch value={f.active} onValueChange={(v) => setF({ ...f, active: v })} accessibilityLabel={t.admin.fields.active}
          trackColor={{ true: c.accent, false: c['progress-track'] }} thumbColor={c['surface-raised']} />
      </Row>
      <PhotoField value={f.image} onChange={(v) => setF({ ...f, image: v })} />
      {msg ? <Text tone={msg.ok ? 'success' : 'danger'} accessibilityLiveRegion="polite">{msg.text}</Text> : null}
      <Button full size="lg" title={t.admin.save} loading={busy} onPress={save} />
    </Screen>
  );
}
