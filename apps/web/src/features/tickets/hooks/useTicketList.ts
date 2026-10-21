import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface State {
  isLoading: boolean;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/** Owns the ticket list's filters and the data they produce. */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  const [query, setQuery] = useState<TicketFilters>(emptyFilters);
  const [state, setState] = useState<State>({ isLoading: true });
  const searchTimer = useRef<number | undefined>(undefined);
  const requestSequence = useRef({ id: 0 });

  const load = useCallback(() => {
    const sequence = requestSequence.current;
    const id = ++sequence.id;

    return ticketsApi.getTickets(query).then(
      (data) => {
        if (id === sequence.id) {
          setState({ isLoading: false, data, error: undefined });
        }
      },
      (error: unknown) => {
        if (id === sequence.id) {
          const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
          setState((current) => ({ ...current, isLoading: false, error: message }));
        }
      },
    );
  }, [query]);

  useEffect(() => {
    const sequence = requestSequence.current;
    void load();
    return () => { ++sequence.id; };
  }, [load]);

  useEffect(() => () => window.clearTimeout(searchTimer.current), []);

  const updateFilters = (patch: Partial<TicketFilters>) => {
    setFilters((current) => ({ ...current, ...patch, page: patch.page ?? 1 }));
    const { search, ...immediate } = patch;

    // All current and future non-search filters flow through the same immediate path.
    if (Object.keys(immediate).length > 0) {
      setState((current) => ({ ...current, isLoading: true, error: undefined }));
      setQuery((current) => ({ ...current, ...immediate, page: patch.page ?? 1 }));
    }

    if (search !== undefined) {
      window.clearTimeout(searchTimer.current);
      searchTimer.current = window.setTimeout(() => {
        setState((current) => ({ ...current, isLoading: true, error: undefined }));
        setQuery((current) => ({ ...current, search, page: 1 }));
        setFilters((current) => ({ ...current, page: 1 }));
      }, DEBOUNCE_MS);
    }
  };

  const resetFilters = () => {
    window.clearTimeout(searchTimer.current);
    // A fresh object also lets Reset retry the initial query after a failed request.
    setState((current) => ({ ...current, isLoading: true, error: undefined }));
    setFilters(emptyFilters);
    setQuery({ ...emptyFilters });
  };

  const reload = () => {
    setState((current) => ({ ...current, isLoading: true, error: undefined }));
    return load();
  };

  return { filters, updateFilters, resetFilters, reload, ...state };
}
