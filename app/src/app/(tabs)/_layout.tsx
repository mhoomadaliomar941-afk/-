import { Redirect, Tabs } from 'expo-router';
import { House, Store, Ticket, User, Wallet } from 'lucide-react-native';
import { Text as RNText } from 'react-native';
import { fonts, layout, useColors } from '../../theme';
import { useSession } from '../../lib/session';
import { t } from '../../i18n';

export default function TabsLayout() {
  const c = useColors();
  const { user, ready } = useSession();
  if (!ready) return null;
  if (!user) return <Redirect href="/login" />;
  return (
    <Tabs screenOptions={{
      headerShown: false, tabBarActiveTintColor: c.accent, tabBarInactiveTintColor: c['text-muted'],
      tabBarStyle: { backgroundColor: c['surface-raised'], borderTopColor: c.border, height: layout['tab-bar-height'] },
      tabBarItemStyle: { paddingVertical: 6 },
    }}>
      <Tabs.Screen name="index" options={{ title: t.tabs.home, tabBarLabel: ({ color }) => <TabLabel title={t.tabs.home} color={color} />, tabBarIcon: ({ color, size }) => <House color={color as string} size={size} strokeWidth={1.75} /> }} />
      <Tabs.Screen name="mine" options={{ title: t.tabs.mine, tabBarLabel: ({ color }) => <TabLabel title={t.tabs.mine} color={color} />, tabBarIcon: ({ color, size }) => <Ticket color={color as string} size={size} strokeWidth={1.75} /> }} />
      <Tabs.Screen name="wallet" options={{ title: t.tabs.wallet, tabBarLabel: ({ color }) => <TabLabel title={t.tabs.wallet} color={color} />, tabBarIcon: ({ color, size }) => <Wallet color={color as string} size={size} strokeWidth={1.75} /> }} />
      <Tabs.Screen name="stores" options={{ title: t.tabs.stores, tabBarLabel: ({ color }) => <TabLabel title={t.tabs.stores} color={color} />, tabBarIcon: ({ color, size }) => <Store color={color as string} size={size} strokeWidth={1.75} /> }} />
      <Tabs.Screen name="profile" options={{ title: t.tabs.profile, tabBarLabel: ({ color }) => <TabLabel title={t.tabs.profile} color={color} />, tabBarIcon: ({ color, size }) => <User color={color as string} size={size} strokeWidth={1.75} /> }} />
    </Tabs>
  );
}

// Own label: the default one measured too narrow for Arabic on web and clipped it.
function TabLabel({ title, color }: { title: string; color: unknown }) {
  return <RNText style={{ fontFamily: fonts[500], fontSize: 12, lineHeight: 16, color: color as string, textAlign: 'center' }}>{title}</RNText>;
}
