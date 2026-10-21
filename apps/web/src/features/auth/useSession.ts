import { useEffect, useSyncExternalStore } from 'react';
import { clearSession, parseSession, readSessionRaw, subscribeSession } from './session';

export function useSession() {
  const raw = useSyncExternalStore(subscribeSession, readSessionRaw, () => null);
  const session = parseSession(raw);
  const expiry = session?.expiresAtUtc;
  useEffect(() => {
    if (!raw) return;
    if (!expiry) { clearSession(); return; }
    let timer: number;
    const expire = () => {
      const remaining = Date.parse(expiry) - Date.now();
      if (remaining <= 0) clearSession();
      else timer = window.setTimeout(expire, Math.min(remaining, 2_147_483_647));
    };
    expire();
    return () => window.clearTimeout(timer);
  }, [raw, expiry]);
  return session;
}
