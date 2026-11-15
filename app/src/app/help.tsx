import { useState } from 'react';
import { Linking, Pressable, View } from 'react-native';
import { ChevronDown } from 'lucide-react-native';
import { Button, Card, Divider, Screen, Text } from '../components/ui';
import { space, useColors } from '../theme';
import { t } from '../i18n';

// S16: FAQ + contact. Terms come with /replica-launch.
export default function Help() {
  const c = useColors();
  const [open, setOpen] = useState<number | null>(0);
  return (
    <Screen edges={['bottom']}>
      <Card style={{ gap: 0, paddingVertical: space(1) }}>
        {t.help.faq.map((f, i) => (
          <View key={f.q}>
            {i > 0 ? <Divider /> : null}
            <Pressable role="button" aria-expanded={open === i} onPress={() => setOpen(open === i ? null : i)}
              style={{ flexDirection: 'row', alignItems: 'center', minHeight: 52, gap: space(2) }}>
              <Text weight={600} style={{ flex: 1 }}>{f.q}</Text>
              <ChevronDown color={c['text-muted']} size={18} style={{ transform: [{ rotate: open === i ? '180deg' : '0deg' }] }} />
            </Pressable>
            {open === i ? <Text tone="text-muted" style={{ paddingBottom: space(3) }}>{f.a}</Text> : null}
          </View>
        ))}
      </Card>
      <Button title={t.help.contact} variant="secondary" onPress={() => Linking.openURL('mailto:support@example.com')} />
    </Screen>
  );
}
