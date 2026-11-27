import { router } from 'expo-router';
import { Gift, Landmark, Package, Settings, Truck } from 'lucide-react-native';
import { Card, Row, Screen, Text } from '../../components/ui';
import { AdminOnly } from '../../components/admin';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { useColors } from '../../theme';
import { t } from '../../i18n';

export default function AdminHome() {
  return <AdminOnly><Menu /></AdminOnly>;
}

function Menu() {
  const c = useColors();
  const orders = useQuery(() => api.adminListOrders());
  const waiting = (orders.data ?? []).filter((o) => o.status === 'pending').length;
  const cliq = useQuery(() => api.adminListCliqTopups());
  const toReview = (cliq.data ?? []).filter((r) => r.status === 'awaiting_review').length;
  const item = (Icon: typeof Gift, title: string, sub: string, href: '/admin/products' | '/admin/store' | '/admin/orders' | '/admin/cliq' | '/admin/settings') => (
    <Card label={title} onPress={() => router.push(href)}>
      <Row gap={3}>
        <Icon color={c['accent-strong']} size={24} />
        <Text variant="lg" style={{ flex: 1 }}>{title}</Text>
        <Text tone="text-muted" variant="sm">{sub}</Text>
      </Row>
    </Card>
  );
  return (
    <Screen edges={['bottom']}>
      {item(Landmark, t.admin.cliq, toReview ? `${toReview} ${t.cliq.status.awaiting_review}` : '', '/admin/cliq')}
      {item(Gift, t.admin.draws, '', '/admin/products')}
      {item(Package, t.admin.storeProducts, '', '/admin/store')}
      {item(Truck, t.admin.orders, waiting ? `${waiting} ${t.orders.status.pending}` : '', '/admin/orders')}
      {item(Settings, t.admin.settings, '', '/admin/settings')}
    </Screen>
  );
}
