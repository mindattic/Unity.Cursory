#!/usr/bin/env bash
# UC-T14: polls the bots' logs for BotDriver's "OK:" lines (Assets/Unity.Cursory/Runtime/Bots/BotDriver.cs); fails
# fast on a "FAIL:" line and otherwise fails after the timeout. Run from smoke/, after `docker compose up -d`.
set -euo pipefail
TIMEOUT=${1:-300}
DEADLINE=$((SECONDS + TIMEOUT))

while (( SECONDS < DEADLINE )); do
  LOGS=$(docker compose logs --no-color botA botB 2>/dev/null || true)
  if echo "$LOGS" | grep -q "\[BotDriver:A\] OK:" && echo "$LOGS" | grep -q "\[BotDriver:B\] OK:"; then
    echo "Both bots saw level 2 solved:"
    echo "$LOGS" | grep "\[BotDriver:.\] OK:"
    exit 0
  fi
  if echo "$LOGS" | grep -q "FAIL:"; then
    echo "A bot reported failure:"
    echo "$LOGS" | grep "FAIL:"
    exit 1
  fi
  sleep 3
done

echo "Timed out after ${TIMEOUT}s waiting for both bots to solve level 2."
docker compose logs --no-color
exit 1
