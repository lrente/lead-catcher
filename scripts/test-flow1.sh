#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ ! -f .env ]]; then
  echo "Missing .env; copy .env.example and configure it first." >&2
  exit 2
fi

set -a
# shellcheck disable=SC1091
. ./.env
set +a

if [[ -z "${POSTGRES_USER:-}" || -z "${POSTGRES_DB:-}" ]]; then
  echo "Set POSTGRES_USER and POSTGRES_DB in .env." >&2
  exit 2
fi

db() {
  if [[ -n "${POSTGRES_HOST:-}" ]]; then
    if ! command -v psql >/dev/null 2>&1; then
      echo "POSTGRES_HOST is set, but psql is not installed on the host." >&2
      return 127
    fi
    PGPASSWORD="${POSTGRES_PASSWORD:-}" psql \
      -h "$POSTGRES_HOST" \
      -p "${POSTGRES_PORT:-5432}" \
      -U "$POSTGRES_USER" \
      -d "$POSTGRES_DB" \
      -v ON_ERROR_STOP=1 "$@"
  else
    docker compose exec -T postgres psql \
      -U "$POSTGRES_USER" \
      -d "$POSTGRES_DB" \
      -v ON_ERROR_STOP=1 "$@"
  fi
}

PHONE_NUMBER_ID="1098765432109876"
WA_ID="351912345678"
TEXT_ID="wamid.HBgLMTIzNDU2Nzg5MDEyFQIAERgSRTY3ODkwQUJDREVGR0hJSktM"
BAD_SIGNATURE_ID="wamid.HBgLMTIzNDU2Nzg5MDEyFQIAERgSQUJDREVGR0hJSktMTU5PUA"
UNKNOWN_TENANT_ID="wamid.HBgLMTIzNDU2Nzg5MDEyFQIAERgSQUJDREVGR0hJSktMTU5PUQ"
AUDIO_ID="wamid.HBgLMTIzNDU2Nzg5MDEyFQIAERgSQUJDREVGR0hJSktMTU5PUg"
STATUS_ID="wamid.HBgLMTIzNDU2Nzg5MDEyFQIAERgSRk9PQkFSQkFaU1RBVFVT"

failures=0

db \
  -v phone_id="$PHONE_NUMBER_ID" \
  -v wa_id="$WA_ID" \
  -v text_id="$TEXT_ID" \
  -v bad_signature_id="$BAD_SIGNATURE_ID" \
  -v unknown_tenant_id="$UNKNOWN_TENANT_ID" \
  -v audio_id="$AUDIO_ID" \
  -v status_id="$STATUS_ID" \
  -v tenant_name="Flow 1 Integration Test" \
  -c "DELETE FROM messages WHERE wa_message_id IN (:'text_id', :'bad_signature_id', :'unknown_tenant_id', :'audio_id', :'status_id') OR lead_id IN (SELECT id FROM leads WHERE wa_id = :'wa_id' AND tenant_id = (SELECT id FROM tenants WHERE phone_number_id = :'phone_id')); DELETE FROM leads WHERE wa_id = :'wa_id' AND tenant_id = (SELECT id FROM tenants WHERE phone_number_id = :'phone_id'); INSERT INTO tenants (name, phone_number_id, business_context, active) VALUES (:'tenant_name', :'phone_id', 'Integration test tenant', TRUE) ON CONFLICT (phone_number_id) DO UPDATE SET active = TRUE;"

send_fixture() {
  local name="$1"
  local path="$2"
  local signature_option="${3:-}"
  local status
  if [[ -n "$signature_option" ]]; then
    status="$(bash scripts/sign-and-send.sh "$path" "$signature_option")" || status="000"
  else
    status="$(bash scripts/sign-and-send.sh "$path")" || status="000"
  fi
  if [[ "$status" != "200" ]]; then
    echo "FAIL $name (webhook returned HTTP $status)" >&2
    failures=$((failures + 1))
    return 1
  fi
  sleep "${FLOW1_SETTLE_SECONDS:-2}"
}

assert_count() {
  local name="$1"
  local message_id="$2"
  local expected="$3"
  local actual
  actual="$(db -At -v message_id="$message_id" \
    -c "SELECT COUNT(*) FROM messages WHERE wa_message_id = :'message_id';" | tr -d '[:space:]')"
  if [[ "$actual" == "$expected" ]]; then
    echo "PASS $name"
  else
    echo "FAIL $name (expected $expected row(s), got $actual)" >&2
    failures=$((failures + 1))
  fi
}

assert_body() {
  local name="$1"
  local message_id="$2"
  local expected="$3"
  local actual
  actual="$(db -At -v message_id="$message_id" \
    -c "SELECT body FROM messages WHERE wa_message_id = :'message_id';")"
  if [[ "$actual" == "$expected" ]]; then
    echo "PASS $name"
  else
    echo "FAIL $name (unexpected message body)" >&2
    failures=$((failures + 1))
  fi
}

if send_fixture "valid text message request" "tests/fixtures/valid-text-message.json"; then
  assert_count "valid text message stored once" "$TEXT_ID" "1"
  assert_body "text body extracted" "$TEXT_ID" "Olá, gostaria de saber mais informações."
fi

if send_fixture "replayed text message request" "tests/fixtures/replayed-message.json"; then
  assert_count "replay remains deduplicated" "$TEXT_ID" "1"
fi

if send_fixture "bad signature request" "tests/fixtures/bad-signature.json" "--bad-signature"; then
  assert_count "bad signature creates no row" "$BAD_SIGNATURE_ID" "0"
fi

if send_fixture "status update request" "tests/fixtures/status-update.json"; then
  assert_count "status update creates no message row" "$STATUS_ID" "0"
fi

if send_fixture "unknown phone number request" "tests/fixtures/unknown-phone-number-id.json"; then
  assert_count "unknown phone number creates no row" "$UNKNOWN_TENANT_ID" "0"
fi

if send_fixture "non-text audio request" "tests/fixtures/non-text-audio.json"; then
  assert_count "audio message stored once" "$AUDIO_ID" "1"
  assert_body "audio placeholder stored" "$AUDIO_ID" "[non-text message: audio]"
fi

verification_status="$(curl --silent --show-error --output /tmp/flow1-verification-body \
  --write-out '%{http_code}' \
  --get \
  --data-urlencode 'hub.mode=subscribe' \
  --data-urlencode "hub.verify_token=${META_VERIFY_TOKEN:-}" \
  --data-urlencode 'hub.challenge=flow1-test-challenge' \
  "${WEBHOOK_URL:-http://127.0.0.1:5678/webhook/meta/whatsapp}" 2>/dev/null)" || verification_status="000"
verification_body="$(cat /tmp/flow1-verification-body 2>/dev/null || true)"
rm -f /tmp/flow1-verification-body
if [[ "$verification_status" == "200" && "$verification_body" == "flow1-test-challenge" ]]; then
  echo "PASS Meta GET verification challenge"
else
  echo "FAIL Meta GET verification challenge (HTTP $verification_status)" >&2
  failures=$((failures + 1))
fi

if [[ "$failures" -ne 0 ]]; then
  echo "Flow 1 tests failed: $failures failure(s)." >&2
  exit 1
fi

echo "All Flow 1 tests passed."
