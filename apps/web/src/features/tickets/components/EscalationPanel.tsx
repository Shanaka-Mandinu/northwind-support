import { useCallback, useState, type FormEvent } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { toErrorMessage } from '../../../api/client';
import { useAsyncData } from '../../../hooks/useAsyncData';
import { formatDateTime } from '../../../lib/format';
import type { TicketDetail } from '../../../types/api';

interface Props {
  ticket: TicketDetail;
  onUpdated: (ticket: TicketDetail) => void;
}

export function EscalationPanel({ ticket, onUpdated }: Props) {
  const loadHistory = useCallback(() => ticketsApi.getEscalations(ticket.id), [ticket.id]);
  const history = useAsyncData(loadHistory, 'Unable to load escalation history.');
  const [reason, setReason] = useState('');
  const [actor, setActor] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [submitted, setSubmitted] = useState(false);
  const [success, setSuccess] = useState(false);
  const reasonValid = reason.trim().length >= 5 && reason.trim().length <= 500;
  const actorValid = actor.trim().length >= 1 && actor.trim().length <= 100;
  const allowed = ticket.priority !== 'Critical' && ticket.status !== 'Resolved' && ticket.status !== 'Closed';

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitted(true);
    setSuccess(false);
    if (!reasonValid || !actorValid || !allowed || pending) return;
    setPending(true);
    setError(undefined);
    try {
      const result = await ticketsApi.escalate(ticket.id, { reason: reason.trim(), escalatedBy: actor.trim() });
      onUpdated(result.ticket);
      history.setData([result.escalation, ...(history.data ?? [])]);
      await history.reload();
      setReason('');
      setSubmitted(false);
      setSuccess(true);
    } catch (caught) {
      setError(toErrorMessage(caught, 'Unable to escalate ticket.'));
    } finally {
      setPending(false);
    }
  };

  return <div className="card">
    <h2>Escalation</h2>
    {allowed ? <form onSubmit={(event) => void submit(event)} noValidate aria-busy={pending}>
      <div className="field">
        <label className="field__label" htmlFor="escalation-reason">Reason</label>
        <textarea id="escalation-reason" value={reason} disabled={pending}
          aria-invalid={submitted && !reasonValid} aria-describedby="escalation-reason-help"
          onChange={(event) => setReason(event.target.value)} />
        <p id="escalation-reason-help" className={submitted && !reasonValid ? 'field__error' : 'summary__note'}>
          Enter 5–500 characters after trimming spaces.
        </p>
      </div>
      <div className="field">
        <label className="field__label" htmlFor="escalation-actor">Escalated by</label>
        <input id="escalation-actor" value={actor} disabled={pending}
          aria-invalid={submitted && !actorValid} aria-describedby="escalation-actor-help"
          onChange={(event) => setActor(event.target.value)} />
        <p id="escalation-actor-help" className={submitted && !actorValid ? 'field__error' : 'summary__note'}>
          Enter your name (1–100 characters).
        </p>
      </div>
      <button className="button button--primary" type="submit" disabled={pending}>
        {pending ? 'Escalating...' : 'Escalate ticket'}
      </button>
    </form> : <p>This ticket cannot be escalated because it is {ticket.status === 'Resolved' || ticket.status === 'Closed' ? ticket.status : 'Critical'}.</p>}
    {error && <p className="field__error" role="alert">{error}</p>}
    {success && <p role="status">Ticket escalated.</p>}
    <h3>Escalation history</h3>
    {history.isLoading && <p role="status">Loading history...</p>}
    {history.error && <div role="alert">
      <p>{history.error}</p>
      <button className="button button--ghost" onClick={() => void history.reload()}>Retry history</button>
    </div>}
    {!history.isLoading && !history.error && history.data?.length === 0 && <p>No escalations yet.</p>}
    <ol>
      {history.data?.map((entry) => <li key={entry.id}>
        <p><strong>{entry.fromPriority} → {entry.toPriority}</strong></p>
        <p>Agent: {entry.fromAgent?.fullName ?? 'Unassigned'} → {entry.toAgent?.fullName ?? 'Unassigned'}</p>
        <p>Due: {formatDateTime(entry.fromDueAtUtc)} → {formatDateTime(entry.toDueAtUtc)}</p>
        <p>{entry.reason}</p>
        <p>{entry.escalatedBy} · {formatDateTime(entry.escalatedAtUtc)}</p>
      </li>)}
    </ol>
  </div>;
}
