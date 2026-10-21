// Run against a disposable development database. Creates and escalates one ticket.
// TASK3_API_URL=http://localhost:5044/api node spec/task3-smoke.mjs
import assert from 'node:assert/strict';
const base = process.env.TASK3_API_URL ?? 'http://localhost:5044/api';
async function call(path, status, token, body) {
  const response = await fetch(base + path, { method: body ? 'POST' : 'GET', headers: {
    'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}),
  }, ...(body ? { body: JSON.stringify(body) } : {}) });
  const data = await response.json();
  assert.equal(response.status, status, `${path}: ${JSON.stringify(data)}`);
  return data;
}
const email = 'alex.turner@northwind-support.example';
const password = 'NorthwindDev!2026';
const wrong = await call('/auth/login', 401, null, { email, password: 'incorrect' });
for (const credentials of [{ email: 'missing@example.com', password },
  { email: 'yuki.tanaka@northwind-support.example', password }]) {
  assert.deepEqual(await call('/auth/login', 401, null, credentials), wrong);
}
assert.equal(wrong.token, undefined);
await call('/auth/login', 400, null, { email: '', password: '' });
const session = await call('/auth/login', 200, null, { email, password });
const payload = JSON.parse(Buffer.from(session.token.split('.')[1], 'base64url'));
assert.equal(payload.exp * 1000, Date.parse(session.expiresAtUtc));
assert.equal(payload.exp - payload.nbf, 120 * 60);
assert.equal(payload.sub, String(session.agent.id));
for (const path of ['/tickets', '/agents', '/customers', '/categories', '/auth/me']) {
  await call(path, 401);
  await call(path, 200, session.token);
}
assert.deepEqual(await call('/auth/me', 200, session.token), session.agent);
await call('/tickets', 401, 'invalid-token');
const created = await call('/tickets', 201, session.token, {
  title: 'Authentication smoke test', description: 'Check escalation actor cannot be spoofed.', customerId: 1, categoryId: 1,
});
const escalated = await call(`/tickets/${created.id}/escalate`, 200, session.token, {
  reason: 'Verify authenticated actor', escalatedBy: 'Forged administrator',
});
assert.equal(escalated.escalation.escalatedBy, `${session.agent.fullName} (agent ${session.agent.id})`);
assert.equal((await fetch(new URL('/swagger/index.html', base))).status, 200);
console.log('PASS: login, generic credential failures, inactive agent, validation, JWT expiry, protected endpoints, /me, invalid token, authenticated escalation actor, open Swagger.');
