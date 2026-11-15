import { Redirect } from 'expo-router';
import { onboarding, useSession } from '../lib/session';

export default function Index() {
  const { user, ready } = useSession();
  if (!ready) return null;
  if (user) return <Redirect href="/(tabs)" />;
  return <Redirect href={onboarding.seen() ? '/login' : '/onboarding'} />;
}
