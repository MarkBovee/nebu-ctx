set -o pipefail
log=$(mktemp)
if bash tests/local-addon-test.sh >"$log" 2>&1; then
  status=0
else
  status=$?
fi
echo "STATUS=$status"
if [ "$status" -eq 0 ]; then
  if grep -q "Dashboard + MCP add-on smoke passed\." "$log"; then
    echo "OUTCOME=PASS"
  else
    echo "OUTCOME=PASS_NO_SENTINEL"
  fi
  if grep -q "=== Validating dashboard routes ===" "$log"; then
    echo "DASHBOARD_ROOT_VALIDATED=yes"
  else
    echo "DASHBOARD_ROOT_VALIDATED=unknown"
  fi
  if grep -q "=== Validating MCP routes ===" "$log"; then
    echo "MCP_HEALTH_VALIDATED=yes"
  else
    echo "MCP_HEALTH_VALIDATED=unknown"
  fi
else
  echo "OUTCOME=FAIL"
  first=$(grep -m1 -E "Timed out waiting|Auth token was not generated|curl:|Expected .* got|ERROR|Error:|error:" "$log" || true)
  if [ -n "$first" ]; then
    echo "FIRST_FAIL=$first"
  else
    echo "FIRST_FAIL=none_matched"
  fi
fi
echo "---LOG TAIL---"
tail -n 120 "$log"