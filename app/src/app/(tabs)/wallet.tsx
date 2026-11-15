import { View } from 'react-native';
import { router } from 'expo-router';
import { Plus } from 'lucide-react-native';
import { Button, EmptyState, ErrorState, Row, Screen, SectionTitle, Skeleton, Text } from '../../components/ui';
import { TransactionRow, WalletBalanceCard } from '../../components/domain';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { radius, useColors } from '../../theme';
import { t } from '../../i18n';

// S07: both balances and the history.
export default function WalletScreen() {
  const c = useColors();
  const w = useQuery(() => api.getWallet());
  const tx = useQuery(() => api.listTxns());
  return (
    <Screen>
      <Row style={{ justifyContent: 'space-between' }}>
        <Text variant="2xl" accessibilityRole="header">{t.wallet.title}</Text>
        <Button title={t.wallet.topUp} size="sm" icon={<Plus color={c['on-accent']} size={18} />} onPress={() => router.push('/topup')} />
      </Row>
      {w.error ? <ErrorState onRetry={w.reload} /> : w.data ? <WalletBalanceCard w={w.data} /> : <Skeleton height={128} r={radius.xl} />}
      <SectionTitle>{t.wallet.history}</SectionTitle>
      <View>
        {tx.error ? <ErrorState onRetry={tx.reload} />
          : !tx.data ? [0, 1, 2].map((i) => <View key={i} style={{ paddingVertical: 12 }}><Skeleton width="60%" /></View>)
          : tx.data.length === 0 ? <EmptyState title={t.wallet.empty} />
          : tx.data.map((x) => <TransactionRow key={x.id} x={x} />)}
      </View>
    </Screen>
  );
}
