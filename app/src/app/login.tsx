import { ComponentRef, useRef, useState } from 'react';
import { Pressable, TextInput, View } from 'react-native';
import { router } from 'expo-router';
import { Button, Screen, Text, TextField } from '../components/ui';
import { radius, space, typeStyle, useColors } from '../theme';
import { api, ApiError } from '../data';
import { t } from '../i18n';

// S02: phone number, then a 6-digit code.
export default function Login() {
  const [step, setStep] = useState<'phone' | 'otp'>('phone');
  const [phone, setPhone] = useState('');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const e164 = '+962' + phone.replace(/\D/g, '').replace(/^0/, '');

  async function send() {
    setError(null); setBusy(true);
    try { await api.sendOtp(e164); setStep('otp'); } catch { setError(t.login.invalidPhone); } finally { setBusy(false); }
  }
  async function verify(c = code) {
    setError(null); setBusy(true);
    try { await api.verifyOtp(e164, c); router.replace('/(tabs)'); }
    catch (e) { setError(e instanceof ApiError && e.code === 'wrong_otp' ? t.login.wrongOtp : t.common.loadError); setCode(''); }
    finally { setBusy(false); }
  }

  return (
    <Screen edges={['top', 'bottom']}>
      <View style={{ gap: space(2), marginTop: space(8) }}>
        <Text variant="2xl" accessibilityRole="header">{step === 'phone' ? t.login.title : t.login.otpTitle}</Text>
        <Text tone="text-muted">
          {step === 'phone' ? t.login.subtitle : `${t.login.otpSent} ⁦${e164}⁩`}
        </Text>
      </View>
      {step === 'phone' ? (
        <>
          <TextField label={t.login.phone} value={phone} onChangeText={(s) => setPhone(s.replace(/[^\d ]/g, ''))} prefix="+962"
            keyboardType="phone-pad" placeholder="7X XXX XXXX" ltr autoFocus maxLength={12} error={error} testID="phone" />
          <Button full size="lg" title={t.login.send} onPress={send} loading={busy} disabled={phone.replace(/\D/g, '').length < 9} />
        </>
      ) : (
        <>
          <OtpInput value={code} error={!!error} onChange={(v) => { setCode(v); if (v.length === 6) verify(v); }} />
          {error ? <Text variant="sm" tone="danger" accessibilityLiveRegion="assertive">{error}</Text> : null}
          <Button full size="lg" title={t.login.verify} onPress={() => verify()} loading={busy} disabled={code.length !== 6} />
          <Button title={t.common.back} variant="ghost" onPress={() => { setStep('phone'); setError(null); setCode(''); }} />
        </>
      )}
      <Text variant="sm" tone="text-muted" style={{ textAlign: 'center' }}>{t.login.devHint}</Text>
    </Screen>
  );
}

// Six boxes over one hidden input: paste and SMS autofill work, digits stay LTR.
function OtpInput({ value, onChange, error }: { value: string; onChange: (v: string) => void; error: boolean }) {
  const c = useColors();
  const ref = useRef<ComponentRef<typeof TextInput>>(null);
  const [focused, setFocused] = useState(true);
  return (
    <Pressable onPress={() => ref.current?.focus()} accessibilityLabel={t.login.otpTitle}>
      <View style={{ flexDirection: 'row', direction: 'ltr', gap: space(2), justifyContent: 'center' }}>
        {Array.from({ length: 6 }, (_, i) => {
          const active = focused && i === Math.min(value.length, 5);
          return (
            <View key={i} style={{ width: 48, height: 56, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center',
              borderWidth: active || error ? 2 : 1, borderColor: error ? c.danger : active ? c.accent : c['border-input'] }}>
              <Text variant="xl">{value[i] ?? ''}</Text>
            </View>
          );
        })}
      </View>
      <TextInput ref={ref} value={value} onChangeText={(s) => onChange(s.replace(/\D/g, '').slice(0, 6))} autoFocus
        keyboardType="number-pad" textContentType="oneTimeCode" autoComplete="one-time-code" maxLength={6} testID="otp"
        onFocus={() => setFocused(true)} onBlur={() => setFocused(false)} accessibilityLabel={t.login.otpTitle}
        style={[typeStyle('base'), { position: 'absolute', opacity: 0, width: '100%', height: '100%' }]} />
    </Pressable>
  );
}
