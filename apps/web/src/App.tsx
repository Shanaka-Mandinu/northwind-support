import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { clearSession } from './features/auth/session';
import { useSession } from './features/auth/useSession';

/** Application shell: the header, the navigation, and wherever the router puts the page. */
export function App() {
  const navigate = useNavigate();
  const session = useSession();

  const onLogout = () => {
    clearSession();
    navigate('/login', { replace: true });
  };

  return (
    <div className="app">
      <header className="app__header">
        <Link className="app__brand" to="/tickets">
          Northwind Support
        </Link>

        <nav className="app__nav" aria-label="Main">
          <NavLink to="/tickets">Tickets</NavLink>
          <NavLink to="/customers">Customers</NavLink>
        </nav>

        <div className="app__user">
          <span>{session?.agent.fullName ?? 'Agent'}</span>
          <button type="button" onClick={onLogout}>Log out</button>
        </div>
      </header>

      <main className="app__main">
        <Outlet />
      </main>
    </div>
  );
}
