# Flow 1: inbound WhatsApp messages

This workflow receives Meta Cloud API webhooks at `/webhook/meta/whatsapp`.
The GET webhook validates Meta's subscription challenge against the
`META_VERIFY_TOKEN` environment variable. The POST webhook acknowledges the
request immediately with HTTP 200, verifies `X-Hub-Signature-256` against the
original raw request bytes using `META_APP_SECRET`, and then extracts messages.

Status-only updates are ignored. For message events, the flow reads
`phone_number_id`, WhatsApp sender ID, profile name, message ID, type, body,
and timestamp. Non-text messages are recorded with a descriptive placeholder.

The Postgres Execute Query node performs the following parameterized operation:

1. Resolve an active tenant by `phone_number_id`.
2. Upsert the lead by `(tenant_id, wa_id)`.
3. Insert the inbound message, using the unique `wa_message_id` constraint to
   deduplicate Meta retries.
4. Return `is_new = true` only when a message row was inserted.

Only new messages are passed to the placeholder `flow2-qualify` sub-workflow.
If no active tenant matches the `phone_number_id`, no lead or message is
created.

The exact SQL used by the Postgres node is:

```sql
WITH tenant AS (
    SELECT id
    FROM tenants
    WHERE phone_number_id = $1
      AND active = TRUE
), lead_upsert AS (
    INSERT INTO leads (tenant_id, wa_id, profile_name, updated_at)
    SELECT tenant.id, $2, $3, NOW()
    FROM tenant
    ON CONFLICT (tenant_id, wa_id)
    DO UPDATE SET
        profile_name = COALESCE(EXCLUDED.profile_name, leads.profile_name),
        updated_at = NOW()
    RETURNING id, tenant_id
), message_insert AS (
    INSERT INTO messages (lead_id, wa_message_id, direction, type, body, received_at)
    SELECT lead_upsert.id, $4, 'inbound', $5, $6, TO_TIMESTAMP($7::double precision)
    FROM lead_upsert
    ON CONFLICT (wa_message_id) DO NOTHING
    RETURNING id, lead_id
)
SELECT
    EXISTS (SELECT 1 FROM message_insert) AS is_new,
    (SELECT id FROM tenant) AS tenant_id,
    (SELECT id FROM lead_upsert) AS lead_id,
    (SELECT id FROM message_insert) AS message_id;
```

The query parameters, in order, are `phone_number_id`, `wa_id`,
`profile_name`, `wa_message_id`, `message_type`, `message_body`, and the
message timestamp in Unix seconds.

The workflow relies on the tables and columns created by
`db/init/02-schema.sql`. Import `n8n/flow1-inbound.json`, create a Postgres
credential for the application database, select it on the Postgres node, and
replace the `flow2-qualify` workflow placeholder after Flow 2 exists.
