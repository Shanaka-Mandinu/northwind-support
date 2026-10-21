// Run only against a disposable, freshly seeded database: this creates and escalates tickets.
// TASK2_API_URL=http://localhost:5044/api node spec/task2-smoke.mjs
import assert from 'node:assert/strict';

const base = process.env.TASK2_API_URL ?? 'http://localhost:5044/api';
const loginResponse = await fetch(base + '/auth/login', {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'alex.turner@northwind-support.example', password: 'NorthwindDev!2026' }),
});
assert.equal(loginResponse.status, 200);
const session = await loginResponse.json();
async function call(path, expected = 200, body, method = 'POST') {
  const response = await fetch(base + path, {
    method: body === undefined ? 'GET' : method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${session.token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const data = await response.json();
  assert.equal(response.status, expected, `${path}: ${JSON.stringify(data)}`);
  return data;
}
const body = { title: 'Task 2 verification', description: 'Disposable test ticket for API verification.', customerId: 1, categoryId: 1 };
const created = await call('/tickets', 201, body);
assert.equal(created.priority, 'Medium');
assert.equal(created.assignedAgent.fullName, 'Sara Lindqvist');
assert.equal(created.slaStatus, 'WithinSla');
assert.ok(Math.abs(Date.parse(created.dueAtUtc) - Date.parse(created.createdAtUtc) - 12 * 3600000) <= 1);
assert.ok(created.triage.priorityReason && created.triage.slaReason && created.triage.assignmentReason);
await call(`/tickets/${created.id}/escalate`, 400, { reason: 'tiny', escalatedBy: 'Alex' });
await call('/tickets/999999/escalate', 404, { reason: 'Valid reason', escalatedBy: 'Alex' });
const first = await call(`/tickets/${created.id}/escalate`, 200, { reason: '  Customer impact increased  ', escalatedBy: ' Alex ' });
assert.equal(first.ticket.priority, 'High');
assert.equal(first.ticket.assignedAgent.id, created.assignedAgent.id);
assert.equal(first.escalation.fromPriority, 'Medium');
assert.equal(first.escalation.reason, 'Customer impact increased');
assert.equal(first.escalation.escalatedBy, `${session.agent.fullName} (agent ${session.agent.id})`);
assert.ok(Math.abs(Date.parse(first.ticket.dueAtUtc) - Date.parse(first.escalation.escalatedAtUtc) - 4 * 3600000) <= 1);
const second = await call(`/tickets/${created.id}/escalate`, 200, { reason: 'Further impact confirmed', escalatedBy: 'Ben' });
assert.equal(second.ticket.priority, 'Critical');
await call(`/tickets/${created.id}/escalate`, 409, { reason: 'Already critical', escalatedBy: 'Alex' });
const history = await call(`/tickets/${created.id}/escalations`);
assert.equal(history.length, 2);
assert.equal(history[0].id, second.escalation.id);
assert.equal(history[1].id, first.escalation.id);
await call('/tickets/999999/escalations', 404);
for (const id of [25, 26]) {
  await call(`/tickets/${id}/escalate`, 409, { reason: 'Completed ticket', escalatedBy: 'Alex' });
}
for (let index = 0; index < 5; index++) {
  const security = await call('/tickets', 201, { ...body, categoryId: 5, requestedPriority: 'Low' });
  assert.equal(security.priority, 'Critical');
  assert.ok(Math.abs(Date.parse(security.dueAtUtc) - Date.parse(security.createdAtUtc) - 2 * 3600000) <= 1);
  if (index < 4) assert.equal(security.assignedAgent.fullName, 'Priya Nair');
  else {
    assert.equal(security.assignedAgent, null);
    assert.match(security.triage.assignmentReason, /Unassigned because/);
  }
}
let total = 0;
for (const state of ['NotApplicable', 'WithinSla', 'AtRisk', 'Breached', 'Met']) {
  const page = await call(`/tickets?slaStatus=${state}&pageSize=100&sortBy=slaStatus`);
  assert.ok(page.items.every(t => t.slaStatus === state));
  assert.equal(page.items.length, page.totalCount);
  total += page.totalCount;
}
assert.equal(total, (await call('/tickets?pageSize=100')).totalCount);
await call('/tickets?slaStatus=999', 400);
console.log('PASS: create/triage, premium windows, specialist capacity, unassigned 201, escalation/history, 400/404/409, SQL SLA filters.');
