import { useState } from 'react';
import { View } from 'react-native';
import { useLocalSearchParams } from 'expo-router';
import { Check } from 'lucide-react-native';
import { Button, Card, ErrorState, Row, Screen, Skeleton, Text, TextField } from '../../components/ui';
import { api, OrderStatus } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, space, useColors } from '../../theme';
import { t } from '../../i18n';

const STEPS: Exclude<OrderStatus, 'failed'>[] = ['awaiting_address', 'pending', 'shipped', 'delivered'];

// S10: owner's delivery address, then order status. Route param is the product id.
export default function OrderScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useColors();
  const o = useQuery(() => api.getOrderForProduct(id), [id]);
  const [f, setF] = useState({ governorate: '', city: '', area: '', street: '', phone: '' });
  const [tried, setTried] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (o.error) return <Screen edges={[]}><ErrorState onRetry={o.reload} /></Screen>;
  if (o.data === undefined) return <Screen edges={[]}><Skeleton height={200} r={radius.lg} /></Screen>;
  if (o.data === null) return <Screen edges={[]}><Text tone="text-muted">لا يوجد طلب توصيل لهذا المنتج.</Text></Screen>;
  const order = o.data;
  const req = (k: keyof typeof f) => (tried && !f[k].trim() ? t.order.required : null);

  async function submit() {
    setTried(true);
    if (!f.governorate.trim() || !f.city.trim() || !f.street.trim() || !f.phone.trim()) return;
    setBusy(true); setError(null);
    try { const a = await api.addAddress(f); await api.setOrderAddress(order.id, a.id); }
    catch { setError('تأكد من رقم الهاتف وحاول مرة ثانية'); } finally { setBusy(false); }
  }
  const at = STEPS.indexOf(order.status as (typeof STEPS)[number]);

  return (
    <Screen edges={['bottom']}>
      <Card>
        {STEPS.map((s, i) => {
          const done = i < at || order.status === 'delivered';
          const current = i === at && order.status !== 'delivered';
          return (
            <Row key={s} gap={3}>
              <View style={{ width: 24, height: 24, borderRadius: radius.pill, alignItems: 'center', justifyContent: 'center',
                backgroundColor: done ? c.success : current ? c.accent : c.border }}>
                {done ? <Check color={c['on-accent']} size={14} /> : null}
              </View>
              <Text weight={current ? 600 : 400} tone={done || current ? 'text' : 'text-muted'}>{t.order.steps[s]}</Text>
            </Row>
          );
        })}
      </Card>
      {order.status === 'awaiting_address' ? (
        <View style={{ gap: space(4) }}>
          <TextField label={t.order.governorate} value={f.governorate} onChangeText={(v) => setF({ ...f, governorate: v })} error={req('governorate')} placeholder="عمّان" />
          <TextField label={t.order.city} value={f.city} onChangeText={(v) => setF({ ...f, city: v })} error={req('city')} />
          <TextField label={t.order.area} value={f.area} onChangeText={(v) => setF({ ...f, area: v })} />
          <TextField label={t.order.street} value={f.street} onChangeText={(v) => setF({ ...f, street: v })} error={req('street')} />
          <TextField label={t.order.phone} value={f.phone} onChangeText={(v) => setF({ ...f, phone: v })} error={req('phone')} keyboardType="phone-pad" ltr />
          {error ? <Text tone="danger">{error}</Text> : null}
          <Button full size="lg" title={t.order.submit} loading={busy} onPress={submit} />
        </View>
      ) : null}
    </Screen>
  );
}
