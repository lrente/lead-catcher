# WhatsApp Lead Automation

The service includes a self-hosted n8n inbound webhook for Meta WhatsApp Cloud
API, PostgreSQL persistence and message deduplication, plus a .NET 8
qualification worker and a human approval API. Draft replies are sent only
after explicit operator approval, recipient opt-in confirmation, and a check
that the WhatsApp customer-service window is still open.

## Setup

1. Install Docker Compose, Git Bash or WSL, `curl`, and `openssl`. The test
   script runs `psql` inside the PostgreSQL container by default. To use a
   host-side client or another database, set `POSTGRES_HOST` and install `psql`.
2. Copy `.env.example` to `.env`. Replace every placeholder, including the
   PostgreSQL password, n8n encryption key, Meta app secret, and verification
   token. Generate strong values with `openssl rand -hex 32`.
3. Set `N8N_DOMAIN` to a public DNS name pointing to this host for a live Meta
   webhook. `localhost` is suitable only for local development.
4. Start the services:

   ```sh
   docker compose up -d
   ```

   PostgreSQL creates the application schema and a separate `n8n` database on
   first initialization. Caddy terminates HTTPS for `N8N_DOMAIN`; n8n is also
   exposed on `127.0.0.1:5678` for local testing.
5. Open `https://N8N_DOMAIN` and complete the n8n owner setup.
6. Import `n8n/flow1-inbound.json` using **Workflows → Import from File**.
7. Create an n8n Postgres credential for the application database:
   - Host: `postgres`
   - Port: `5432`
   - Database: the value of `POSTGRES_DB` (default `lead_automation`)
   - User/password: `POSTGRES_USER` and `POSTGRES_PASSWORD`
8. Select that credential on **Upsert Lead and Deduplicate Message**. Create
   a `flow2-qualify` workflow (it may be a stub for now), then select it on
   **Run flow2-qualify**. The imported workflow is inactive until activated.
9. Activate the workflow after configuring the credential and sub-workflow.

The Code nodes read the Meta secrets from the n8n process environment and
require `NODE_FUNCTION_ALLOW_BUILTIN=crypto`. Environment access is enabled
for n8n Code nodes; only import workflows you trust into this instance.

The sample tenant in `seed.example.sql` uses the phone number ID included in
the text and audio fixtures. Add or update tenants with the actual
`phone_number_id` from Meta before using the service.

## Configure Meta

1. In the Meta developer app, add the WhatsApp product and configure its
   webhook callback URL as `https://N8N_DOMAIN/webhook/meta/whatsapp`.
2. Enter the exact value of `META_VERIFY_TOKEN` from `.env` as the Meta
   verification token, then subscribe to the `messages` webhook field.
3. Set Meta's app secret in `META_APP_SECRET` in `.env`, then recreate the n8n
   container so it receives the value: `docker compose up -d --force-recreate n8n`.
4. Keep the callback reachable over public HTTPS. Never include app secrets
   or verification tokens in workflow JSON or source control.

## Run the Flow 1 tests

Start the stack, import and activate the workflow, and configure its Postgres
credential first. Then run from Git Bash or WSL:

```sh
bash scripts/test-flow1.sh
```

Tests send realistic signed fixtures to the local webhook and assert database
rows for a text message, replay, invalid signature, status event, unknown
phone number ID, and audio message. They also verify the Meta GET challenge.
The script clears only rows matching its dedicated fixture IDs and uses a
dedicated integration-test tenant. Do not point it at production data.

To send one fixture manually:

```sh
bash scripts/sign-and-send.sh tests/fixtures/valid-text-message.json
```

Use `--bad-signature` as a second argument when testing signature rejection.

## Qualification worker (Phase 2)

The `qualification-worker` Compose service runs as a .NET 8 background worker.
It claims eligible leads using a short PostgreSQL lease, waits for the message
debounce period, loads up to 10 chronological messages, and calls the
Anthropic Messages API. Replies and classifications are written to
`qualifications`; Phase 2 qualification itself does not send WhatsApp messages.

### Worker configuration

Set these values in `.env` before starting the full Compose stack:

| Variable | Default | Purpose |
|---|---:|---|
| `QUALIFICATION_DEBOUNCE_SECONDS` | `8` | Wait after the newest unprocessed inbound message before claiming its lead. |
| `QUALIFICATION_LEASE_SECONDS` | `120` | Lead claim lease; released after processing or failure. |
| `QUALIFICATION_MINIMUM_CONFIDENCE` | `0.6` | Lower confidence forces a human handoff. |
| `QUALIFICATION_MAX_ATTEMPTS` | `3` | Failed qualification attempts before a poison-message handoff is stored. |
| `ANTHROPIC_BASE_URL` | `https://api.anthropic.com` | Anthropic API base URL. |
| `ANTHROPIC_API_KEY` | required | Anthropic API key. Keep it out of source control. |
| `ANTHROPIC_MODEL` | required | A model ID enabled for your Anthropic account. The project intentionally sets no model name. |
| `ANTHROPIC_TIMEOUT_SECONDS` | `25` | HTTP client timeout. |
| `ANTHROPIC_MAX_TOKENS` | `700` | Maximum completion tokens per API attempt. |

`POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` also configure the
worker's database connection. The default per-tenant `hot_threshold` is 70;
it is stored on `tenants` and can be changed for an individual tenant. The
confidence threshold is configured globally. Polling starts at 2 seconds and
backs off to 10 seconds while idle.

The worker applies its EF Core migrations at startup. They assume the Phase 1
tables from `db/init/02-schema.sql` already exist; migrations add qualification
state, `messages.processed_at`, the per-tenant hot threshold, the
`qualifications` table, and Phase 3 approval/audit fields. This supports a
fresh Compose database (the init SQL runs first) and an existing Phase 1 or
Phase 2 database without replacing its data.

Copy `.env.example` to `.env`, fill in `ANTHROPIC_API_KEY` and
`ANTHROPIC_MODEL`, then run `docker compose up -d`. Add the tenant's verified
business facts to `tenants.business_context`. A signed Flow 1 webhook message
will be qualified after the debounce window using the real Anthropic API.
There is no fake qualifier in the Compose worker; the fake is used only by the
automated tests.

### Tests

Run unit tests and the default suite (no external LLM calls):

```sh
dotnet test WaLeads.sln
```

PostgreSQL Testcontainers integration tests use `FakeLeadQualifier` and are
opt-in. With Docker available, run:

```sh
RUN_TESTCONTAINERS=1 dotnet test WaLeads.sln --filter Category=Integration
```

The tests cover migrations over existing Phase 1 data, debounce batching,
messages arriving during an LLM call, concurrent workers, failures, leases,
and tenant hot thresholds. In PowerShell, set `$env:RUN_TESTCONTAINERS = "1"`
before running the same `dotnet test` command.

The separate `tests/WaLeads.Evals` project contains 20 Portuguese conversation
fixtures. Set Anthropic user-secrets (no key is stored in the repository):

```sh
dotnet user-secrets set "Anthropic:ApiKey" "YOUR_KEY" --project tests/WaLeads.Evals/WaLeads.Evals.csproj
dotnet user-secrets set "Anthropic:Model" "YOUR_ACCOUNT_MODEL_ID" --project tests/WaLeads.Evals/WaLeads.Evals.csproj
RUN_LLM_EVALS=1 dotnet test tests/WaLeads.Evals/WaLeads.Evals.csproj --filter Category=Eval
```

The eval test is skipped unless `RUN_LLM_EVALS=1` is set. It prints each
fixture's predicted intent/action, expected values, and the total pass rate.

## WhatsApp reply approval (Phase 3)

The worker places `ask_question` and `propose_booking` replies in the approval
queue. It never sends them automatically. `handoff_human` and forced handoffs
appear in the human-review queue; `ignore` is marked actioned without sending.
The API runs in the existing qualification-worker container and is bound to
`127.0.0.1:8081` on the host only. It is not routed through Caddy or exposed
publicly. Operators can call it locally or through an SSH tunnel.

Set the following in `.env`:

| Variable | Purpose |
|---|---|
| `META_ACCESS_TOKEN` | Meta access token with permission to send from each configured tenant phone-number ID. Keep it secret. |
| `META_GRAPH_API_VERSION` | A supported Graph API version, in `vNN.N` format; choose and maintain this value for your Meta app. |
| `WHATSAPP_TIMEOUT_SECONDS` | Outbound request timeout (default `25`). |
| `APPROVAL_API_TOKEN` | Random bearer token of at least 32 bytes. Never commit it. |

The Compose service requires these settings and the worker applies the
additive approval migrations at startup. One access token is shared by this
MVP across configured tenants; verify that it is authorized for every
`tenants.phone_number_id` you use.

Use the API with an `Authorization: Bearer` header. For example, in a shell
with `APPROVAL_API_TOKEN` set:

```sh
curl -H "Authorization: Bearer $APPROVAL_API_TOKEN" \
  http://127.0.0.1:8081/api/approvals
```

Approve a draft only after verifying the recipient has opted in. The
confirmation is required in the request and its timestamp is persisted for
audit:

```sh
curl -X POST \
  -H "Authorization: Bearer $APPROVAL_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"recipientOptInConfirmed":true}' \
  http://127.0.0.1:8081/api/approvals/QUALIFICATION_ID/approve
```

`POST /api/approvals/{id}/reject` rejects a pending draft without sending.
`POST /api/approvals/{id}/resolve` closes a human-review item. The API rechecks
the latest inbound message immediately before sending. If there is no inbound
message within the last 24 hours, approval is not sent; the item is instead
marked for human review (`outside_customer_service_window`). This phase does
not support approved message templates or sending outside that window.

WhatsApp Cloud API send requests are non-retried to avoid accidental duplicate
messages. If the send outcome cannot be confirmed, the item is marked
`send_uncertain` for operator reconciliation rather than retried automatically.
An item left in `sending` after a process/database failure also requires manual
reconciliation before any retry. The API logs qualification/lead/message IDs
and error IDs, not message text or recipient phone numbers.

Configure and verify the Meta access token, Graph API version, account
permissions, recipient opt-in records, and the test phone-number IDs before
approving a live message. The API and worker do not provide an operator
dashboard; the bearer-authenticated HTTP endpoints are the review interface
for this phase.
