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

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 FIXTURE.json [--bad-signature]" >&2
  exit 2
fi

fixture="$1"
bad_signature="${2:-}"
if [[ ! -f "$fixture" ]]; then
  echo "Fixture not found: $fixture" >&2
  exit 2
fi
if [[ -z "${META_APP_SECRET:-}" || "$META_APP_SECRET" == replace-* ]]; then
  echo "Set META_APP_SECRET in .env before signing requests." >&2
  exit 2
fi
if [[ "$bad_signature" != "" && "$bad_signature" != "--bad-signature" ]]; then
  echo "Unknown option: $bad_signature" >&2
  exit 2
fi

webhook_url="${WEBHOOK_URL:-http://127.0.0.1:5678/webhook/meta/whatsapp}"
signature="$(openssl dgst -sha256 -hmac "$META_APP_SECRET" -hex "$fixture" | awk '{print $NF}')"

if [[ "$bad_signature" == "--bad-signature" ]]; then
  first_nibble="${signature:0:1}"
  if [[ "$first_nibble" == "0" ]]; then
    first_nibble="1"
  else
    first_nibble="0"
  fi
  signature="${first_nibble}${signature:1}"
fi

curl --silent --show-error \
  --output /dev/null \
  --write-out '%{http_code}' \
  --request POST \
  --header 'Content-Type: application/json' \
  --header "X-Hub-Signature-256: sha256=$signature" \
  --data-binary "@$fixture" \
  "$webhook_url"
