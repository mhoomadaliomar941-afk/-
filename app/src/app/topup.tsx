import { useState } from 'react';
import { Platform, Pressable, View } from 'react-native';
import { router } from 'expo-router';
import { Check, CreditCard, Landmark, Smartphone } from 'lucide-react-native';
import { Button, ErrorState, Row, Screen, SectionTitle, Skeleton, Text, TextField } from '../components/ui';
import { api, ApiError, PaymentMethod } from '../data';
import { useQuery } from '../data/useQuery';
import { radius, space, useColors } from '../theme';
import { formatJD, JD } from '../lib/money';
import { t } from '../i18n';

const PRESETS = [1, 5, 10, 20].map((n) => n * JD);
type Method = PaymentMethod | 'cliq';

// S06: choose an amount and a method. CliQ is confirmed by the owner; cards go through the gateway.
export default function Topup() {
  const c = useColors();
  const settings = useQuery(() => api.getSettings());
  const [amount, setAmount] = useState<number | null>(5 * JD);
  const [other, setOther] = useState('');
  const [picked, setMethod] = useState<Method | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const otherFils = /^\d{1,3}$/.test(other) && Number(other) > 0 ? Number(other) * JD : null;
  const value = amount ?? otherFils;

  // what this phone can offer: Google Pay on Android (and web), Apple Pay on iPhone
  const s = settings.data;
  const methods: { m: Method; Icon: typeof CreditCard; label: string }[] = [];
  if (s?.cliqAlias) methods.push({ m: 'cliq', Icon: Landmark, label: t.topup.cliq });
  if (s?.cardEnabled) {
    methods.push({ m: 'card', Icon: CreditCard, label: t.topup.card });
    if (Platform.OS === 'ios') methods.push({ m: 'apple_pay', Icon: Smartphone, label: t.topup.applePay });
    else methods.push({ m: 'google_pay', Icon: Smartphone, label: t.topup.googlePay });
  }
  // the first available method is selected until the member picks another
  const method: Method | null = picked && methods.some((x) => x.m === picked) ? picked : methods[0]?.m ?? null;

  async function pay() {
    if (!value || !method) return;
    setBusy(true); setError(null);
    try {
      if (method === 'cliq') { const r = await api.startCliqTopup(value); router.replace(`/cliq/${r.id}`); }
      else { await api.startTopup(value, method); router.back(); }
    } catch (e) {
      const code = e instanceof ApiError ? e.code : null;
      setError(code === 'too_many_pending' ? t.cliq.tooMany : code === 'cliq_unavailable' ? t.cliq.unavailable : t.common.loadError);
    } finally { setBusy(false); }
  }

  const chip = (fils: number) => {
    const on = amount === fils;
    return (
      <Pressable key={fils} role="radio" aria-checked={on} onPress={() => { setAmount(fils); setOther(''); }}
        style={{ flex: 1, height: 48, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center',
          backgroundColor: on ? c['accent-soft'] : c.surface, borderWidth: on ? 2 : 1, borderColor: on ? c.accent : c.border }}>
        <Text weight={600} tone={on ? 'accent-strong' : 'text'}>{formatJD(fils)}</Text>
      </Pressable>
    );
  };

  if (settings.error) return <Screen edges={['bottom']}><ErrorState onRetry={settings.reload} /></Screen>;
  return (
    <Screen edges={['bottom']}>
      <SectionTitle>{t.topup.amount}</SectionTitle>
      <View role="radiogroup" aria-label={t.topup.amount} style={{ gap: space(2) }}>
        <Row>{PRESETS.slice(0, 2).map(chip)}</Row>
        <Row>{PRESETS.slice(2).map(chip)}</Row>
      </View>
      <TextField label={t.topup.other} value={other} onChangeText={(v) => { setOther(v.replace(/\D/g, '').slice(0, 3)); setAmount(null); }}
        keyboardType="number-pad" prefix="د.ا" placeholder="0" />
      <SectionTitle>{t.topup.method}</SectionTitle>
      {!s ? <Skeleton height={56} r={radius.md} />
        : methods.length === 0 ? <Text tone="text-muted">{t.topup.noMethods}</Text>
        : (
          <View role="radiogroup" aria-label={t.topup.method} style={{ gap: space(2) }}>
            {methods.map(({ m, Icon, label }) => {
              const on = method === m;
              return (
                <Pressable key={m} role="radio" aria-checked={on} onPress={() => setMethod(m)}
                  style={{ flexDirection: 'row', alignItems: 'center', gap: space(3), minHeight: 56, paddingHorizontal: space(4), borderRadius: radius.md,
                    borderWidth: on ? 2 : 1, borderColor: on ? c.accent : c.border }}>
                  <Icon color={c['text-muted']} size={20} />
                  <Text style={{ flex: 1 }}>{label}</Text>
                  {on ? <Check color={c.accent} size={20} /> : null}
                </Pressable>
              );
            })}
          </View>
        )}
      <Text variant="sm" tone="text-muted">{method === 'cliq' && s ? t.cliq.hours(s.cliqHours) : t.topup.note}</Text>
      {error ? <Text tone="danger" accessibilityLiveRegion="assertive">{error}</Text> : null}
      <Button full size="lg" title={value ? t.topup.pay(formatJD(value)) : t.topup.pay('')} disabled={!value || !method} loading={busy} onPress={pay} />
    </Screen>
  );
}
