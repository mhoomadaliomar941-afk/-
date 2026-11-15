import { useEffect } from 'react';
import { Redirect, Tabs } from 'expo-router';
import { House, ShoppingBag, Ticket, User, Wallet } from 'lucide-react-native';
import { Text as RNText } from 'react-native';
import { fonts, layout, useColors } from '../../theme';
import { useSession } from '../../lib/session';
import { registerForPush } from '../../lib/push';
import { t } from '../../i18n';

export default function TabsLayout() {
  const c = useColors();
  const { user, ready } = useSession();
  useEffect(() => { if (user) registerForPush(); }, [user]);
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
      <Tabs.Screen name="store" options={{ title: t.tabs.store, tabBarLabel: ({ color }) => <TabLabel title={t.tabs.store} color={color} />, tabBarIcon: ({ color, size }) => <ShoppingBag color={color as string} size={size} strokeWidth={1.75} /> }} />
      <Tabs.Screen name="profile" options={{ title: t.tabs.profile, tabBarLabel: ({ color }) => <TabLabel title={t.tabs.profile} color={color} />, tabBarIcon: ({ color, size }) => <User color={color as string} size={size} strokeWidth={1.75} /> }} />
    </Tabs>
  );
}

// Own label: the default one measured too narrow for Arabic on web and clipped it.
function TabLabel({ title, color }: { title: string; color: unknown }) {
  return <RNText style={{ fontFamily: fonts[500], fontSize: 12, lineHeight: 16, color: color as string, textAlign: 'center' }}>{title}</RNText>;
}
