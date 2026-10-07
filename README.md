# SonosMonitoring

Lokales Sonos-Monitoring mit .NET 10, Blazor/MudBlazor und der Devbone Event-Driven Platform.
Die responsive Weboberfläche bietet Deutsch/Englisch, Liveübersicht, Gerätedetails,
interaktive reporterbezogene Topologiekarte, Erfassungseinstellungen, Metrikhistorie, Änderungsverlauf, interne Meldungen,
Offline-Snapshots, Überwachungsregeln, eine filterbare Anwendungslogübersicht und eine gesondert freizugebende lokale Mediensteuerung.
Zeitangaben und Historien-Zeiteingaben folgen der Zeitzone des Browsers.

Implementiert sind begrenzte IPv4-Unicast-Discovery, optionales SSDP auf ausgewählten lokalen
IPv4-Schnittstellen, begrenzte Discovery aus aktuellen Topologiehinweisen, Geräteidentifikation, rohe
Interface-Zähler und Live-Raten, WLAN-/Ethernetzustände, lesende Wiedergabe-/Lautstärke-/Mute-
Diagnosen, SOAP-Gruppentopologie mit Liveinformationen, Datenalter/Teilfehler, ein Diagnoseexport mit
reduzierten Kennungen, optionaler GENA-Ereignisempfang mit Polling-Rückfall, Prometheus-Export,
verzögerte Störungserkennung mit geschützter interner Meldungs-API, numerische Historienabfragen
und ein dauerhafter PostgreSQL-Beobachtungsverlauf mit sichtbaren Erfassungslücken.
26 erlaubte Historienselektoren machen Byte-, Paket-, Fehler-, Drop- und Quellenverläufe
sichtbar. Detaildiagnosen benötigen eine ausdrücklich aktivierte Überwachungsregel je Gerät.
Vier detaillierte Grafana-Dashboards zeigen Übersicht, Gerätedetails, Netzwerk und
Collector/Ereignisse mit lesbaren Raum-/Gerätenamen. Die Einstellungen erklären SSDP und
GENA mit aufklappbarer Einrichtungshilfe; ausführlich in der [Netzanleitung](docs/discovery.md#ssdp-und-gena-im-alltag-sm-002-sm-016-sm-019).
Überwachungsregeln laufen als fachliche Commands über PostgreSQL und eine transaktionale
Outbox nach RabbitMQ und in MongoDB-Projektionen. Die optionale Mediensteuerung bietet
Wiedergabe, Player-/Gruppenlautstärke, Quellen-/Queueauswahl und Gruppierung über eine
geschützte API. Sie benötigt zusätzlich `MediaControl:Enabled=true`, ein eigenes
PostgreSQL-Koordinationsschema und eine **anfangs gesperrte** dauerhafte Betreiberfreigabe.
Ohne beide Freigaben werden keine Medienbefehle gesendet. Architektur und Bediengrenzen:
[Backend](docs/umsetzung-backend.md#lokale-mediensteuerung-sm-013) und
[Frontend](docs/umsetzung-frontend.md#mediensteuerung-sm-013).

## Einstieg

Voraussetzungen: SDK aus `global.json`, PowerShell 7.3 oder neuer, Docker mit Linux-Containern für
Infrastrukturtests sowie Leserecht auf den privaten Devbone-NuGet-Feed.
Lokale vorhandene NuGet-Anmeldedaten werden über den Quellnamen
`github-sharedshelf01` weiterverwendet. Keine Zugangsdaten ins Repository schreiben.

```powershell
dotnet restore SonosMonitoring.slnx --locked-mode
dotnet build SonosMonitoring.slnx -c Release --no-restore
dotnet run --project src/SonosMonitoring.Api --no-launch-profile
# In einem zweiten Terminal:
dotnet run --project src/SonosMonitoring.Web --no-launch-profile
```

Die Oberfläche ist unter **http://127.0.0.1:48082** erreichbar. Sie verwendet die API auf
Port 48080. Ohne aktivierten Collector zeigt sie einen erklärten Leerzustand. Für die
Oberfläche ist keine zusätzliche Datenbank nötig. Zugang, LAN-/HTTPS-Betrieb und
Startkonfiguration: [Frontend](docs/umsetzung-frontend.md#start-und-konfiguration).

Die API startet standardmäßig auf `http://127.0.0.1:48080`; Collector und Persistenz sind
beim ersten Start deaktiviert. Unter **Einstellungen** können Suchnetze, Startadressen,
Intervalle, Ressourcengrenzen, SSDP und GENA konfiguriert und die Erfassung gestartet werden.
Gespeicherte Einstellungen werden bei späteren Starts wieder geladen. Ohne Aktivierung
findet keine Netzwerksuche statt.
`GET /health/live` und `GET /api/collection` sind bereits nutzbar.
`GET /api/collection/export` lädt eine versionierte JSON-Momentaufnahme herunter; siehe
[Exportvertrag](docs/umsetzung-backend.md#snapshot-und-diagnoseexport-sm-006) und
[Offline-Auswertung](docs/betrieb.md#diagnoseexport-sichern-und-offline-lesen).
`GET /api/alerts` benötigt ein konfiguriertes Prometheus; `GET /api/collection/history`
benötigt das ausdrücklich aktivierte, zuvor provisionierte Beobachtungsjournal.
Konfiguration und Grenzen stehen bei den [Backend-Ergänzungen](docs/umsetzung-backend.md#backend-ergänzungen-vom-29092026).

Für den vollständigen lokalen Backendbetrieb:

```powershell
./scripts/start-backend.ps1 Up
./scripts/start-backend.ps1 Projections # eigenes Terminal
./scripts/start-backend.ps1 Api         # weiteres Terminal
```

Der Start legt projektspezifische Entwicklungsdienste und lokale Konfiguration unter
`.local/` an. Die konkreten Schritte zur Collector-Freigabe, Commands und Metrikbereitstellung
stehen in der [Backend-Dokumentation](docs/umsetzung-backend.md).
`start-backend.ps1 Up` provisioniert `sonos_media` nur bei fehlendem Namespace.
Bereits vorhandene `.local/backend.json` wird nicht ersetzt: dort `MediaControl:Enabled`
bewusst setzen, dann die Steuerung unter `/media` gesondert freigeben.

Alternativ laufen alle Hosts einschließlich Frontend und Monitoring in Docker:

```powershell
./scripts/start-stack.ps1            # aktuellen Code bauen und starten
./scripts/start-stack.ps1 Stop       # alle Stack-Container stoppen, Daten behalten
# Optional: vorhandene Images bewusst wiederverwenden
./scripts/start-stack.ps1 -SkipBuild
```

Der Start führt standardmäßig Locked-Restore, Publish und den Build aller drei Anwendungsimages
aus und zeigt nach erfolgreicher Bereitschaftsprüfung die URLs an. Voraussetzung ist ein
vertrauenswürdiges lokales .NET-HTTPS-Entwicklungszertifikat.
Frontend: **https://localhost:48082**, Anmeldung mit `WebAccessKey` aus
`.local/docker/credentials.json`. Prometheus: http://localhost:48090, Grafana:
http://localhost:48300. Vorhandene Daten und Grafana-Konten bleiben erhalten.
Alle lokalen Ports liegen im eigenen Bereich `48000–48999`; die vollständige
[Portzuordnung und Hinweise für bestehende Konfigurationen](docs/betrieb.md#lokaler-portbereich)
stehen im Betriebshandbuch.
Details zu Wiederanlauf, Konfiguration und Grenzen: [Docker-Stack](docs/betrieb.md#vollständiger-lokaler-docker-stack).

## Prüfungen

```powershell
./scripts/verify.ps1                        # Restore, Release-Build, Format, Kataloge, Unit-Tests
./scripts/verify.ps1 -IncludeInfrastructure # zusätzlich Regeln, Backup/Restore, Integration und HTTP-E2E
./scripts/publish.ps1 -BuildImages          # veröffentlichen und lokale Runtime-Images bauen
./scripts/test-application-logs.ps1         # isoliertes Logvolume, Nicht-Root-Writer und Neustart
./scripts/scan-images.ps1                   # alle drei Images mit gepinntem Trivy prüfen
# Browserprüfung nach dem Release-Build, Node.js 22+:
npm ci --prefix tests/browser
npm exec --prefix tests/browser -- playwright install chromium
npm test --prefix tests/browser
node tests/browser/media.cjs             # isolierte Medien-UI mit API-Simulator
node tests/browser/logs.cjs              # Logfilter, Fehlerfälle und mobile Logansicht
# Zusätzlich gegen ein ausdrücklich eingerichtetes lokales Grafana:
# GRAFANA_ADMIN_USER und GRAFANA_ADMIN_PASSWORD nur in der Prozessumgebung setzen.
./scripts/test-grafana-live.ps1
npm run test:grafana --prefix tests/browser
```

Die Infrastrukturtests verwenden `sonosmonitoring-tests` sowie eindeutig benannte, kurzlebige
Backup-/Prometheus-Testcontainer, eigene Ports und frische Datenbanken/Zielvolumes.
Sie werden bei fehlender Infrastruktur nicht still übersprungen.
Testergebnisse entstehen unter `artifacts/test-results/`. Siehe
[Anforderungsstatus und tatsächlicher Prüfstand](docs/planung.md#umsetzungsstand-backend).
Die reguläre Prüfung gleicht auch Dashboardmetriken, Gerätenamen und Einheiten mit
dem Katalog ab. Der zusätzliche Grafana-Lauf prüft geladene Dashboards und echte
Abfragen, der Browserlauf ihre lesbare Auswahl und Darstellung. Er verändert keine
Collector-Einstellungen. Die [Dashboardanleitung](docs/metriken.md#10-detaillierte-grafana-dashboards-und-lesbare-gerätenamen)
erklärt Filter, Werte und Datenlücken.

## Dokumentation

- [Anforderungen, Entscheidungen und Abnahmestand](docs/planung.md)
- [Planung der lokalen Mediensteuerung und Recherche bestehender Apps](docs/planung.md#ergänzung-lokale-mediensteuerung-30092026)
- [Gruppenverwaltung, Wiedergabeziele und API-Vergleich](docs/planung.md#gruppenverwaltung-und-wiedergabeziele-sm-0136-07102026)
- [Umsetzung, Architektur, Konfiguration und API-Verträge](docs/umsetzung-backend.md)
- [Frontendplan, Bedienung, Konfiguration und Browserprüfung](docs/umsetzung-frontend.md)
- [Betrieb, Diagnose, Sicherung und Wiederanlauf](docs/betrieb.md)
- [Framework-Paketkombination und Einsatzplan](docs/framework.md)
- [Versionsgebundener Framework-API-Guide](docs/framework/FRAMEWORK_API_GUIDE.md)
- [Metrikquellen, Aufbewahrung und Messgrenzen](docs/metriken.md)
- [Discovery und Netzvoraussetzungen](docs/discovery.md)
- [Entwicklungsregeln](AGENTS.md)

Prometheus-Aufbewahrung ist pro Umgebung über `PROMETHEUS_RETENTION` konfigurierbar:
`400d` ist der Betriebsvorschlag, `7d` der getestete Kurzzeitwert.
Ein kurzer Test belegt keinen Jahresbetrieb. Reale Sonos-/Firmware-, SSDP-/GENA-Netz- und
Linux/LXC-Abnahme sowie Kapazitätsmessungen bleiben offen. Stereo-/Heimkinointerpretation,
gemessene Meshverbindungen und Mehrmandantenbetrieb bleiben weitere Ausbauschritte.
Die [SM-013-Planung](docs/planung.md#ergänzung-lokale-mediensteuerung-30092026) trennt
implementierten Umfang und noch offene Simulator-/Geräteabnahme.
Benachrichtigungen erfolgen auf Nutzerwunsch ausschließlich über die interne API.
Die [Prometheus-Regeln](docs/umsetzung-backend.md#störungserkennung-sm-005-sm-011-sm-019)
unterscheiden Erfassungsunterbrechungen von dauerhaft fehlgeschlagenen Gerätebeschreibungsabrufen.
Skripte für PostgreSQL-/Prometheus-Sicherung und Wiederherstellung in neue Ziele sowie
systemd-Vorlagen stehen im [Betriebshandbuch](docs/betrieb.md).
