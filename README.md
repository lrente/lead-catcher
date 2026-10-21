# WhatsApp Lead Automation

Phase 1 provides a self-hosted n8n inbound webhook for Meta WhatsApp Cloud
API, PostgreSQL persistence, message deduplication, and an integration test
script. Flow 2 is intentionally not included in this phase.

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
