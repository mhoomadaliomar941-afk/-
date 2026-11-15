import { useEffect, useState } from 'react';
import { api } from './index';

type State<T> = { data: T | undefined; error: unknown; loading: boolean };

/** Loads with `fn`, reloads when the data layer emits a change, exposes `reload` for retry.
 *  Results from a previous `deps` value are ignored once it changes. */
export function useQuery<T>(fn: () => Promise<T>, deps: unknown[] = []) {
  const [state, setState] = useState<State<T>>({ data: undefined, error: null, loading: true });
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let alive = true;
    const run = () => fn()
      .then((data) => { if (alive) setState({ data, error: null, loading: false }); })
      .catch((error) => { if (alive) setState((s) => ({ ...s, error, loading: false })); });
    run();
    const unsubscribe = api.subscribe(run);
    return () => { alive = false; unsubscribe(); };
    // `fn` is a new closure each render; `deps` lists what it reads.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, attempt]);
  const reload = () => { setState((s) => ({ ...s, loading: true, error: null })); setAttempt((n) => n + 1); };
  return { ...state, reload };
}
