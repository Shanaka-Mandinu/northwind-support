import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useSession } from './useSession';

export function RequireAuth({ children }: { children: ReactNode }) {
  const location = useLocation();
  const session = useSession();
  if (!session) return <Navigate to="/login" replace
    state={{ from: location.pathname + location.search + location.hash }} />;
  return <>{children}</>;
}
