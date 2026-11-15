import { useState } from 'react';
import { Pressable, View } from 'react-native';
import { Check, MapPin } from 'lucide-react-native';
import { Button, Text, TextField } from './ui';
import { api, ApiError } from '../data';
import { useQuery } from '../data/useQuery';
import { radius, space, useColors } from '../theme';
import { t } from '../i18n';

/** Pick a saved address or add one. Calls onChange with the chosen address id (or null). */
export function AddressPicker({ value, onChange }: { value: string | null; onChange: (id: string | null) => void }) {
  const c = useColors();
  const q = useQuery(() => api.listAddresses());
  const [adding, setAdding] = useState(false);
  const list = q.data ?? [];
  const showForm = adding || (q.data !== undefined && list.length === 0);
  return (
    <View style={{ gap: space(2) }}>
      <Text weight={600}>{t.store.address}</Text>
      <View role="radiogroup" aria-label={t.store.address} style={{ gap: space(2) }}>
        {list.map((a) => {
          const on = value === a.id;
          return (
            <Pressable key={a.id} role="radio" aria-checked={on} onPress={() => onChange(a.id)}
              style={{ flexDirection: 'row', alignItems: 'center', gap: space(3), minHeight: 52, paddingHorizontal: space(3), borderRadius: radius.md,
                borderWidth: on ? 2 : 1, borderColor: on ? c.accent : c.border }}>
              <MapPin color={c['text-muted']} size={18} />
              <Text style={{ flex: 1 }} numberOfLines={2}>{a.governorate}، {a.city}، {a.street}</Text>
              {on ? <Check color={c.accent} size={18} /> : null}
            </Pressable>
          );
        })}
      </View>
      {showForm ? <AddressForm onSaved={(id) => { setAdding(false); onChange(id); }} />
        : <Button title={t.store.newAddress} variant="ghost" size="sm" onPress={() => setAdding(true)} />}
    </View>
  );
}

function AddressForm({ onSaved }: { onSaved: (id: string) => void }) {
  const [f, setF] = useState({ governorate: '', city: '', area: '', street: '', phone: '' });
  const [tried, setTried] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const req = (k: keyof typeof f) => (tried && !f[k].trim() ? t.order.required : null);
  async function save() {
    setTried(true);
    if (!f.governorate.trim() || !f.city.trim() || !f.street.trim() || !f.phone.trim()) return;
    setBusy(true); setError(null);
    try { onSaved((await api.addAddress(f)).id); }
    catch (e) { setError(e instanceof ApiError && e.code === 'invalid' ? 'تأكد من رقم الهاتف وحاول مرة ثانية' : t.common.loadError); }
    finally { setBusy(false); }
  }
  return (
    <View style={{ gap: space(3) }}>
      <TextField label={t.order.governorate} value={f.governorate} onChangeText={(v) => setF({ ...f, governorate: v })} error={req('governorate')} />
      <TextField label={t.order.city} value={f.city} onChangeText={(v) => setF({ ...f, city: v })} error={req('city')} />
      <TextField label={t.order.street} value={f.street} onChangeText={(v) => setF({ ...f, street: v })} error={req('street')} />
      <TextField label={t.order.phone} value={f.phone} onChangeText={(v) => setF({ ...f, phone: v })} error={req('phone')} keyboardType="phone-pad" ltr />
      {error ? <Text tone="danger" variant="sm">{error}</Text> : null}
      <Button title={t.store.saveAddress} variant="secondary" loading={busy} onPress={save} />
    </View>
  );
}
