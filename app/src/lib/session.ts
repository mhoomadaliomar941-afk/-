import { api } from '../data';
import { useQuery } from '../data/useQuery';

export function useSession() {
  const q = useQuery(() => api.me());
  return { user: q.data ?? null, ready: !q.loading || q.data !== undefined };
}

// Onboarding is shown once per app run in the fake build; /replica-backend moves this to storage.
let seen = false;
export const onboarding = { seen: () => seen, done: () => { seen = true; } };

export const newKey = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
