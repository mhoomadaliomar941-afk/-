import { useEffect } from 'react';
import { View } from 'react-native';
import { router } from 'expo-router';
import { Bell, CircleX, Gift, PartyPopper, Truck, Wallet } from 'lucide-react-native';
import { Card, EmptyState, ErrorState, Row, Screen, Skeleton, Text } from '../components/ui';
import { api, Notification } from '../data';
import { useQuery } from '../data/useQuery';
import { radius, useColors } from '../theme';
import { t } from '../i18n';

const ICON = { owner: PartyPopper, credited: Gift, product_open: Bell, order_shipped: Truck, topup_ok: Wallet, topup_rejected: CircleX };

// S14: inbox. Marked read when opened.
export default function Notifications() {
  const c = useColors();
  const q = useQuery(() => api.listNotifications());
  useEffect(() => { const i = setTimeout(() => api.markNotificationsRead(), 1500); return () => clearTimeout(i); }, []);
  const open = (n: Notification) => {
    if (n.type === 'topup_ok' || n.type === 'topup_rejected') return router.push('/(tabs)/wallet');
    if (n.productId) router.push(n.type === 'product_open' ? `/product/${n.productId}` : `/result/${n.productId}`);
  };
  return (
    <Screen edges={['bottom']}>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? [0, 1].map((i) => <Skeleton key={i} height={72} r={radius.lg} />)
        : q.data.length === 0 ? <EmptyState title={t.notifications.empty} />
        : q.data.map((n) => {
          const Icon = ICON[n.type];
          return (
            <Card key={n.id} label={`${n.title}، ${n.body}`} onPress={() => open(n)}
              style={!n.readAt ? { borderColor: c.accent } : undefined}>
              <Row gap={3}>
                <View style={{ width: 40, height: 40, borderRadius: radius.pill, backgroundColor: c['accent-soft'], alignItems: 'center', justifyContent: 'center' }}>
                  <Icon color={c['accent-strong']} size={20} />
                </View>
                <View style={{ flex: 1 }}>
                  <Text weight={600}>{n.title}</Text>
                  <Text variant="sm" tone="text-muted">{n.body}</Text>
                </View>
                {!n.readAt ? <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: c.accent }} accessibilityLabel="جديد" /> : null}
              </Row>
            </Card>
          );
        })}
    </Screen>
  );
}
