import { useState } from 'react';
import { Switch } from 'react-native';
import { Button, ErrorState, Row, Screen, Skeleton, Text, TextField } from '../../components/ui';
import { AdminOnly } from '../../components/admin';
import { api, AppSettings } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, useColors } from '../../theme';
import { t } from '../../i18n';

export default function AdminSettings() {
  return <AdminOnly><Form /></AdminOnly>;
}

function Form() {
  const q = useQuery(() => api.getSettings());
  if (q.error) return <Screen edges={['bottom']}><ErrorState onRetry={q.reload} /></Screen>;
  if (!q.data) return <Screen edges={['bottom']}><Skeleton height={200} r={radius.lg} /></Screen>;
  return <Editor initial={q.data} />;
}

function Editor({ initial }: { initial: AppSettings }) {
  const c = useColors();
  const [f, setF] = useState<AppSettings>(initial);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);
  const F = t.admin.settingsFields;
  async function save() {
    setBusy(true); setMsg(null);
    try { await api.adminSaveSettings(f); setMsg({ text: t.admin.saved, ok: true }); } catch { setMsg({ text: t.common.loadError, ok: false }); } finally { setBusy(false); }
  }
  return (
    <Screen edges={['bottom']}>
      <TextField label={F.cliqAlias} value={f.cliqAlias} onChangeText={(v) => setF({ ...f, cliqAlias: v })} ltr />
      <Text variant="sm" tone="text-muted">{t.admin.cliqOff}</Text>
      <TextField label={F.cliqHolder} value={f.cliqHolder} onChangeText={(v) => setF({ ...f, cliqHolder: v })} />
      <TextField label={F.cliqHours} value={f.cliqHours} onChangeText={(v) => setF({ ...f, cliqHours: v })} />
      <Row style={{ justifyContent: 'space-between' }}>
        <Text weight={600} style={{ flex: 1 }}>{F.cardEnabled}</Text>
        <Switch value={f.cardEnabled} onValueChange={(v) => setF({ ...f, cardEnabled: v })} accessibilityLabel={F.cardEnabled}
          trackColor={{ true: c.accent, false: c['progress-track'] }} thumbColor={c['surface-raised']} />
      </Row>
      <Text variant="sm" tone="text-muted">{t.admin.cardNote}</Text>
      {msg ? <Text tone={msg.ok ? 'success' : 'danger'} accessibilityLiveRegion="polite">{msg.text}</Text> : null}
      <Button full size="lg" title={t.admin.save} loading={busy} onPress={save} />
    </Screen>
  );
}
