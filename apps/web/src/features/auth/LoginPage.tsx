import { type FormEvent, useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { authApi, clearSession, saveSession } from '../../api/auth';
import { useSession } from './useSession';
import { toErrorMessage } from '../../api/client';

export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const session = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const candidate = (location.state as { from?: unknown } | null)?.from;
  const from = typeof candidate === 'string' && candidate.startsWith('/') && !candidate.startsWith('//')
    && !candidate.includes('\\') && candidate.split(/[?#]/)[0] !== '/login' ? candidate : '/tickets';

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) return;
    if (!email.trim() || !/^[^\s@]+@[^\s@]+$/.test(email.trim()) || !password.trim()) {
      setError('Enter a valid email address and password.');
      return;
    }
    setError(null);
    setPending(true);

    try {
      const response = await authApi.login({ email: email.trim(), password });
      saveSession(response);
      navigate(from, { replace: true });
    } catch (caught) {
      clearSession();
      setError(toErrorMessage(caught, 'Unable to sign in.'));
    } finally {
      setPending(false);
    }
  }

  if (session) {
    return <Navigate to={from} replace />;
  }

  return (
    <section className="card auth-page">
      <form className="auth-form" onSubmit={handleSubmit} noValidate aria-busy={pending}>
        <h1>Sign in</h1>

        <label className="field">
          Email
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            disabled={pending}
            required
          />
        </label>

        <label className="field">
          Password
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            disabled={pending}
            required
          />
        </label>

        {error ? <p role="alert">{error}</p> : null}

        <button className="button button--primary" type="submit" disabled={pending}>
          {pending ? 'Signing in...' : 'Sign in'}
        </button>
      </form>
    </section>
  );
}
