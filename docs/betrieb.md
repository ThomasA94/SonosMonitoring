# Backend-Betrieb und Diagnose

Gültig für den in [Backend-Umsetzung](umsetzung-backend.md) beschriebenen Stand.
Der verifizierte Prüfstand und noch offene Betriebsabnahmen stehen in
[planung.md](planung.md#umsetzungsstand-backend).

Für den separaten Webhost: [Start, API-Verbindung, Cookies und LAN-/HTTPS-Konfiguration](umsetzung-frontend.md#start-und-konfiguration).
Die Weboberfläche ist standardmäßig auf `http://127.0.0.1:48082` erreichbar.

## Lokale Bereitstellung

1. SDK 10.0.201 und PowerShell 7.3 oder neuer bereitstellen; privaten Paketfeed mit Leserechten konfigurieren.
2. `dotnet restore SonosMonitoring.slnx --locked-mode`, dann Release-Build.
3. `./scripts/start-backend.ps1 Up`: getrenntes Compose-Projekt `sonosmonitoring-dev`.
   PostgreSQL 48432, RabbitMQ 48672 und MongoDB 48017 werden nur an Loopback veröffentlicht.
4. `./scripts/start-backend.ps1 Projections` und `./scripts/start-backend.ps1 Api`
   in separaten Terminals starten.
5. Collector zunächst deaktiviert lassen, Konfiguration unter `.local/backend.json` prüfen
   und ausschließlich die tatsächlich freigegebenen Zielbereiche eintragen. Änderungen benötigen Neustart.

`SONOSMONITORING_CONFIG` oder `--config /absoluter/pfad.json` wählen eine Host-Konfigurationsdatei.
Umgebungsvariablen und Kommandozeile können deren Werte überschreiben; Doppelunterstriche
bilden Abschnitte ab, etwa `Collector__Enabled=true`. Secrets liegen unter `.local/`
beziehungsweise in der Deployment-Secretverwaltung. Normale Starts dürfen keine neuen
Zugangsdaten für vorhandene Volumes erfinden.

Das Entwicklungssetup ist ein Einzelhost mit Ein-Knoten-Mongo-Replica-Set, keine Hochverfügbarkeit.
Die Testinfrastruktur hat eigene Ports 48433/48673/48018 und den Namen
`sonosmonitoring-tests`. Entwicklung und Tests niemals durch dieselben Volumes konfigurieren.

### Lokaler Portbereich

Seit 30.09.2026 verwenden die lokalen Vorgaben den Bereich `48000–48999`, um
Kollisionen mit länger bestehenden Testanwendungen zu vermeiden (DEC-018).
Containerinterne Dienstports bleiben unverändert; veröffentlicht wird nur auf Loopback.

| Dienst | Hostport | Separater Infrastrukturtest |
|---|---:|---:|
| API | 48080 | dynamisch |
| GENA-Empfang, optional | 48081 | dynamisch |
| Weboberfläche | 48082 | dynamisch |
| Projektionsworker, bei direktem Hoststart | 48083 | dynamisch |
| Prometheus | 48090 | dynamisch |
| Grafana | 48300 | — |
| PostgreSQL | 48432 | 48433 |
| RabbitMQ | 48672 | 48673 |
| MongoDB | 48017 | 48018 |

GENA wird weiterhin nur nach ausdrücklicher Konfiguration an einer aus dem Sonos-Netz
erreichbaren Adresse veröffentlicht. Der Portbereich ist eine lokale Konvention,
keine globale Reservierung; vor Einsatz auf einem anderen Host dessen Belegung prüfen.

Bei bestehenden Installationen die eigenen Werte in `.local/backend.json` und
`.local/projections.json` anpassen: API 5080 → 48080, PostgreSQL 55433 → 48432,
RabbitMQ 55673 → 48672, MongoDB 57018 → 48017. Eine konfigurierte Journal-Verbindung
ebenfalls berücksichtigen. Separate Web-/Metrikeinstellungen verwenden nun API 48080,
Web 48082 und Prometheus 48090. Bereits gespeicherte GENA-Einstellungen bei Bedarf
über die Webseite von 5081 auf 48081 ändern und Callback-Portfreigaben mitziehen.
Der direkt gestartete Projektionsworker verwendet jetzt 48083 statt 5081, damit er
nicht mit GENA kollidiert. Startskripte überschreiben individuelle Hostdateien nicht.
Anschließend `scripts/start-stack.ps1` ausführen; Datenvolumes und Zugangsdaten bleiben
erhalten. Browser-Lesezeichen und gegebenenfalls externe Clients auf die neuen URLs
umstellen. Die Docker-Netzüberschneidung aus SM-002 ist davon unabhängig.

RabbitMQ behält beim Ersetzen seines Containers die bisherige Knotenidentität:
`start-backend.ps1` übernimmt einmalig den vorhandenen Container-Hostnamen als
`SONOSMONITORING_DEV_RABBITMQ_HOSTNAME` in `.local/dev.env`. Neue Installationen
verwenden `sonosmonitoring-dev-rabbitmq`. Fehlt bei vorhandenem Broker-Volume der
alte Container und diese Einstellung, bricht der Start ab; den ursprünglichen Namen
aus der bisherigen Installation wiederherstellen. Ein zufälliger neuer Hostname würde
ein anderes Knotendatenverzeichnis auswählen, obwohl das Volume gleich bleibt
([RabbitMQ-Konfiguration 4.1](https://www.rabbitmq.com/docs/4.1/configure)).
Die lokale Umstellung bewahrt deshalb den vor dem Portwechsel ermittelten Hostnamen.

### Start aus Windows PowerShell

`start-stack.ps1` und `start-backend.ps1` wechseln bei Aufruf aus Windows PowerShell
5.1 beziehungsweise PowerShell vor 7.3 vor allen Startaktionen automatisch zum
installierten `pwsh` auf `PATH`. Aktionen, Zeitlimit und gesetzte Schalter werden
weitergegeben; Fehler im untergeordneten Prozess brechen auch den ursprünglichen
Aufruf ab. Fehlt die erforderliche Version, erscheint eine verständliche Meldung.
Alternativ ausdrücklich starten: `pwsh -NoProfile -File ./scripts/start-stack.ps1`.

Der MongoDB-Initialisierungscode wird über stdin mit `mongosh --file /dev/stdin`
ausgeführt. Dadurch gelangen seine Anführungszeichen unverändert in den Linux-Container,
auch bei `PSNativeCommandArgumentPassing=Legacy`. Der frühere Aufruf über `--eval`
konnte mit `SyntaxError … print(MongoDB` abbrechen, bevor die Replica-Set-Prüfung
ausgeführt wurde. Die Initialisierung eines bereits passenden Replica-Sets bleibt
unverändert ein No-op. Das Infrastruktur-Gate in `verify.ps1 -IncludeInfrastructure`
prüft den Erststart ausdrücklich mit Legacy-Argumentübergabe.

## Vollständiger lokaler Docker-Stack

`./scripts/start-stack.ps1` (oder explizit `Start`) baut den aktuellen Arbeitsbaum und startet
API, Projektionsdienst, Blazor-Frontend, PostgreSQL, RabbitMQ, MongoDB, Prometheus und Grafana.
Jeder reguläre Start führt zuerst `dotnet restore SonosMonitoring.slnx --locked-mode` und
`scripts/publish.ps1 -BuildImages` aus. Erst nach erfolgreichem Build werden Dienste gestartet
und die Anwendungscontainer ersetzt; Restore-/Buildfehler brechen ohne Rückfall auf alte
Images ab. Das baut auch nicht eingecheckte Änderungen, führt aber keinen Git-Pull aus.
Die Voraussetzungen für SDK und privaten Paketfeed gelten entsprechend dem Projekteinstieg.
Der Start benötigt ein vertrauenswürdiges, exportierbares .NET-Entwicklungszertifikat;
falls keines vorhanden ist, einmal `dotnet dev-certs https --trust` ausführen.

Nur `-SkipBuild` überspringt Restore, Publish und Image-Build bewusst. Dann müssen passende
lokale Images bereits vorhanden sein. Der bisherige Schalter `-BuildImages` bleibt kompatibel,
ist aber nicht mehr erforderlich; die Kombination mit `-SkipBuild` ist ungültig.
`Up` und `Down` sind alternative Aktionsnamen für `Start` und `Stop`.
Nach erfolgreicher Bereitschaftsprüfung erscheinen die vier unten aufgeführten URLs zusammen
am Ende der Ausgabe, gefolgt vom Hinweis auf die lokale Zugangsdaten-Datei und den Stop-Befehl.

Das bestehende Compose-Projekt `sonosmonitoring-dev` wird durch `infra/compose.app.yaml`
ergänzt. Das separate Projekt `sonosmonitoring-metrics` behält seine bestehenden Volumes
und tritt über `infra/compose.metrics.docker.yaml` dem Anwendungsnetz bei.
Alle veröffentlichten Ports sind auf Host-Loopback beschränkt:

| Dienst | Adresse | Zugang |
|---|---|---|
| Frontend | https://localhost:48082 | `WebAccessKey` aus `.local/docker/credentials.json` |
| API | http://localhost:48080 | `X-Api-Key`, Wert `ApiKey` aus derselben Datei |
| Prometheus | http://localhost:48090 | Lokaler Zugriff |
| Grafana | http://localhost:48300 | Bestehendes Grafana-Konto |

Bei einer **neuen** Grafana-Datenbank gelten `GrafanaBootstrapUser` und
`GrafanaBootstrapPassword` aus der lokalen Datei. Bei vorhandener Datenbank ändern diese
Werte kein Konto und sind kein Nachweis des bestehenden Passworts. Explizit gesetzte
`GRAFANA_ADMIN_USER`/`GRAFANA_ADMIN_PASSWORD` werden bei der erstmaligen Erstellung der lokalen
Bootstrap-Datei übernommen. Vorhandene Konten und Volumes werden nicht zurückgesetzt.

Die Host-Konfigurationen `.local/backend.json` und `.local/projections.json` bleiben
maßgeblich. Der Helper erzeugt daraus Container-Konfigurationen mit internen Dienstnamen
unter `.local/docker/`. Die API erhält einen stabilen Schlüssel; Web und Prometheus verwenden
denselben Schlüssel serverseitig. Ein bereits in der Backend-Datei gesetzter API-Schlüssel
hat Vorrang. Der separate Webschlüssel, das exportierte Zertifikat und persistente
Data-Protection-Schlüssel liegen ebenfalls unter `.local/docker/`; dieses Verzeichnis
enthält Secrets und darf nicht veröffentlicht werden.

HTTPS endet direkt im Webcontainer, sodass die UI ihren Schutz für nicht lokale Peers
beibehält. Der Docker-Bridge-Peer wird ausdrücklich nicht als vertrauenswürdiges Loopback
behandelt. Nach einer Zertifikatserneuerung den Helper erneut ausführen.
Jeder Start erstellt die drei Anwendungscontainer neu, damit geänderte Einstellungen
übernommen werden. Aktive Browserverbindungen müssen sich anschließend neu verbinden.

Collector und Beobachtungsjournal behalten ihren bisherigen Aktivierungszustand. Das
Journal wird nicht automatisch provisioniert; eine vorhandene lokale PostgreSQL-Verbindung
auf Port 48432 wird beim Kopieren auf den Datenbankcontainer umgestellt. Docker Desktop
ersetzt keine reale SSDP-/GENA-Netzabnahme. Für freigegebenen GENA-Empfang sind eine passende
Callback-Adresse und eine explizite Portfreigabe zusätzlich erforderlich.

Zum Anhalten ohne Entfernen von Daten:

```powershell
./scripts/start-stack.ps1 Stop
```

`Stop` benötigt nur einen erreichbaren Docker-Daemon, keine SDK-/Zertifikatsprüfung und keine
lokalen Konfigurations- oder Zugangsdaten-Dateien. Es wählt Container über die exakten
Compose-Projektlabels `sonosmonitoring-dev` und `sonosmonitoring-metrics`, bestätigt den
gestoppten Zustand und lässt Container sowie Volumes bestehen. Andere Projekte bleiben
unberührt. Wiederholtes Stoppen eines bereits gestoppten Stacks ist erfolgreich.

Prüfstand vom 29.09.2026 auf Windows/Docker Desktop: acht Container gestartet,
API-Readiness erfolgreich, API ohne Schlüssel mit 403 abgewiesen, Projektionsconsumer aktiv
und Prometheus-Scrape `up`. Im echten Browser wurden vertrauenswürdiges HTTPS, Anmeldung,
acht Seiten, sichere Sitzungscookies sowie eine Mobilansicht ohne horizontalen Überlauf
geprüft; keine Browserfehler. Collector und Journal waren dabei deaktiviert. Dieser
Startnachweis ersetzt keine Sonos-Netzabnahme oder einen Command-bis-Query-Integrationstest.

## Schema und Migration

```powershell
dotnet src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll --write-event-store-schema .local/event-store-schema.sql
```

Der Befehl erzeugt das unveränderte Schema aus dem gepinnten Frameworkpaket. Er verbindet sich
nicht mit einer Datenbank. Das Initialisierungsskript verwendet es nur bei einem vollständig
leeren Schema. Der API-Start prüft `event_store_schema` gegen die Framework-Schemaversion 4.
Vorhandene falsche Versionen werden nicht automatisch überschrieben.

Die optionale Mediensteuerung verwendet zusätzlich `sonos_media` Version 1. Der
Release-API-Befehl `--write-media-control-schema <Datei>` schreibt ausschließlich
das Erstinstallations-SQL. Für eine ausdrücklich benannte PostgreSQL-Datenbank:

```powershell
./scripts/initialize-media.ps1 -ContainerName sonosmonitoring-dev-postgres-1 -Database sonosmonitoring_dev -User sonosmonitoring_dev
```

Das Skript lehnt jeden bereits vorhandenen `sonos_media`-Namespace ab; es migriert
oder leert nichts. Im lokalen Entwicklungsstack provisioniert
`scripts/start-backend.ps1 Up` das Schema additiv nach dem Event Store. Ein
vorhandener Namespace wird nur mit erwarteten Tabellen einschließlich
`target_epochs`, Spalten und Version 1
akzeptiert. Die dauerhafte Steuerfreigabe steht nach Erstinstallation auf `false`.
Die Beispielkonfiguration enthält `MediaControl:Enabled=true`, aber eine bereits
vorhandene `.local/backend.json` bleibt unangetastet. Betreiber müssen diese
Einstellung selbst bewusst ergänzen und anschließend unter `/media` getrennt
freigeben. Eine leere `MediaControl:ConnectionString` verwendet die konfigurierte
Event-Store-Datenbank; eine getrennte Datenbank muss dasselbe Schema erhalten.

### Medienfreigabe, unklare Wirkung und Wiederanlauf

Nur nach freigegebener Netz-/Geräteumgebung zuerst `GET /api/media/control`
prüfen: `enabled`, `generation` und `blocks` sind der dauerhafte Stand. Unter
`/media` kann der geschützte Betreiber die eigene Steuerfreigabe aktivieren oder
entziehen. Entzug erzeugt eine neue Generation und widerruft wartende Vorgänge;
spätere erneute Aktivierung belebt sie nicht. Der technische Deployment-Schalter
und die Betreiberfreigabe sind getrennte Voraussetzungen. Ein API-/Worker-Neustart
übernimmt den gespeicherten Stand, führt bereits freigegebene schreibende Schritte
aber nicht blind erneut aus. Für einen Executor ist eine PostgreSQL-Session mit
Advisory Lock vorgesehen; Sonos-Netzaufrufe laufen außerhalb der kurzen
Koordinationstransaktionen.

Bei `media.outcome.unknown` oder `media.target.blocked` zuerst
`GET /api/media/operations/{uuid}` und `GET /api/media/control` lesen. Den
Vorgang, alle `deviceKeys`, Aktion und Wert aus `blocks` dokumentieren. Eine alte
Wirkung kann noch eintreffen; ein frischer Geräte-Read oder ein passend wirkender
Zustand ist keine Entsperrung. Die Webansicht hält betroffene Aktionen gesperrt
und nennt UUIDs, Status und ursprünglichen Wert. Ein `dispatch_started`-Vorgang
kann dort nicht entsperrt werden; erst `outcome_unknown` ist für Risikoannahme
vorgesehen. Nur nach bewusster Prüfung über `/media` die konkrete
Risikoannahme mit `confirmDelayedEffect=true` und genau diesen
`operationIds` auslösen. Der API-Code `media.risk.not_uncertain` bedeutet, dass
die benannten Sperren nicht mehr genau zum dauerhaften Stand passen; neu lesen
und nicht mit einer erweiterten oder still veränderten Liste wiederholen.
Antwortverlust bei einer Betreiberentscheidung verlangt einen ausdrücklichen
Retry mit derselben UUID und demselben Body. Vor erneutem Betrieb auch Queue,
Gruppen und mögliche Teilwirkung am tatsächlichen Gerät prüfen; einen alten
Schreibvorgang niemals über Event-/Outbox-Replay erneut ausführen.

Für Upgrades die zur neuen Paketkombination gehörende Migration, vorherige Sicherung und
Probe-Restore verwenden. Schema-Erstellung ist kein Upgrade. Ein Datenreset ist kein allgemeiner
Reparaturschritt und ist für erhaltenswerte Daten nicht freigegeben.

## Linux-Container und LXC

Nach dem Image-Build prüft `scripts/scan-images.ps1` alle drei Anwendungsimages mit Trivy 0.74.0
(Scanner per Digest fixiert). Der Scanner liest exportierte Image-Dateien und erhält keinen
Docker-Socket. HIGH-/CRITICAL-Befunde lassen den Prüfablauf fehlschlagen; JSON-Berichte liegen
unter `artifacts/security/`. Der Scanner lädt aktuelle Schwachstellendaten; kein Befund bedeutet
nur, dass die verwendeten Datenbanken keine passenden Meldungen geliefert haben.
Das Zeitbudget je Image beträgt 15 Minuten und kann über `-Timeout 20m` angepasst werden.
Ein Zeitablauf ist eine fehlgeschlagene Prüfung; vorhandene ältere Berichte sind kein
Nachweis für die neu gebauten Images.

`./scripts/publish.ps1 -BuildImages` veröffentlicht API, Projektionsworker und Webhost und baut separate
Runtime-Images. Restore findet auf dem Buildrechner statt; private Feed-Tokens gelangen
nicht in Runtime-Layer. Die Dockerfiles verwenden ein per Digest fixiertes .NET-10.0.12-Image
und dessen unprivilegierten Benutzer `app`.
Der Scan vom 30.09.2026 meldete CVE-2026-84782 für `openssl` und `libssl3t64`
in Version `3.0.13-0ubuntu3.15`. Die Dockerfiles installieren deshalb vor dem
Benutzerwechsel gezielt beide Ubuntu-Pakete in `3.0.13-0ubuntu3.16`; Paketlisten
werden aus dem Layer entfernt. Der Paketfix wurde über die Ubuntu-Repositories
beim Image-Build tatsächlich aufgelöst. Diese Ergänzung bleibt erforderlich,
bis ein neu festgelegter Upstream-Digest den Fix enthält und alle drei Images
erneut erfolgreich geprüft wurden. Eine Änderung am Basisimage allein ist kein
Scan-Nachweis; maßgeblicher Abschluss bei SM-001/018 in [planung.md](planung.md#erweiterte-metrik--und-zustandsanzeige-30092026).
`scripts/smoke-containers.ps1` prüft die gebauten Images gegen eine bereits gestartete
Testinfrastruktur (`scripts/test-infrastructure.ps1 Up`). Die API muss mit Schlüssel 200
und ohne Schlüssel 403 liefern; der Worker muss starten und eine eigene RabbitMQ-Queue
anlegen. Beide Images verwenden `app` und enthalten kein SDK. Der Test verwendet eigene
kurzlebige Container und eine eigene Projektionsdatenbank. Danach entfernt
`scripts/test-infrastructure.ps1 Down` die gesamte entbehrliche Testumgebung.

Das API-Image bindet im Container an Port 8080. Den Hostport ausdrücklich an Loopback binden,
einen extern injizierten `Access__ApiKey` setzen und Konfigurationsdateien read-only einhängen.
Die PostgreSQL-/RabbitMQ-/Mongo-Ziele müssen aus dem Container erreichbar sein;
`127.0.0.1` bezeichnet dort den Container selbst. Produktive Verbindungen und Secrets
nicht blind aus der Windows-Entwicklungskonfiguration übernehmen.

LXC verwendet die gleichen veröffentlichten DLLs mit .NET-10-ASP.NET-Runtime, einem
eigenen eingeschränkten Benutzer und systemd. Die Dienstvorlagen stehen unter
`infra/systemd/sonosmonitoring-{api,projections}.service`. Den Systembenutzer
`sonosmonitoring` ohne Login anlegen, Publish-Ausgaben nach `/opt/sonosmonitoring/api`
beziehungsweise `/opt/sonosmonitoring/projections` übertragen und als root verwalten.
Konfigurationen unter `/etc/sonosmonitoring/{backend,projections}.json` nur root und der
Dienstgruppe lesbar machen (Verzeichnis 0750, Dateien 0640). Dienste erst nach Einrichtung
von PostgreSQL-Schemata, Broker und Mongo-Replica-Set installieren/aktivieren.
API und Worker bekommen getrennte Dienste mit geordnetem SIGTERM-Shutdown,
Restart-on-failure und extern verwalteten Zugangsdaten. Es wurde kein Proxmox-Host verändert;
Netzwerk, Distribution, Architektur und systemd-Härtung müssen am tatsächlichen Ziel geprüft werden.

## Metrikbetrieb und Aufbewahrung

`scripts/metrics.ps1 Up` verlangt `SONOSMONITORING_API_KEY`,
`GRAFANA_ADMIN_USER` und `GRAFANA_ADMIN_PASSWORD`. Den gleichen API-Key im API-Host
als `Access__ApiKey` setzen. `SONOSMONITORING_METRICS_TARGET` wählt den erreichbaren
Exporter als host:port; Docker unter Linux benötigt dafür ein ausdrücklich erreichbares Ziel.

`PROMETHEUS_RETENTION=7d` für Tests, beispielsweise `400d` für den regulären Betrieb.
Die Laufzeiteinstellung ist unter `/api/v1/status/flags` prüfbar; Prometheus kann sieben Tage
als `1w` normalisieren. Nach Änderungen der Containerargumente `Up` zur Neuerstellung
verwenden; ein bloßes Docker-Restart übernimmt keine geänderten Argumente.

Prometheus ist lokal auf 48090, Grafana auf 48300 erreichbar. Grafana verlangt Anmeldung.
Die provisionierte Datenquelle und vier detaillierte Dashboards kommen aus `infra/grafana/`.
Die [Dashboardanleitung](metriken.md#10-detaillierte-grafana-dashboards-und-lesbare-gerätenamen)
erklärt Übersicht, Gerätedetails, Netzwerk und Collector/Ereignisse. Auswahl, Tabellen
und Legenden zeigen Raum-/Gerätenamen; der stabile pseudonyme Schlüssel bleibt intern.
Der Test prüfte Retentionskonfiguration und Neustart, nicht sieben Tage tatsächliche Löschung
oder einjähriges Wachstum. Eine erreichbare TSDB garantiert keine erfolgreichen Scrapes:
`up`, letzte Erfolgszeit und Collector-Heartbeat separat kontrollieren.

Eine aktive globale Erfassung aktiviert noch keine Gerätediagnose. Bei fehlender
Gerätepolicy sind Interface-, WLAN-, Ethernet- und Wiedergabediagnosen absichtlich aus.
In der Geräteseite die Überwachung aktivieren und die bestätigte Projektion abwarten.
Das wirksame Intervall ist mindestens das globale Pollingintervall. Nach der nächsten
Geräteprobe sind Rohwerte verfügbar, nach zwei erfolgreichen Proben Live-Raten und
nach zusätzlichen Prometheus-Scrapes historische Raten. Fehlende Werte in diesem
Anlauf sind keine Nullmessung. Bei alten Werten Quellenstatus und Erfolgszeit prüfen.
Detailquellen veralten nach mindestens drei effektiven Intervallen.

Neue Dashboardpanels werden durch `start-stack.ps1` mit dem provisionierten Dashboard
ausgeliefert. Grafana liest Dashboarddateien alle 30 Sekunden ein; bei Docker-Bind-Mounts
sind Dateibenachrichtigungen nicht immer verfügbar. Bei erstmaliger Übernahme der
geänderten Providerkonfiguration Grafana neu starten, danach genügen JSON-Änderungen.
Grafana-Konto und Datenvolume dabei erhalten. Details zur
[Polling-Provisionierung](https://grafana.com/docs/grafana/latest/administration/provisioning/#detect-updates-to-provisioned-dashboards-files).
Für einen Datenvergleich dieselbe pseudonymisierte Gerätekennung,
Interface-/Quellenauswahl, Zeitraum und Schrittweite verwenden. Historische
Prometheusraten mitteln über zwei Minuten; die letzte Live-Rate ist eine eigene
Erfassungsdifferenz. Funk-/PHY-Diagnosen werden nicht periodisch abgefragt; Meshpfade
und RSSI/SNR bleiben ohne belastbaren Quellenvertrag offen.

Der Export enthält mit `sonos_device_info` jetzt auch Raum-/Modellnamen und Firmware
als eigene Metadatenreihe. Diese Angaben sind für berechtigte Prometheus-/Grafana-Nutzer
sichtbar; das ist kein anonymisierter Diagnoseexport. Metrikendpunkt und TSDB-Zugriff
entsprechend begrenzen. Eine Namensänderung ändert ausschließlich die Metadatenreihe,
nicht die Zähleridentität. WLAN-/Wiedergabe-Info wird nur bei frischer erfolgreicher
Quelle ausgegeben. Namen aus einer alten Beschreibung sind kein Erreichbarkeitsbeleg.

Die reguläre Prüfung führt `scripts/test-grafana-dashboards.ps1` ohne Infrastruktur aus.
Nach Auslieferung im lokalen Stack können zusätzlich `scripts/test-grafana-live.ps1`
und `npm run test:grafana --prefix tests/browser` laufen. Dafür `GRAFANA_ADMIN_USER`
und `GRAFANA_ADMIN_PASSWORD` ausschließlich in der Prozessumgebung bereitstellen;
vorhandene Grafana-Konten verwenden. Beide Prüfungen lesen nur Loopback-Grafana und
ändern weder Gerätediagnosen noch SSDP-/GENA-Einstellungen. Prüfberichte und aktuelle
Browserscreenshots liegen unter `artifacts/test-results/`; Screenshots können Raumnamen
enthalten und werden nicht als reduzierter Export behandelt.

SSDP-/GENA-Bedeutung, Einrichtung und Erfolgskontrolle stehen in der
[Netzanleitung](discovery.md#ssdp-und-gena-im-alltag-sm-002-sm-016-sm-019)
und direkt unter **Einstellungen**. Im aktuellen lokalen Stack bleiben beide optionalen
Protokolle deaktiviert; erfolgreiche HTTP-Erfassung beweist den GENA-Rückweg nicht.

## Technische Störungserkennung

Der optionale Metrikstack lädt die [versionierten Regeln](umsetzung-backend.md#störungserkennung-sm-005-sm-011-sm-019).
Nach einem Update `scripts/metrics.ps1 Up` ausführen; vorhandene Metrikvolumes bleiben erhalten.
`Restart` übernimmt ebenfalls geänderte Mounts und Flags durch Neuerstellung der Container.
Die erforderlichen Umgebungsvariablen wie beim normalen Start setzen.

1. In Prometheus `http://localhost:48090/alerts` nach `SonosApiUnavailable`,
   `SonosCollectorInterrupted` und `SonosDeviceDescriptionUnavailable` sehen.
2. Unter `/api/v1/rules` prüfen, dass die Gruppe `sonosmonitoring` vier Regeln geladen hat
   (eine Recording Rule und drei Alarme), `health=ok` meldet und aktuell auswertet.
3. Bei API-Ausfall zuerst API-Prozess, Zieladresse, Schlüssel und Scrape-Verbindung prüfen.
   Bei Collector-Unterbrechung Aktivierung, Heartbeat und das konfigurierte Rundentiming prüfen.
4. Einen Gerätealarm anhand der pseudonymen `device_id`, der eigenen Collector-Ansicht und
   `description.lastAttemptUtc/lastSuccessUtc/errorCode` zuordnen. Die Regel meldet keinen
   gesicherten Hardware- oder Audioausfall. Rohkennungen werden nicht zu Alarm-Labels ergänzt.
5. Bei erloschenem Alarm prüfen, ob tatsächlich eine erfolgreiche neue Beobachtung vorliegt;
   verlorene Serien oder deaktivierte Erfassung können ebenfalls zum Erlöschen führen.

Wartungs-/Schlafausnahmen bleiben offen. Der gewählte Kanal ist die interne Alarm-API; es werden keine externen Nachrichten
versendet. Vor Aktivierung eines späteren Kanals Schwellen am Bestand kalibrieren und getrennte
Nachweise für Wiederholung, Deduplizierung und Erholungsmeldungen erbringen. Den Prometheus-Ausfall
aus einer unabhängigen Betriebsüberwachung erkennen; seine eigenen Regeln reichen dafür nicht.

Isolierter Nachweis ohne lokale Entwicklungsdienste oder reale Geräte:

```powershell
./scripts/test-alerts.ps1
```

Das Skript prüft Metriknamen gegen den Katalog, die produktive Konfigurationsvorlage und
Zeitsequenzen mit `promtool` aus dem gepinnten Image. Danach lädt ein kurzlebiger Prometheus
dieselbe Regeldatei und wertet sie aus. Sein einziges Scrape-Ziel ist `127.0.0.1:1` im eigenen
Container; HTTP-Zugriff von außen ist auf einen kurzlebigen Loopback-Port begrenzt.
Der Container wird auch bei Fehlern entfernt, es werden keine bestehenden Datenvolumes benutzt.
Belege: `artifacts/test-results/alerts-promtool.log` und `alerts-runtime.json`.
Der vollständige CI-Prüfablauf mit `-IncludeInfrastructure` führt diese Prüfung verpflichtend aus.

## Diagnose nach Schicht

| Symptom | Zuerst prüfen |
|---|---|
| API 403 | Tatsächlicher Socketpeer, API-Key und Bindings; Forwarded-Header helfen absichtlich nicht |
| API startet nicht | Pflichtkonfiguration, Schema 4, DB-Verbindung und Exceptions im geschützten Log |
| Geräte fehlen | Collector aktiviert, erlaubte CIDRs, Routing TCP 1400, Grenzen/Scanstatus |
| Topologiemitglied wird nicht direkt erkannt | `reportedAddress`, CIDR-Hostgrenzen und Quellenqualität des Reporters prüfen; fehlgeschlagene/veraltete Reporter liefern keine Hinweise. `topologyDiscovery` zeigt geplante Kandidaten/Limit; Fehlversuche unterliegen Backoff. Die tatsächliche Gerätebeschreibung muss erfolgreich sein. |
| SSDP findet keine Kandidaten | Explizite lokale `SsdpInterfaceAddresses`, Interface-Zuweisung und `ssdp`-Suchstatus prüfen. Ausgehend UDP 1900 an `239.255.255.250`, Unicast-Antworten an den kurzlebigen Quellport erlauben. TTL 1 bleibt im lokalen Segment; geroutete CIDRs werden weiter per HTTP durchsucht. |
| SSDP teilweise oder begrenzt | `failedInterfaces`, `limitReached` und `errorCode` prüfen; ungültige/zu große Datagramme zählen ebenfalls zum Empfangslimit. Unicast-Suche bleibt aktiv. Nach Interface-/Konfigurationsänderung Dienst neu starten. |
| Alte Werte | Quellenzeit/Fehlercode, Heartbeat und letzter abgeschlossener Suchlauf |
| Gruppen fehlen oder sind alt | `topologyStatus` je berichtendem Gerät, `lastSuccessUtc`, Fehlercode und Backoff; `ifconfig` kann unabhängig weiter funktionieren. SOAP auf TCP 1400 erforderlich; kein GENA-Rückweg nötig. |
| Policy 202 pending | Receipt-EventSequence gegen Mongo eventSequence; Outbox-/Workerzustand |
| Command bestätigt, Anzeige alt | Event/Outbox dauerhaft vorhanden, Publishstatus, Rabbit-Bindings und Worker-Datenbank |
| History 503 | Prometheus-URL, Erreichbarkeit/Query-Budget; Collector läuft unabhängig weiter |
| Metriklücke | Scrape-`up`, Collector-Unterbrechung, Quelle fehlgeschlagen oder bewusst deaktivierte Policy |

Liveness misst nur den Prozess. API-Readiness prüft bei aktiviertem Backend PostgreSQL
und MongoDB mit engem Budget. Ein Brokerausfall verhindert nicht automatisch einen
dauerhaften Commit; der Rückstau muss zusätzlich beobachtet werden.
Healthchecks sind kein Nachweis einer korrekten Projektion.

Logs sind stdout/stderr, enthalten keine Kennwörter. Gerätekennung und lokale Adressen
können Diagnosekontext sein; Zugriffe und Aufbewahrung entsprechend begrenzen.
Keine Rohantworten mit vollständigen SSIDs/MACs/Titeln als Dauerlogs sammeln.

## Diagnoseexport sichern und offline lesen

Der [Exportvertrag](umsetzung-backend.md#snapshot-und-diagnoseexport-sm-006) beschreibt
Schemafassung, reduzierte Kennungen und die erhaltene Quellenqualität. Im lokalen Modus:

```powershell
Invoke-WebRequest http://127.0.0.1:48080/api/collection/export -OutFile .local/snapshot.json
```

Bei konfiguriertem API-Key zusätzlich `-Headers @{ 'X-Api-Key' = $env:SONOS_API_KEY }`
verwenden; den Schlüssel vorher über die lokale Secretverwaltung bereitstellen. Der Export
enthält keine Konfigurationsdateien, Zugangsdaten, Rohantworten oder Logs. Er schreibt selbst
keine Datei auf dem Server und löst keine neue Geräteabfrage aus.

Die gespeicherte Datei lässt sich nach Stoppen der API beziehungsweise auf einem Rechner ohne
Collector mit PowerShell lesen. Format und Version vor der Auswertung prüfen:

```powershell
$snapshotDocument = Get-Content -Raw .local/snapshot.json | ConvertFrom-Json
if ($snapshotDocument.format -cne 'sonosmonitoring.collection-snapshot' -or $snapshotDocument.schemaVersion -ne 1) {
    throw 'Nicht unterstütztes Snapshot-Format.'
}
$snapshotDocument.capturedAtUtc
$snapshotDocument.snapshot.devices | Select-Object deviceId, roomName, lastSeenUtc,
    @{ Name = 'topologyStaleAtCapture'; Expression = { $_.topologyStatus.isStale } },
    @{ Name = 'topologyError'; Expression = { $_.topologyStatus.errorCode } }
$snapshotDocument.snapshot.devices.groups | ConvertTo-Json -Depth 8
```

Die Statusflags sind auf den Aufnahmezeitpunkt eingefroren; für das Alter einer Beobachtung
ihren eigenen `lastSuccessUtc` heranziehen. Eine Datei ist weder eine aktuelle Liveansicht noch
eine atomare Aufnahme aller Geräte. Aliase niemals als IP-Adressen oder streamfähige Geräte-IDs
verwenden und nicht über mehrere Dateien hinweg zur Identifikation verbinden.
Zeitstempel, Messwerte und Beziehungen bleiben potenziell sensibel; Datei vor Weitergabe prüfen
und nach dem Diagnosebedarf löschen. Keine automatische Aufbewahrung oder Übermittlung.
Der Export ersetzt keine Sicherung der Events, Policies oder Metrikhistorie und ist kein Restoreformat.

## Wiederzustellung und Rebuild

Ein bestätigter Commit enthält Outboxzeilen. Bei vorübergehend fehlendem Broker/Binding liefert
der Dispatcher begrenzt erneut. Terminale `Failed`-Zeilen erfordern eine gezielte Prüfung;
eine beliebige Wiederholung ist kein unbegrenzter Retryvertrag.

Framework-Replay verwendet die ursprünglichen Envelope-Bytes bereits veröffentlichter
Outboxzeilen. Neue Event-IDs, Zeitstempel oder Trace-Metadaten zu erzeugen würde den
Deduplizierungsvertrag verletzen. Der isolierte Replay-Test demonstriert den unterstützten
`PostgresResendRequestStore`/`ResendProcessor`-Weg mit Grund und Rate-Limit.

Für einen vollständigen Raw-/Merged-Wiederaufbau eine **neue**, isolierte Mongo-Datenbank
und neue Worker-Queue-/Deploymentnamen verwenden, erhaltene Events vollständig in Reihenfolge
wieder zustellen und DeviceKey/Enabled/Intervall sowie EventSequence mit dem alten Stand
vergleichen. Erst nach Prüfung die Abfragequelle kontrolliert umstellen.
Bestehende Live-Collections oder Checkpoints nicht als vermeintliche Reparatur löschen.
Ein Rebuild stellt keine während Collector-Ausfällen unbeobachteten Messwerte her.

Noch keine produktive Wartungs-HTTP-API ausgeliefert. Replays dürfen nicht allein durch
einen anonymen API-Aufruf auslösbar sein. Vor Wiederholung auf gemeinsam genutzten Exchanges
alle übrigen Consumer und ihre Idempotenz berücksichtigen.

## Sicherung und Restore

Vor erhaltenswertem Dauerbetrieb die folgenden Daten sichern. Isolierte technische
Prüfungen ersetzen keinen Probe-Restore der tatsächlichen Betriebsdaten:

- PostgreSQL einschließlich Events, Commit-/Head-Zustand, Snapshots, Outbox und Schema-Version
  konsistent sichern; Restore in einer neuen Datenbank prüfen.
- Prometheus-TSDB über dessen unterstützten Snapshot-/Sicherungsweg konsistent kopieren.
  Live-Dateikopien bei laufenden Writes sind keine belegte Sicherung.
- Mongo kann aus vollständiger Historie neu aufgebaut werden; verwendete Konfiguration,
  Workeridentität und Prüfbelege trotzdem sichern.
- Externe Konfiguration und Zugangsdaten separat geschützt sichern.
- Wiederherstellung gegen bekannte Policyzustände, Commit- und Projektionspositionen sowie
  historische Metrikbereiche prüfen, bevor die alte Umgebung abgelöst wird.

`docker compose down -v` ist **kein** Betriebsbefehl für Entwicklungs-/Produktionsdaten.
Nur das explizite Testskript entfernt seine entbehrlichen Testvolumes.

### PostgreSQL-Werkzeuge

`backup-postgres.ps1` erzeugt mit `pg_dump -Fc` eine konsistente Sicherung einschließlich
aller Anwendungsschemata. Die Binärdatei wird mit `docker cp` übertragen; vorhandene
Zieldateien werden abgelehnt. Lokale Container-Authentifizierung verwenden, keine
Kennwörter als Kommandozeilenargumente. Beispiel mit bewusst benannten Betriebsressourcen:

```powershell
./scripts/backup-postgres.ps1 -ContainerName sonos-postgres -Database sonos -User sonos -OutputPath /backup/sonos-20260929.dump
./scripts/restore-postgres.ps1 -ContainerName sonos-postgres -Database sonos_restore_20260929 -User sonos -BackupPath /backup/sonos-20260929.dump
```

Die Datenbanknamen im Beispiel müssen zur eigenen Umgebung passen. Restore legt nur eine
nachweislich fehlende neue Datenbank an; niemals `--clean` oder Überschreiben. Eine teilweise
wiederhergestellte neue Datenbank bleibt bei Fehlern zur Diagnose erhalten. Owner und Grants
werden bewusst nicht übernommen; die Zielrolle besitzt die Objekte, weitere Berechtigungen
müssen beim kontrollierten Cutover eingerichtet werden. Rückstau der gesicherten Outbox kann
nach dem Restore erneut zugestellt werden; Idempotenz und isolierte Rebuild-Ziele beachten.
Enthält die neue Zieldatenbank `sonos_media`, setzt das Restore-Skript in einer
Transaktion die Medienfreigabe auf `false` und erzeugt eine neue Generation.
Es markiert `dispatch_started` und `device_acknowledged` als `outcome_unknown`,
widerruft noch `accepted`-Vorgänge und setzt auch zuvor freigegebene unklare
Risikosperren vorsichtshalber wieder fest. Fehlende oder inkompatible Version 1
stoppt den Restoreablauf vor dem Cutover; die neue Datenbank bleibt zur Prüfung
erhalten. Ein Event-/Projektions-Replay darf niemals alte Lautsprecherbefehle
ausführen. Vor erneuter Steuerfreigabe die benannten unklaren Vorgänge prüfen,
betroffene Räume dokumentieren und eine mögliche verspätete Altwirkung nur über
die ausdrückliche Risikoannahme freigeben.
Konfiguration/Secrets separat sichern. SHA256-Ausgabe zusammen mit Zeitpunkt, Release,
Datenbank-/Schemaversion und Speicherort protokollieren. Backups verschlüsselt aufbewahren.

Der unabhängige Werkzeugtest `scripts/test-backup.ps1` verwendet ausschließlich einen eigenen
kurzlebigen PostgreSQL-Container. Er prüft Daten, JSONB, Sequenzen, Constraints und Ablehnung
vorhandener Ziele. Der Integrationstest für Monitoring-Restore sichert echte Policy-Events,
Outbox und Beobachtungsjournal. Nach Wiederherstellung in eine neue Datenbank prüft er dieselbe
idempotente Aktion ohne Duplikat, einen weiteren Commit und die fortgesetzte Historisierung
mit Neustartlücke. Die Quelldatenbank bleibt unverändert. Aktueller Prüfstand in `planung.md`.

## Prometheus-Sicherung und Wiederherstellung

`backup-prometheus.ps1` benötigt eine ausdrücklich freigegebene, ausschließlich an Loopback
veröffentlichte Admin-API (`--web.enable-admin-api`) desselben benannten Containers. Die
Standard-Compose-Datei aktiviert sie absichtlich nicht. Für eine Sicherung die eigene
Betriebskonfiguration kontrolliert um dieses Argument ergänzen und den Container mit seinem
bisherigen Volume neu erstellen; das Skript selbst verändert weder Startargumente noch Dienste.
Die API ermöglicht auch Löschoperationen und darf nicht ins LAN veröffentlicht werden.

```powershell
./scripts/backup-prometheus.ps1 -ContainerName sonosmonitoring-metrics-prometheus-1 -AdminUrl http://127.0.0.1:48090 -OutputDirectory /backup/metrics-20260929
./scripts/restore-prometheus-volume.ps1 -BackupDirectory /backup/metrics-20260929 -VolumeName sonosmonitoring-metrics-restore-20260929
```

Die Sicherung schließt den Head ein und kopiert ausschließlich den soeben erzeugten Snapshot.
Nach bestätigter Kopie entfernt sie genau diesen serverseitigen Snapshot; bei Fehlern wird ein
verbliebener Snapshot zur Prüfung benannt. Das Zielverzeichnis muss fehlen. Restore akzeptiert
nur ein neues Volume, beweist dessen Eigentümerschaft über ein eindeutiges Label und verwendet
einen kurzlebigen Kopierprozess aus dem gepinnten Prometheus-Image. UID 65534 erhält die Daten;
der anschließend separat gestartete Prometheus läuft unprivilegiert. Ein fehlgeschlagenes neues
Volume bleibt zur Diagnose erhalten. Niemals ein Live-Volume als Restoreziel verwenden.

Vor Umschalten dieselbe gespeicherte Query mit identischem Zeitraum und Schritt ausführen und
Werte/Labels vergleichen. Der isolierte Test `test-prometheus-backup.ps1` führt dies gegen einen
zweiten Prometheus tatsächlich aus. Er beweist keine einjährige Kapazität. Für den Betrieb
`prometheus_tsdb_storage_blocks_bytes`, Plattenreserve, WAL und `pg_total_relation_size` der
Journal-/Eventtabellen messen; diese Werte plus Ereignisrate bestimmen die benötigte Kapazität.
Journal-MaxEntries begrenzt Zeilen, nicht WAL, Backups oder sämtliche PostgreSQL-Dateigrößen.

## Ereignisrückweg betreiben

GENA braucht vom Gerät zur konfigurierten Callback-Adresse eingehendes TCP auf `Gena:Port`.
Den Listener nur für die freigegebenen Gerätenetze erreichbar machen; er besitzt keine
allgemeinen API-Endpunkte und benutzt keine API-Keys, die Sonos nicht mitsenden kann.
Bei Docker muss die Callback-Portweiterleitung zur tatsächlichen Listenerbindung passen;
Container-interne Adressen nicht als vom Gerät erreichbare Adresse voraussetzen. Reverse
Proxies mit geändertem Socketpeer sind für diesen Listener nicht unterstützt.
Beim Containerbetrieb den Callback-Port zusätzlich explizit veröffentlichen, beispielsweise
`--publish <Host-LAN-IP>:48081:48081`, und dieselbe erreichbare Hostadresse als `CallbackAddress`
setzen. Der konfigurierte Port gilt für Listener und Callback-URL; abweichende externe
Portübersetzung ist nicht vorgesehen. `EXPOSE` im Image allein veröffentlicht keinen Port.

`gena.subscribed=true` allein beweist keinen Rückweg. Dazu muss
`initialNotificationReceived=true` vorliegen. `callback_unconfirmed` bedeutet eingeschränkten
Ereignisempfang bei weiterhin aktivem Polling. Nach Freigabe des Rückwegs wird eine neue Lease
automatisch versucht. `identity_unconfirmed` verweist zuerst auf die Gerätebeschreibung;
`listener_unavailable` auf Portbindung und lokale Firewall. Callback-Pfade und SIDs nicht
loggen oder in Diagnoseexports übernehmen. Reale Firmware- und LXC-Netzabnahme bleibt nötig.

## Änderungsverlauf bereitstellen und prüfen

Nach Release-Build das separate Schema explizit erstellen:

```powershell
./scripts/initialize-history.ps1 -ContainerName sonos-postgres -Database sonos -User sonos
```

Danach `History:Enabled=true`, die geschützte PostgreSQL-Verbindung sowie Retention/Sampling
in der Backend-Konfiguration setzen und API neu starten. Schema 1 wird nur geprüft;
erneutes Initialisieren ist keine Migration. Pro Datenbank genau einen Collector schreiben
lassen. Für Auswertungen kann die API auch mit deaktiviertem Collector laufen.

`/api/collection/history` muss eine aktuelle `lastRecordedAtUtc` zeigen. Bei älterer Zeit
PostgreSQL-Erreichbarkeit, Platz und Berechtigungen prüfen; laufende Geräteerfassung ist
kein Beweis für laufende Historisierung. Nach Wiederverbindung wird eine Speicherlücke
vermerkt. Im Journal wird nur der aktuelle Zustand nachgetragen, kein unbeobachteter
Zwischenverlauf rekonstruiert. Retention greift während erfolgreicher Schreibzyklen;
bei gestopptem Writer bleibt der letzte Stand bis zum nächsten Zyklus erhalten.
Journal und Checkpoints sind nicht aus Domain-Events rekonstruierbar und gehören deshalb
in die PostgreSQL-Sicherung. Für wiederhergestellte Journalstände neue Cursor ab 0 lesen;
alte Clientcursor können zu einer inzwischen anderen Datenbankhistorie gehören.

## Interne Meldungen abrufen

`GET /api/alerts` liefert ausschließlich intern abfragbare aktuelle Meldungen. Der Nutzer
hat keinen externen Kanal gewählt. API-Key/Loopback-Schutz gelten unverändert. 502 bedeutet,
dass Regelgesundheit oder Aktualität nicht bestätigt ist; eine solche Antwort niemals als
leere Entwarnung behandeln. Clients können stabile `identity`-Werte wiederholter Antworten
zusammenführen und `deviceLabel` über das Livefeld `metricLabel` einem Gerät zuordnen.
Für genau eine Anzeige je Störung/Übergang nach `(occurrenceIdentity, state)` deduplizieren.
`pending` ist eine laufende Vorprüfung, keine bestätigte Störungsbenachrichtigung; aktive
Benachrichtigungen erst für `firing` und `recovered` auslösen.
Nur `state=recovered` ist der explizite positive Nachweis der benannten technischen Quelle;
ein verschwundener Alarm allein ist keine Erholung. Die positiven Erholungsregeln benötigen
eine Minute Bestätigung und tatsächliche Firing-Evidenz innerhalb der letzten 24 Stunden.
Kein dauerhaftes Postfach: innerhalb des Fensters abfragen, Fehler und Beobachtungslücken
sichtbar behandeln. Eine neue Prometheus-Installation kann keine frühere Störung rekonstruieren.
API und Regeldatei gemeinsam ausliefern: Der Reader erwartet genau die acht Regeln des aktuellen
Releases. Ein gemischter Regel-/API-Stand ergibt 502 statt einer unvollständigen Meldungsliste.

## Erfassungseinstellungen sichern und wiederherstellen (SM-021)

Die normale Bedienung erfolgt unter **Einstellungen** in der Webseite. Die API persistiert
die technische Collector-Konfiguration am CollectionSettings:Path; Standard ist
.local/collector-settings.json im ContentRoot. Im vollständigen Docker-Stack ist dies
/app/collector-data/settings.json im eigenen persistenten Volume collector-settings.
Der API-Container läuft weiterhin als Benutzer app; das Zielverzeichnis gehört diesem Benutzer.
Das Volume bei Containerneuerstellung erhalten. Es enthält lokale Netzadressen, aber keine
API- oder Datenbankschlüssel. Sicherungen dennoch auf berechtigte Betreiber beschränken.

Vor manueller Wiederherstellung die API stoppen, die gesicherte Datei am gleichen Pfad mit
passendem Eigentümer wiederherstellen und die API starten. Die validierten Einstellungen und
deren Revision werden geladen. Bei ungültiger Datei oder fehlenden lokalen SSDP-Schnittstellen
startet die Erfassung nicht; Datei anhand der dokumentierten Optionen reparieren oder eine
bekannte gültige Sicherung verwenden. Deploymentwerte überschreiben eine gespeicherte Datei
nicht automatisch. Keine Datenbank, Brokerdaten oder historischen Beobachtungen dafür löschen.

Eine Konfigurationsübernahme beendet laufende Erfassung und GENA-Leases; die nächste Generation
beginnt mit leerem Livecache. Ein kurzes Wiederentdecken ist erwartbar. Die Oberfläche der
Erfassung zeigt anschließend Heartbeat, Suchergebnisse und getrennte Quellenfehler. Netzwerk-
oder Firewallfreigaben werden durch ein Formular nicht erstellt; besonders SSDP und GENA
benötigen weiterhin eine passende Container-/Hostanbindung.

## Anwendungslogs (SM-023)

Unter **Anwendungslogs** (`/logs`) stehen aktuelle und aufbewahrte Meldungen der API
(einschließlich Collector), des Projektionsworkers und des Webhosts. Zum Eingrenzen eines
Ausfalls zuerst Dienst `api`, Mindestschweregrad `Warning` und Suchtext `Sonos` wählen.
Vollständige Discoveryläufe protokollieren CIDRs und Anzahl geplanter/geprüfter/gefundener,
fehlgeschlagener und zeitüberschreitender Abrufe. Bekannte Geräte protokollieren den
Wechsel zu unerreichbar und die Erholung; erwartete Nichttreffer einzelner Scanadressen
erzeugen keine eigene Warnung.

Werden Geräte gleichzeitig unerreichbar, Erreichbarkeit vom Host und aus dem API-Container
vergleichen. Funktioniert TCP 1400 nur vom Host, Docker-Netze und Routen auf Überschneidung
prüfen. Die Logmeldung nennt dies als mögliche Ursache, sie erkennt keine Docker-Routen.
Konkrete Diagnosebefehle und der frühere Befund:
[Discovery-Netzdiagnose](discovery.md#diagnose-host-erreicht-das-gerät-docker-collector-nicht).

**Speicherung:** `ApplicationLogs:Directory` muss für alle drei Hosts auf dasselbe
beschreibbare Verzeichnis zeigen. Compose bindet dafür das persistente Volume
`application-logs` unter `/app/application-logs` ein. Die Images bereiten den Pfad für
den unprivilegierten Benutzer `app` vor. Für lokale Prozesse vor dem Start in jedem
Terminal dieselbe absolute Einstellung setzen, zum Beispiel:

```powershell
$env:ApplicationLogs__Directory = Join-Path (Get-Location) '.local/logs'
```

Ohne Einstellung gilt `.local/logs` relativ zum jeweiligen Arbeitsverzeichnis.
`ApplicationLogs:Enabled=false` deaktiviert Speicherung und Abfrage. Eingebettete
Testhosts mit Konfigurations-Overrides deaktivieren sie standardmäßig, sofern weder
Logverzeichnis noch Aktivierung explizit übergeben werden; Tests nutzen eigene Temporärpfade.

Je Dienst bestehen maximal drei Dateien à 1 MiB (`api.0.jsonl` bis `api.2.jsonl`,
entsprechend `web` und `projections`). Rotation ersetzt die ältesten Dateien.
Es gibt keine garantierte Aufbewahrungsdauer. Das Volume überlebt Containerneustarts
und normale Stop-/Startläufe. Es ist kein dauerhaftes Auditarchiv.
Pro Quellenname ist genau ein schreibender Host je Verzeichnis vorgesehen.
Bei Replikaten separate Verzeichnisse beziehungsweise einen externen Logdienst einsetzen.

Die zusätzliche Speicherung umfasst Information/Warnung/Fehler/Kritisch, bei
Microsoft-/System-Kategorien nur Warnung und höher; Prozessstart/-stopp aus
`Microsoft.Hosting.Lifetime` bleibt zusätzlich enthalten. Bestehende Konsolenlogs bleiben
erhalten. Konfigurierte Zugangsdaten und übliche Credential-/Header-/URI-Muster werden
vor Speicherung und Ausgabe bereinigt; keine Scopes oder Exception-Stacks übernehmen.
Gerätekennung, lokale IP, Kategorie und Meldung bleiben zur Diagnose enthalten.
Logs sind daher installationsintern und über denselben API-Schlüssel/Webzugang geschützt.
Die Bereinigung ist kein Anonymisierungsversprechen für beliebige zukünftige Logtexte.

**API:** `GET /api/diagnostics/logs?source=api&minimumLevel=Warning&search=timeout&limit=200`.
Quelle optional (`api`, `web`, `projections`), Schweregrad Information bis Critical,
Suchtext bis 200 Zeichen, Limit 1–500. Unzulässige Abfragen ergeben 400. Antwort:
Einträge absteigend, Quellenstatus, Trefferbegrenzung. Der Status `available` bedeutet
nur lesbare Dateien und bestätigt weder einen laufenden Dienst noch aktuelle Verbindung.
Fehlende/gesperrte Dateien, beschädigte Zeilen und Schreibfehler bleiben erkennbar;
Schreibfehler dürfen die Anwendung nicht stoppen. Ein ferner Schreibfehler kann bei
noch lesbaren alten Dateien nur in dessen Prozess festgestellt werden.

Bei unerreichbarer API bleibt die Webansicht mit Fehlermeldung und gegebenenfalls letztem
Stand sichtbar. Bei ausgefallenem Webhost oder nicht gestarteter API weiter die
Container-Konsolenlogs verwenden. Fehler vor Initialisierung der Logprovider können
nur dort stehen. Docker-Daemon-, Datenbank- und Broker-Konsolenlogs sind nicht Bestandteil
dieser Ansicht. Vor dieser Änderung entstandene Konsolenlogs werden nicht rückwirkend importiert.

Nach dem Imagebuild prüft `./scripts/test-application-logs.ps1` ein eigenes kurzlebiges
Volume: Schreibrechte aller drei Images ohne Root, API-Lesen der Webhost-Logs und
Erhalt alter API-Einträge nach Containerneustart. Testcontainer/-volume werden entfernt;
Collector und Backendpersistenz bleiben im Test deaktiviert. Der Projektionsworker wird
dabei nicht gegen Broker/MongoDB betrieben; seine Image-Schreibrechte werden separat geprüft.
