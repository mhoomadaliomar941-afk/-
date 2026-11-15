import { Pressable, View } from 'react-native';
import { router } from 'expo-router';
import { Bell } from 'lucide-react-native';
import { CardSkeleton, EmptyState, ErrorState, Row, Screen, Text } from '../../components/ui';
import { ProductCard } from '../../components/domain';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, useColors } from '../../theme';
import { t } from '../../i18n';

// S03: open products, the most-filled first.
export default function Home() {
  const c = useColors();
  const q = useQuery(() => api.listProducts());
  const unread = useQuery(() => api.listNotifications().then((n) => n.filter((x) => !x.readAt).length));
  return (
    <Screen>
      <Row style={{ justifyContent: 'space-between' }}>
        <View>
          <Text variant="sm" tone="text-muted">{t.appName}</Text>
          <Text variant="2xl" accessibilityRole="header">{t.feed.title}</Text>
        </View>
        <Pressable accessibilityRole="button" accessibilityLabel={`${t.notifications.title}${unread.data ? `، ${unread.data} جديد` : ''}`}
          onPress={() => router.push('/notifications')}
          style={{ width: 44, height: 44, borderRadius: radius.pill, backgroundColor: c.surface, alignItems: 'center', justifyContent: 'center' }}>
          <Bell color={c.text} size={22} strokeWidth={1.75} />
          {unread.data ? <View style={{ position: 'absolute', top: 10, end: 11, width: 9, height: 9, borderRadius: 5, backgroundColor: c.danger }} /> : null}
        </Pressable>
      </Row>
      {q.error ? <ErrorState onRetry={q.reload} />
        : !q.data ? <><CardSkeleton /><CardSkeleton /></>
        : q.data.length === 0 ? <EmptyState title={t.feed.empty} body={t.feed.emptyBody} />
        : q.data.map((p) => <ProductCard key={p.id} p={p} onPress={() => router.push(`/product/${p.id}`)} />)}
    </Screen>
  );
}
