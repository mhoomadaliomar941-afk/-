import { Pressable, View } from 'react-native';
import { router } from 'expo-router';
import { Bell, ChevronLeft, CircleHelp, Languages, LogOut, Trophy, User } from 'lucide-react-native';
import { Card, Divider, Row, Screen, Text } from '../../components/ui';
import { api } from '../../data';
import { useSession } from '../../lib/session';
import { radius, space, useColors } from '../../theme';
import { t } from '../../i18n';

// S15: account and settings.
export default function Profile() {
  const c = useColors();
  const { user } = useSession();
  const row = (Icon: typeof Bell, label: string, onPress?: () => void, value?: string, tone: 'text' | 'danger' = 'text') => (
    <Pressable accessibilityRole="button" accessibilityLabel={label} onPress={onPress}
      style={{ flexDirection: 'row', alignItems: 'center', gap: space(3), minHeight: 52 }}>
      <Icon color={tone === 'danger' ? c.danger : c['text-muted']} size={20} strokeWidth={1.75} />
      <Text tone={tone} style={{ flex: 1 }}>{label}</Text>
      {value ? <Text tone="text-muted" variant="sm">{value}</Text> : null}
      {onPress && tone !== 'danger' ? <ChevronLeft color={c['text-muted']} size={18} /> : null}
    </Pressable>
  );
  return (
    <Screen>
      <Text variant="2xl" accessibilityRole="header">{t.profile.title}</Text>
      <Row gap={3}>
        <View style={{ width: 56, height: 56, borderRadius: radius.pill, backgroundColor: c['accent-soft'], alignItems: 'center', justifyContent: 'center' }}>
          <User color={c['accent-strong']} size={26} />
        </View>
        <View>
          <Text variant="lg">{user?.fullName ?? 'عضو'}</Text>
          <Text tone="text-muted" style={{ writingDirection: 'ltr', textAlign: 'right' }}>{user?.phone}</Text>
        </View>
      </Row>
      <Card style={{ paddingVertical: space(1), gap: 0 }}>
        {row(Bell, t.profile.notifications, () => router.push('/notifications'))}
        <Divider />
        {row(Trophy, t.profile.owners, () => router.push('/owners'))}
        <Divider />
        {row(Languages, t.profile.language, undefined, 'العربية')}
        <Divider />
        {row(CircleHelp, t.profile.help, () => router.push('/help'))}
      </Card>
      <Card style={{ paddingVertical: space(1) }}>
        {row(LogOut, t.profile.logout, async () => { await api.signOut(); router.replace('/login'); }, undefined, 'danger')}
      </Card>
    </Screen>
  );
}
