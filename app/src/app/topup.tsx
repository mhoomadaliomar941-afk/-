import { useState } from 'react';
import { Pressable, View } from 'react-native';
import { router } from 'expo-router';
import { Check, CreditCard, Smartphone, Wallet } from 'lucide-react-native';
import { Button, Row, Screen, SectionTitle, Text, TextField } from '../components/ui';
import { api, PaymentMethod } from '../data';
import { radius, space, useColors } from '../theme';
import { formatJD, JD } from '../lib/money';
import { t } from '../i18n';

const PRESETS = [1, 5, 10, 20].map((n) => n * JD);

// S06: choose an amount and a method; the gateway confirms by webhook (faked here).
export default function Topup() {
  const c = useColors();
  const [amount, setAmount] = useState<number | null>(5 * JD);
  const [other, setOther] = useState('');
  const [method, setMethod] = useState<PaymentMethod>('card');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const otherFils = /^\d{1,3}$/.test(other) ? Number(other) * JD : null;
  const value = amount ?? otherFils;

  async function pay() {
    if (!value) return;
    setBusy(true); setError(null);
    try { await api.startTopup(value, method); router.back(); } catch { setError(t.common.loadError); } finally { setBusy(false); }
  }

  const chip = (fils: number) => {
    const on = amount === fils;
    return (
      <Pressable key={fils} accessibilityRole="radio" accessibilityState={{ checked: on }} onPress={() => { setAmount(fils); setOther(''); }}
        style={{ flex: 1, height: 48, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center',
          backgroundColor: on ? c['accent-soft'] : c.surface, borderWidth: on ? 2 : 1, borderColor: on ? c.accent : c.border }}>
        <Text weight={600} tone={on ? 'accent-strong' : 'text'}>{formatJD(fils)}</Text>
      </Pressable>
    );
  };
  const methodRow = (m: PaymentMethod | 'cliq', Icon: typeof CreditCard, label: string, unavailable = false) => {
    const on = method === m;
    return (
      <Pressable key={m} accessibilityRole="radio" accessibilityState={{ checked: on, disabled: unavailable }}
        onPress={unavailable ? undefined : () => setMethod(m as PaymentMethod)}
        style={{ flexDirection: 'row', alignItems: 'center', gap: space(3), height: 56, paddingHorizontal: space(4), borderRadius: radius.md,
          borderWidth: on ? 2 : 1, borderColor: on ? c.accent : c.border, opacity: unavailable ? 0.5 : 1 }}>
        <Icon color={c['text-muted']} size={20} />
        <Text style={{ flex: 1 }}>{label}</Text>
        {on ? <Check color={c.accent} size={20} /> : null}
      </Pressable>
    );
  };
  return (
    <Screen edges={['bottom']}>
      <SectionTitle>{t.topup.amount}</SectionTitle>
      <Row>{PRESETS.slice(0, 2).map(chip)}</Row>
      <Row>{PRESETS.slice(2).map(chip)}</Row>
      <TextField label={t.topup.other} value={other} onChangeText={(s) => { setOther(s.replace(/\D/g, '').slice(0, 3)); setAmount(null); }}
        keyboardType="number-pad" prefix="د.ا" placeholder="0" />
      <SectionTitle>{t.topup.method}</SectionTitle>
      <View style={{ gap: space(2) }}>
        {methodRow('card', CreditCard, t.topup.card)}
        {methodRow('apple_pay', Smartphone, t.topup.applePay)}
        {methodRow('cliq', Wallet, t.topup.cliq, true)}
      </View>
      {error ? <Text tone="danger">{error}</Text> : null}
      <Button full size="lg" title={value ? t.topup.pay(formatJD(value)) : t.topup.pay('')} disabled={!value} loading={busy} onPress={pay} />
    </Screen>
  );
}
