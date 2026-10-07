# Local metrics stack

The optional metrics stack uses official image releases pinned to these image digests:

| Image | Version | Digest |
|---|---|---|
| `prom/prometheus` | `v3.13.3` | `sha256:6976aa8a60fec930796ce5772b8d12da7a318a5daa8d40d69c5c7819a05eeed7` |
| `grafana/grafana` | `13.1.6-slim` | `sha256:89fd8413a4307b5d7a3c9b97b71d85c18d8c49f4e962d518ba0c2f8e7dd347f9` |

These digests were inspected after the images were pulled on the current `linux/amd64` Docker
host. Compose pins each version tag and digest together.

Prometheus retains `400d` by default. Set `PROMETHEUS_RETENTION=7d` for short-lived local
verification. The integration test stack is independent of the Sonos collector and uses an
unreachable target while verifying storage retention.

Run `scripts/metrics.ps1 Up` with `SONOSMONITORING_API_KEY`, `GRAFANA_ADMIN_USER`, and
`GRAFANA_ADMIN_PASSWORD` set in the process environment. The script writes the scrape
configuration and API key file under the ignored `.local/metrics` directory. The API key is
referenced through Prometheus' secret-file HTTP-header support and is never embedded in the
committed configuration or emitted by the script. Set `SONOSMONITORING_METRICS_TARGET` to a
`host:port` target when the API is not at `host.docker.internal:48080`.

Prometheus is exposed on `http://localhost:48090`; Grafana is exposed on
`http://localhost:48300` and requires the configured admin login. Both ports bind to loopback.
The named data volumes survive `Down` and container recreation. `scripts/metrics.ps1 Down`
stops and removes only the `sonosmonitoring-metrics` Compose project, removes the generated
local API-key file, and preserves its data volumes.

The shared `prometheus.yml.template` loads `sonos-alerts.yml`. Prometheus owns these initial
API, collector-heartbeat and player-description alert rules. There is no Alertmanager or
external notification receiver configured. Three recovery rules require prior firing evidence,
current positive observations, the original warning to end, and a one-minute confirmation.
The firing-start recording rule retains the original occurrence identity in the TSDB;
recovery evidence is looked up for at most 24 hours. Inspect rule states in `/alerts` and
use the protected application `/api/alerts` for pending/firing/recovered messages.
Run `scripts/metrics.ps1 Up` after changing Compose mounts; `Restart` recreates containers
with current flags/mounts while retaining named volumes.

Four linked Grafana dashboards cover overview, device details, network counters and
collector/events. They use readable device metadata while retaining stable metric keys.
Dashboard provisioning polls every 30 seconds because Docker bind mounts can omit file
watch notifications. Restart Grafana once after provider configuration changes; subsequent
JSON updates are picked up by polling. Account and data volumes must be retained.
See `docs/metriken.md` for interpretation and `scripts/test-grafana-dashboards.ps1` for the
infrastructure-free catalogue check. `scripts/test-grafana-live.ps1` and the Grafana browser
test use explicitly configured local credentials and validate actual provisioned queries.

`scripts/test-alerts.ps1` checks rule metric names against the catalogue, validates the
production configuration template and runs `sonos-alerts.tests.yml` with the exact image
pinned above. Its separate short-lived Prometheus verifies real rule loading/evaluation;
it scrapes only an unreachable container-loopback target and keeps no persistent volume.
The infrastructure CI gate runs this check. See the German backend documentation and runbook
for timing, suppression, missing-data semantics and open notification acceptance criteria.
