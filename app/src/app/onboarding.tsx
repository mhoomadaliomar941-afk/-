import { useState } from 'react';
import { View } from 'react-native';
import { router } from 'expo-router';
import { ShieldCheck, Ticket, Sparkles } from 'lucide-react-native';
import { Button, Row, Screen, Text } from '../components/ui';
import { radius, space, useColors } from '../theme';
import { onboarding } from '../lib/session';
import { t } from '../i18n';

const ICONS = [Sparkles, Ticket, ShieldCheck];

// S01: three slides, skip always reachable.
export default function Onboarding() {
  const c = useColors();
  const [i, setI] = useState(0);
  const finish = () => { onboarding.done(); router.replace('/login'); };
  const slide = t.onboarding.slides[i];
  const Icon = ICONS[i];
  const last = i === t.onboarding.slides.length - 1;
  return (
    <Screen scroll={false} edges={['top', 'bottom']}>
      <Row style={{ justifyContent: 'flex-end' }}>
        <Button title={t.onboarding.skip} variant="ghost" size="sm" onPress={finish} />
      </Row>
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', gap: space(5) }}>
        <View style={{ width: 160, height: 160, borderRadius: radius.pill, backgroundColor: c['accent-soft'], alignItems: 'center', justifyContent: 'center' }}>
          <Icon color={c['accent-strong']} size={72} strokeWidth={1.25} />
        </View>
        <Text variant="2xl" accessibilityRole="header" style={{ textAlign: 'center' }}>{slide.title}</Text>
        <Text tone="text-muted" style={{ textAlign: 'center', maxWidth: 320 }}>{slide.body}</Text>
      </View>
      <Row style={{ justifyContent: 'center' }} gap={2}>
        {t.onboarding.slides.map((_, k) => (
          <View key={k} style={{ width: k === i ? 20 : 8, height: 8, borderRadius: radius.pill, backgroundColor: k === i ? c.accent : c['progress-track'] }} />
        ))}
      </Row>
      <Button full size="lg" title={last ? t.onboarding.start : t.onboarding.next} onPress={() => (last ? finish() : setI(i + 1))} />
    </Screen>
  );
}
