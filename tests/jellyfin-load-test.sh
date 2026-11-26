#!/usr/bin/env bash
# Finishes Jellyfin's first-run wizard, signs in, and checks that the Clearr plugin is loaded and its API works.
set -euo pipefail
JF="$1"
CLIENT='MediaBrowser Client="Clearr CI", Device="CI", DeviceId="clearr-plugin-ci", Version="1.0"'
fail=0
check() { if eval "$2"; then echo "PASS $1"; else echo "FAIL $1"; fail=1; fi; }

# Jellyfin answers before it has finished starting, so wait for a real version and an open wizard.
VER=null
for i in $(seq 1 120); do
  VER=$(curl -s "$JF/System/Info/Public" | jq -r '.Version // empty' 2>/dev/null || true)
  [ -n "$VER" ] && [ "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: $CLIENT" "$JF/Startup/Configuration")" = 200 ] && break
  sleep 2
done
echo "Jellyfin ${VER:-unknown}"
step() { # retry a wizard call; show the response if it never succeeds
  for i in $(seq 1 15); do
    code=$(curl -s -o /tmp/step -w '%{http_code}' -X POST "$JF/$1" -H "Authorization: $CLIENT" -H 'Content-Type: application/json' ${2:+-d "$2"})
    case "$code" in 2*) return 0;; esac; sleep 2
  done
  echo "Wizard step $1 failed: HTTP $code $(head -c 300 /tmp/step)"; exit 1
}
step Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
curl -s "$JF/Startup/User" -H "Authorization: $CLIENT" >/dev/null || true
step Startup/User '{"Name":"admin","Password":"ci-pass-1"}'
step Startup/Complete
for i in $(seq 1 30); do
  TOKEN=$(curl -sf -X POST "$JF/Users/AuthenticateByName" -H "Authorization: $CLIENT" -H 'Content-Type: application/json' -d '{"Username":"admin","Pw":"ci-pass-1"}' | jq -r .AccessToken || true)
  [ -n "${TOKEN:-}" ] && [ "$TOKEN" != "null" ] && break; sleep 2
done
AUTH="$CLIENT, Token=\"$TOKEN\""
get() { curl -s -o /tmp/body -w '%{http_code}' -H "Authorization: $AUTH" "$JF/$1"; }

PLUGIN=$(curl -sf -H "Authorization: $AUTH" "$JF/Plugins" | jq -c '.[] | select(.Name=="Clearr")')
echo "Plugin: $PLUGIN"
check "Plugin loaded and Active" '[ "$(echo "$PLUGIN" | jq -r .Status)" = "Active" ]'
check "Dashboard page registered" 'curl -sf -H "Authorization: $AUTH" "$JF/web/ConfigurationPages" | jq -e ".[] | select(.Name==\"Clearr\")" >/dev/null'
check "GET /Clearr/status" '[ "$(get Clearr/status)" = 200 ] && jq -e ".mode" /tmp/body >/dev/null'
check "GET /Clearr/report (empty library)" '[ "$(get Clearr/report)" = 200 ] && jq -e ".shows" /tmp/body >/dev/null'
check "GET /Clearr/settings" '[ "$(get Clearr/settings)" = 200 ] && jq -e ".settings.mode == \"DryRun\"" /tmp/body >/dev/null'
check "GET /Clearr/users" '[ "$(get Clearr/users)" = 200 ] && jq -e "length >= 1" /tmp/body >/dev/null'
check "Dry run guard: POST /Clearr/cleanup?dryRun=false refused" '[ "$(curl -s -o /tmp/body -w "%{http_code}" -X POST -H "Authorization: $AUTH" "$JF/Clearr/cleanup?dryRun=false")" = 409 ]'
check "Shared UI served" '[ "$(curl -s -o /tmp/body -w "%{http_code}" "$JF/Clearr/ui/clearr-ui.js")" = 200 ] && grep -q ClearrUI /tmp/body'
check "Admin-only API (anonymous refused)" '[ "$(curl -s -o /dev/null -w "%{http_code}" "$JF/Clearr/report")" = 401 ]'
check "Scheduled task registered" 'curl -sf -H "Authorization: $AUTH" "$JF/ScheduledTasks" | jq -e ".[] | select(.Category==\"Clearr\")" >/dev/null'
exit $fail
