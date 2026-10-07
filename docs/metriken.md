# Metriken: Datenquellen und Speicherentscheidung

Stand: 30.09.2026. Ergänzung zur [maßgeblichen Planung](planung.md).
Anforderungsstatus ausschließlich dort unter SM-008 bis SM-011 und SM-014/SM-015 führen.
Die ursprüngliche Quellenanalyse folgt unten. Export, eingeschränkte Historien-API, generierter [Metrikkatalog](../contracts/metrics.json) und optionale Prometheus/Grafana-Komposition sind inzwischen implementiert. Aktueller Prüfstand: [Backend-Umsetzung](planung.md#umsetzungsstand-backend); konkrete Konfiguration: [Betrieb](betrieb.md).

## 1. Bestätigter Bedarf und Empfehlung

Für den Dauerbetrieb ist mindestens ein Jahr vorgesehen; die Aufbewahrung ist gemäß ergänzender Nutzerentscheidung pro Umgebung konfigurierbar, für Tests genügen sieben Tage. Ältere Daten dürfen gröber
werden. Die eigene Webapp und ein unabhängig nutzbares Grafana sollen dieselbe Historie lesen.
Vorhandene Grafana-/Prometheus-Infrastruktur und benötigte Detailauflösung sind noch unbekannt.

**Vorläufige Empfehlung:** Sonos-Erfassung in .NET, Prometheus-kompatibler Metrikexport und
zunächst Prometheus als Speicher-/Abfragedienst. Prometheus enthält bereits eine Zeitreihendatenbank;
es braucht nicht automatisch eine zusätzliche TSDB. Die ASP.NET-Core-API übernimmt Abfragen für
die eigene Webapp, Grafana bekommt eine eigene Datenquellenverbindung. [M1, M2, M3]

Die häufigen Messwerte gehören in den Zeitreihenspeicher. Gerätestammdaten, Einstellungen,
Topologieänderungen, Alarmregeln und Störungsvorgänge benötigen andere Datenstrukturen.
Devbone ist für fachliche Abläufe einschließlich bewusster Framework-Erprobung gewählt;
die konkrete Komposition steht im [Framework-Einsatzplan](framework.md). Grafana und numerische
Messreihen bleiben davon getrennt. Sonos-Fehlerbehandlung folgt SM-019 im Gesamtplan.

## 2. Lesende Erhebung an einem realen Gerät

Der Nutzer nannte einen lokalen Sonos-Host auf Port 1400 mit Einstieg `/status`.
Es wurden ausschließlich dieser Host und die dort verlinkten Diagnoseansichten mit HTTP GET
abgefragt. Keine Netzwerksuche, keine Steuerung, kein Neustart und keine Konfigurationsänderung.
Aufnahme am 28.09.2026; zweite Interface-Probe um 15:38:12 UTC. IP, MAC, Seriennummer,
Haushaltskennung, Raumname und vollständige Rohantworten werden nicht eingecheckt.

Der Einstieg und alle sieben verlinkten Ansichten antworteten mit HTTP 200. `/status/zp`
wurde nur auf Feldnamen und ausgewählte technische Versionswerte ausgewertet. Ausgelesen:
SoftwareVersion `86.10-80260`, HardwareVersion `1.20.2.3-2.0`, SeriesID `A100`.
Ein Produktname wurde damit nicht verifiziert; S2 bleibt bis zur passenden Zuordnung offen.

| Endpunkt | Tatsächlich beobachtet | Bedeutung / Grenze |
|---|---|---|
| `/status/wireless` | XML; `WifiModeString=STATION_MODE`, `ConnectionTypeString=WiFi`, `SonosNetDisabled=0` | Aktuell WLAN. SonosNet nicht deaktiviert bedeutet nicht, dass das Gerät SonosNet verwendet. |
| `/status/enetports` | Port 0, `Link=0`, `Speed=0` | Ethernet-Link war nicht aktiv. Einheit und Bedeutung der Geschwindigkeit bei aktivem Link noch prüfen. |
| `/status/ifconfig` | RX/TX Bytes, Pakete, Fehler und Drops für `ath0`, `br0`, `eth0`, `lo` | Kandidaten für Interface-Counter; nicht automatisch Nutzdaten-/Audiotransport oder Peer-Durchsatz. |
| `/status/VERSION` | `86.10-80260` | Technische Firmwarekennung für reproduzierbare Befunde. |
| `/status/proc/ath_rincon/status` | INFRA-Modus; Operating 2412 / IEEE 1; Home 2437 / IEEE 6; Noise Floor pro Chain; PHY-Fehlerfeld; Node-Felder FROM/TO | Diagnosewerte verfügbar, aber Semantik und Zuordnung teilweise offen. FROM/TO nicht als RSSI oder echte aktive Meshverbindung interpretieren. |
| `/status/showstp` | STP für `br0` deaktiviert | Diese Antwort liefert keine aktive STP-Baumstruktur. |
| `/status/zp` | Technische Versionen und zusätzliche Identitäts-/Konfigurationsfelder | Kein allgemeiner Metrikendpunkt; nur benötigte Felder übernehmen. |

Zwei Proben aus `/status/ifconfig`:

| `br0`-Feld | Erste Probe | Zweite Probe |
|---|---:|---:|
| RX bytes | 978257218 | 978274588 |
| TX bytes | 1220226800 | 1220245743 |
| RX packets | 2540392 | 2540544 |
| TX packets | 2645499 | 2645636 |
| RX dropped | 392360 | 392368 |

Damit sind veränderliche Zähler belegt. Die erste Probe enthält keinen explizit aufgezeichneten
Messzeitpunkt; deshalb wird daraus keine belastbare Datenrate berechnet. Die Abfragen selbst
erzeugen Verkehr und können in den Zählern enthalten sein. Die Drop-Zähler sind keine Messung
hörbarer Aussetzer und ohne Treiber-/Interface-Semantik kein Beleg für WLAN-Paketverlust.

`ath0` zeigte hier Nullwerte, während `br0` zählte. Schnittstellen nicht addieren und dadurch
Verkehr doppelt zählen; noch ist nicht belegt, welches Interface für welche Anzeige maßgeblich ist.
Noise Floor ist kein Empfangspegel und kein SNR. Das PHY-Feld ist als „since last reading/reset“
beschriftet: Lese-/Resetverhalten erst prüfen, bevor es als kumulativer Counter exportiert wird.
Der Funkdiagnoseendpunkt wurde deshalb nicht wiederholt zur Zeitreihenbildung abgefragt.

## 3. Vorgeschlagener Metrikvertrag

Die Namen sind Entwurf und werden bei Umsetzung in einen generierten/geprüften Katalog überführt.
Alle Gerätemetriken verwenden eine stabile, möglichst pseudonymisierte `device_id`; bei Bedarf
zusätzlich `household_id`. Interface- und Quellenlabels nur aus einer begrenzten Menge übernehmen.

| Kandidat | Typ / Einheit | Quelle / Abnahmepunkt |
|---|---|---|
| `sonos_device_probe_success` | Gauge 0/1 | Letzter abgeschlossener Geräteprobeversuch; Collector-Ausfall separat erkennen |
| `sonos_device_probe_duration_seconds` | Gauge, Sekunden | Dauer des letzten Probeversuchs, lokal gemessen |
| `sonos_source_last_success_timestamp_seconds` | Gauge, Unix-Sekunden | Letzte erfolgreiche Erhebung je Diagnosequelle |
| `sonos_source_scrape_success` | Gauge 0/1 | Erfolg je Diagnosequelle; Teilfehler nicht als Gesamtausfall ausgeben |
| `sonos_interface_receive_bytes_total` / `sonos_interface_transmit_bytes_total` | Counter, Bytes | `ifconfig`, Label `interface`; Reset-/Überlaufverhalten noch prüfen |
| `sonos_interface_receive_packets_total` / `sonos_interface_transmit_packets_total` | Counter, Pakete | `ifconfig`, Label `interface` |
| `sonos_interface_receive_errors_total` / `sonos_interface_receive_dropped_total` | Counter, Anzahl | Rohzähler mit klarer Beschreibung; kein automatischer Audiofehlerbefund |
| `sonos_ethernet_link_up` | Gauge 0/1 | `enetports`, Label `port` |
| `sonos_wifi_noise_floor_dbm` | Gauge, dBm | Vorläufig experimentell: Chain-/Bandzuordnung und Aktualität erst prüfen |

Numerische Einheiten und Counter-Suffixe explizit führen. IP, Raumname, Firmware und Modell nicht
an jede Messreihe hängen: Umbenennung oder Upgrade würde neue Reihen erzeugen. Metadaten über
die API beziehungsweise eine separate begrenzte Info-Metrik zuordnen. Keine Tracktitel, URLs,
Zeitstempel oder beliebigen Fehlertexte als Labels. Das begrenzt Zahl und Wechsel der Reihen. [M4]

Durchsatz aus geeigneten Countern ableiten, zum Beispiel `rate(..._bytes_total[2m])` in Bytes/s;
für bit/s mit acht multiplizieren. Abfragefenster müssen mehrere Proben enthalten.
Collector-Neustart darf Gerätecounter nicht künstlich nullsetzen. Geräte-Neustart, Counter-Reset,
Überlauf und fehlende Proben testen; keine negativen Datenraten zeichnen.

Ein nicht unterstützter Wert wird ausgelassen, nicht als Null exportiert. Der Collector hält
einen begrenzten Cache für Liveansicht und Export; alte Gerätewerte dürfen nach Ablauf ihres
Gültigkeitsfensters nicht weiter als frische Samples angeboten werden. Erfolgszeitpunkt und
Quellenstatus bleiben separat abfragbar. Das Prometheus-Signal `up` für den Exporter beschreibt
dessen Erreichbarkeit und ersetzt keine Geräteprobe.

## 4. Speicheroptionen

| Option | Passend für diesen Bedarf | Abwägung |
|---|---|---|
| Prometheus mit lokaler TSDB | Scraping, PromQL, eigene API-Abfragen und Grafana; bevorzugter erster Kandidat | Retention explizit setzen, Einzelknoten dauerhaft speichern und sichern. Ein Jahr ist mit passender Dimensionierung möglich. [M1–M3] |
| VictoriaMetrics Single Node | Alternative mit Prometheus-Abfrage-API und eigenem Scraping; längere Historie als Bewertungsfall | Abfragekompatibilität prüfen. Automatisches Downsampling und Retentionfilter nicht als kostenlose Standardfunktion einplanen: Die Dokumentation ordnet sie Enterprise zu. [M5] |
| PostgreSQL mit Timescale | Falls SQL-Auswertungen, relationale Verknüpfungen und gestufte Aggregate im Vordergrund stehen | Eigenen Schreibweg, Schema, Aggregations-/Löschregeln und Grafana-SQL-Abfragen gestalten; mehr Anwendungsarbeit als beim Scrape-Modell. [M6, M7] |

Nicht mehrere dieser Speicher parallel als gleichberechtigte Historien aufbauen. Nach Auswahl
ein maßgeblicher Abfrageweg für Metriken. Eine später notwendige Migration benötigt einen
eigenen Datenübernahmeplan; Prometheus-Kompatibilität bedeutet keine automatische Migration.

## 5. Auflösung, Retention und Lücken

Die Mindestdauer von einem Jahr ist bestätigt. Konkrete Abtastraten sind noch offen.
Vorschlag zur Erprobung: Interface-/Erreichbarkeitswerte alle 15 Sekunden, statische Informationen
deutlich seltener; teurere oder semantisch unsichere Diagnosequellen separat behandeln.

**Einfacher erster Entwurf:** 400 Tage Rohdaten bei passender Kapazität; damit wird das Jahresziel
ohne zusätzlichen Verdichtungspfad erreicht. Grafana und Webapp wählen für große Zeiträume
gröbere Abfragen. Das reduziert Rückgabemenge, nicht den belegten Speicher.

**Falls das Volumen dies erfordert:** beispielsweise 30 Tage Rohwerte und mindestens 400 Tage
5-Minuten-Aggregate. Das ist eine Alternative, kein bereits beschlossener Speicherplan.
Für Signalwerte nicht nur Mittelwerte, sondern bei Bedarf Minimum/Maximum und Probenanzahl
erhalten; für Erreichbarkeit Ausfalldauer und Datenabdeckung unterscheiden. Counters vor
Verdichtung resetgerecht auswerten. Ein beliebiger letzter Wert je Zeitfenster genügt dafür nicht.

Prometheus beschreibt Retention und Blockkompaktierung. Recording Rules speichern vorberechnete
Auswertungen als zusätzliche Reihen; alleine trennen sie nicht die Aufbewahrungsdauer alter
Rohdaten und Aggregate. Für verschiedene Retentionstufen braucht es eine ausdrücklich geplante
Speicher-/Löschstrategie. Timescale dokumentiert kontinuierliche Aggregate, deren Refresh- und
Löschregeln abgestimmt werden müssen. [M1, M6, M8]

Kapazitätsplanung anhand tatsächlicher Reihen, Scrape-Intervall, Labelwechsel und Aufbewahrung:
`Samples = aktive Reihen × Sekunden der Aufbewahrung / Scrape-Intervall`.
Geräteproben sind keine einzelnen Samples: Jede exportierte Metrik/Labelkombination bildet eine
Reihe. WAL, Index, temporärer Platz, Metadaten, Backups und Reserven zusätzlich berücksichtigen.
Keine zugesagte RAM-/Disk-Größe ohne Messung des konkreten Metrikumfangs.

Pull-Scraping erfasst während eines Prometheus-Ausfalls keine neuen Werte. Ein Livecache holt
die Zwischenwerte danach nicht nach. Solche Lücken müssen sichtbar bleiben. Falls auch bei
Speicherausfall vollständige Beobachtungen erforderlich werden, separat eine persistente Queue
oder einen geeigneten Collector-/Remote-Write-Weg entwerfen. Auch damit lassen sich bei
abgeschaltetem Collector überhaupt nicht gemessene Werte nicht rekonstruieren.

Retention/Backup für Anwendung und Metriken getrennt prüfen. Lokale persistente Datenträger
für Prometheus vorsehen; dessen Dokumentation empfiehlt lokales Dateisystem und unterstützt
NFS für die lokale TSDB nicht. Retentions-Speicherlimits dürfen das Jahresziel nicht unbemerkt
verkürzen. Ein Neustarttest ersetzt keinen Wiederherstellungstest. [M1]

## 6. Webapp, Grafana und Alarme

Die Webapp erhält Historie über unsere API: ausgewählte Metriken, erlaubte Geräte,
Zeitraum und passende Schrittweite. Die API fragt etwa Prometheus `query_range` ab; keine
ungefilterte öffentliche Weiterleitung beliebiger PromQL-Abfragen. Datenquellenzugangsdaten
bleiben serverseitig. Grafana liest dieselbe TSDB mit eigenem Zugriff. [M2, M3]

Vorgeschlagene erste Dashboards: Geräteerreichbarkeit und Datenalter, Interface-Durchsatz,
Fehler-/Drop-Zähler sowie Collector-Gesundheit. Funkwerte erst nach ihrer fachlichen Prüfung.
Die eigene Webapp ergänzt Topologie und Gerätekontext. Grafana muss nicht eingebettet werden
und ist keine Voraussetzung für die Bedienbarkeit unserer Webapp.

Für die ersten technischen Störungsregeln wurde Prometheus als alleiniger Besitzer gewählt;
Wartezeiten, Bedingungen und Grenzen stehen unter
[Störungserkennung](umsetzung-backend.md#störungserkennung-sm-005-sm-011-sm-019).
C# exportiert Beobachtungen und Erfassungsbudgets; Grafana bewertet diese Regeln nicht erneut.
Alertmanager für Gruppierung/Zustellung bleibt ein Kandidat für den nächsten Zustellungsschritt;
Anwendungslogik besitzt spätere fachliche Störungsvorgänge. Ein Alertmanager wäre kein dauerhaftes
Störungsarchiv. Kanal, Integration und Quittierungssemantik bleiben offen. [M9]

## 7. Noch ausstehende Nachweise

- Unterstützte Geräte/Firmware und Gegenprüfung unter LAN/SonosNet.
- Semantik von Interface-Drops, Funkwerten und Counter-Reset-/Leseverhalten.
- Collectorlast bei längerem Betrieb und Auswirkungen des Pollings auf die Geräte.
- Metrikexport, tatsächliche Speicherung, gleiche Abfrageergebnisse in Webapp/Grafana.
- Retention, Verdichtung falls gewählt, Backup/Restore, Neustart und sichtbare Erfassungslücken.
- Tatsächliche Ressourcenmessung und Entscheidung über vorhandene oder mitgelieferte TSDB.

## 8. Bestandsprüfung: fehlende Webmetriken und Topologie, 30.09.2026

Anlass: Der Nutzer sieht kaum Metriken und vermisst Liveinformationen in der Topologie.
Die Prüfung umfasst Collector, Policyprojektion, API, Prometheus, Webcode und die drei
bereits erkannten Geräte. Anforderungsbezug und maßgeblicher Prüfstand:
[SM-003/004/005/008/009/014/015/016/020](planung.md#metriken--und-topologiebefund-30092026).
Die Befund-IDs unten bezeichnen Beobachtungen, keine neu beschlossenen Anforderungen.
Die Ausbaufolge ist eine Empfehlung; sie erweitert den laufenden Prüfauftrag nicht um
eine vollständige Implementierung. Kein Hinweis auf einen Frameworkdefekt gefunden.

### Tatsächlich beobachteter Betrieb

Lesende Prüfung am 30.09.2026, etwa 13:25–13:29 Uhr Europe/Berlin:

- Collector aktiv, Pollingintervall 15 Sekunden, Veraltungsgrenze 45 Sekunden;
  SSDP und GENA deaktiviert. Alle acht SonosMonitoring-Stackcontainer laufen.
- Drei Geräte: zweimal SYMFONISK Bookshelf, einmal SYMFONISK Table lamp.
  Gerätebeschreibung und SOAP-Topologie sind bei allen erfolgreich und aktuell.
  Jeder Reporter meldet eine Gruppe mit drei Mitgliedern. Die Topologie-Erfolgszeit
  wird bei späteren API-Abrufen weitergeschrieben.
- Bei allen Geräten: `ifconfig.errorCode=monitoring_disabled`, kein letzter Erfolg,
  keine Interface-Zähler im Collector-Snapshot. Der jeweilige Policy-GET liefert 404:
  keine projizierte Überwachungsregel vorhanden. Es wurde kein fachlicher Stream
  direkt ausgelesen; die für den Scheduler maßgebliche Projektion ist hier die Evidenz.
- Prometheus: `up{job="sonosmonitoring-api"}=1`, drei `sonos_device_probe_success`-Reihen.
  Keine aktuellen RX-Byte-Reihen und keine solchen Reihen im abgefragten 24-Stunden-Fenster.
  Auch `source="topology"` fehlt im Quellenexport.
- Historien-API für ein erkanntes Gerät und eine Stunde bei 15 Sekunden Schrittweite:
  `receive_rate` liefert HTTP 200 mit leerer Matrix; `probe_success` HTTP 200 mit
  einer Reihe und 240 Punkten. `receive_dropped` wird mit HTTP 400 abgelehnt.
  Die Historienanbindung funktioniert; das Fehlen der Interface-Daten entsteht davor.
- Direkte GET-Einzelabfragen am Windows-Host: `/status/ifconfig`, `/status/wireless`,
  `/status/enetports` und `/status/VERSION` liefern bei allen drei Geräten HTTP 200.
  Je Gerät vier vollständige RX-/TX-Paket- und Byte-Zählerblöcke; WLAN meldet
  `STATION_MODE`, `WiFi`, `SonosNetDisabled=0`; Ethernet-Port 00 meldet `Link=0`,
  `Speed=0`; Firmware jeweils `86.10-80260`. Die Gerätebeschreibungen enthalten
  Software-/Hardwareversion und Modellnummer sowie unter anderem `AVTransport`,
  `RenderingControl` und `GroupRenderingControl`. Diese Dienste wurden nicht aufgerufen.

Keine Netzsuche zusätzlich ausgelöst, keine Überwachungsregel aktiviert, keine
GENA-Anmeldung und keine Sonos-Steueraktion ausgeführt. Keine Rohantworten, Adressen,
Raumnamen, UDNs oder Zugangsschlüssel eingecheckt. Die direkten Diagnose-GETs stammen
vom Windows-Host; sie ersetzen keinen Diagnose-Nachweis aus dem API-Container.
Ein erfolgreicher GET und vollständig wirkende Zählerblöcke ersetzen außerdem noch
keinen Parser-/Counter-Reset-Nachweis gegen genau diese Firmware.

### Ursachen und vorhandene Abdeckung

| ID / Anforderungen | Befund und Ursache | Konsequenz |
|---|---|---|
| MET-001 / SM-005/014/020 | Bei aktivem fachlichem Backend entscheidet `ProjectedCollectionPolicy`: fehlende Policy bedeutet `Enabled=false`. `CollectorWorker` liest Beschreibung und Topologie vorher, überspringt danach `ifconfig`. Der Formatter unterdrückt deaktivierte Zähler. | Globale Erfassung aktivieren genügt nicht. In **Geräte → Gerätedetails → Überwachung aktivieren** muss die Diagnose je Gerät aktiviert werden. Danach erst entstehen neue Messreihen; vergangene Messungen lassen sich nicht nachtragen. |
| MET-002 / SM-005/018/020 | DE-/EN-Text `NoPolicy` behauptet, ohne Regel gelte die Standarderfassung. Die Metrik-Leeransicht verweist nur auf Geräteauswahl, Zeitraum, Prometheus und Retention. `SourceBadge` klassifiziert auch `monitoring_disabled` als fehlgeschlagen; `SourceCard` zeigt den Grund nur als technischen Code. | Die Ursache der leeren Diagramme ist nicht verständlich sichtbar. Fehlende Regel, bewusste Deaktivierung, Projektionsausfall, ununterstützte Quelle und technische Fehler müssen getrennt erklärt werden. |
| MET-003 / SM-014/015 | Acht Interface-Counter sind implementiert: RX/TX Bytes, Pakete, Fehler und Drops. Gerätedetails zeigen alle acht Rohwerte; API und Webhistorie erlauben nur RX/TX Bytes, RX/TX Byterate und Beschreibungserfolg. Das Grafana-Beispiel hat lediglich RX-Rate und Beschreibungserfolg. | Selbst bei aktivierter Diagnose bleiben Paket-, Fehler- und Drop-Historien im Web unzugänglich. Erweiterung benötigt auch korrekte Einheiten im API-Vertrag; `HistoryEndpoints` kennt bisher nur Bytes, Bytes/s und Boolean. |
| MET-004 / SM-003/005/016 | Snapshot und Gerätedetails enthalten Topologiequalität, Zeitpunkte und GENA-Zustand. Der Prometheus-Formatter ruft `AppendSource` nur für Beschreibung und `ifconfig` auf; Topologie und Ereignispfad fehlen. Es gibt keine entsprechenden Störungsregeln. | Gruppen-/Callbackprobleme lassen sich nicht historisch bewerten. Erfolgreiche Gerätebeschreibung beweist weder aktuelle Gruppen noch funktionierende Ereigniszustellung. |
| MET-005 / SM-003/004/005 | `Groups` erbt den 10-Sekunden-Webtimer. Die Karte aktualisiert Daten und bewahrt Positionen bei gleicher Struktur; Knotenqualität hängt allein an der Gerätebeschreibung. Auswahl zeigt Modell, Adresse und Beschreibungsbadge. | Die Karte hat Polling-Livedaten, aber kaum sichtbaren Livekontext: Diagnose deaktiviert, Topologiealter, Ereignispfad, Zähler und Datenraten fehlen an den Knoten. Eine aktuelle Beschreibung kann neben fehlender Diagnose stehen. |
| MET-006 / SM-008 | Der Collector erlaubt als GET-Pfade nur Beschreibung und `ifconfig`; `wireless`, `enetports` und Versionsinformationen werden nicht integriert. Der Beschreibungsparser übernimmt nur Identität, Raum, Modell und Topologie-Eventunterstützung. | Verfügbare WLAN-/Ethernetzustände und technische Metadaten bleiben liegen. Sie benötigen je Quelle einen eigenen Status, Erhebungszeit und Unterstützungshinweis. |
| MET-007 / SM-003/009 | Kanten entstehen aus Gruppenmitgliedschaft und ausdrücklich gemeldeten Satelliteneltern. Keine Messung von Router/AP-Verbindung, SonosNet-Peerlinks, Linkqualität oder Verkehr zwischen Geräten. | Eine reichere Gruppenkarte ist sofort mit vorhandenen Zuständen möglich. Eine echte Netzwerktopologie benötigt zusätzliche belegte Quellen; Interface-Durchsatz darf nicht als Kantenverkehr verwendet werden. |
| MET-008 / SM-005/014/020 | Policyintervalle von 5–300 Sekunden sind zulässig; tatsächlich gilt mindestens das globale Pollingintervall. `CollectorState` bewertet alle Quellen mit demselben `StaleAfter`, unabhängig vom Geräteintervall. Der Formatter lässt veraltete Zähler weg. | Bei z. B. 60 Sekunden Diagnoseintervall und 45 Sekunden Veraltungsgrenze entstehen erwartbare Exportlücken zwischen erfolgreichen Abrufen. Dies ist aus dem Code abgeleitet, nicht mit einer realen 60-Sekunden-Policy reproduziert. Kombination validieren oder quellenbezogene Aktualitätsbudgets festlegen. |

Der generierte [Metrikkatalog](../contracts/metrics.json) enthält 15 Metrikdefinitionen:
drei Collectorwerte, Beschreibungserfolg, drei generische Quellenwerte und acht
Interface-Counter. Das ist ein enger technischer Erfassungsschnitt, noch kein vollständiges
Sonos-Monitoring. `probe_success` meint ausschließlich den Gerätebeschreibungsabruf,
nicht Wiedergabequalität, Audioaussetzer oder erfolgreiche Detailerfassung.

### Empfohlene Ausbaufolge und überprüfbare Abnahme

| Reihenfolge | Umfang | Abnahme bei Umsetzung |
|---|---|---|
| 1: Bestehende Messung nutzbar machen | MET-001/002/008: explizite Geräteaktivierung, zutreffende DE-/EN-Statushinweise, Aktivierungslink in der leeren Metrikansicht, sichtbares effektives Intervall und kompatible Aktualitätsgrenze. | Fehlende/aktive/deaktivierte/nicht lesbare Policy unterscheiden; Aktivierung über fachlichen Command und bestätigte Projektion. Anschließend reale `ifconfig`-Erfolge, acht Counter je unterstütztem Interface, neue Prometheus-Reihen und nach mehreren Proben RX/TX-Raten prüfen. Deaktivierung, Timeout, Reset und lange Intervalle gesondert nachweisen. |
| 2: Vorhandene Counter und Zustände zeigen | MET-003/004/005: Pakete/s, Fehler/s, Drops/s sowie Rohzähler; bereits exportiertes Quellenalter/-erfolg und Collector-Heartbeat sichtbar machen. Topologiequalität und Ereignispfad zusätzlich exportieren. Karte um getrennte Beschreibungs-/Diagnose-/Topologiestatus, Messzeit und ausgewählte Interfacewerte ergänzen; Details direkt am Knoten, gleiche Informationen in der Liste. | Gemeinsame Selektoren und Einheiten für API/Web/Grafana; keine unkontrollierten PromQL-Eingaben. Kein Addieren von Bridge-, physischem und Loopbackverkehr. Gleiche Zeiträume liefern gleiche Werte; veraltete Werte bleiben markiert, Messlücken sichtbar. Liveupdate bei 360/1440 px mit Positions-/Zoomerhalt, Touch und Tastatur prüfen. |
| 3: Belegte Quellen integrieren | MET-006: WLAN-/LAN-/SonosNet-Modus, Ethernet-Link, Software-/Hardwareversion und Modellnummer. Zusätzlich Collector-Probedauer, Fehler-/Timeoutanzahl, Sweepdauer und Discoveryergebnis als lokal messbare Betriebsmetriken. | Modell-/Firmwarebezogene Parserproben; je Quelle Timeout, fehlendes Feld und Unsupported unterscheiden. Linkgeschwindigkeit erst mit belegter Einheit/aktivem Link. Metadaten in API/Details, nicht als wechselnde Labels an allen Zeitreihen. Keine Nullwerte für fehlende Quellen. |
| 4: Wiedergabezustände ergänzen | Lesender Zustand Playing/Paused/Stopped/Buffering, Lautstärke, Mute und gegebenenfalls Quelle/Track im Gerätekontext. | Lokale angebotene Dienste und deren Aktionen pro Modell prüfen; reine GET-/SOAP-Leseaktionen isolieren. Gruppen-/Playersemantik trennen; Eventabonnements bei Bedarf zusätzlich zur Topologie implementieren. Titel/URLs bleiben Zustandsdaten und werden keine Metriklabels. Keine Steuerbefehle für diese Erweiterung erforderlich. |
| 5: Funk- und Netzwerktopologie erproben | MET-007: Kanal/Frequenz, RSSI/SNR, Noise Floor, AP-Zuordnung, tatsächliche Peer-/STP-Verbindungen nur nach eigenem Quellenbeleg. | Unterstützte und ununterstützte Modelle unterscheiden; Einheiten, Richtung, Aktualität, Reset-/Leseeffekte und Peerzuordnung prüfen. Beobachtete Gruppenbeziehung, berichtete Netzwerkverbindung und gemessener Link bleiben unterscheidbar. |

Die Sonos-[Netzwerkdetails](https://support.sonos.com/de-de/article/understanding-the-network-details-section-in-the-sonos-app)
beschreiben Kanal, Frequenz, BSSID und SNR für bestimmte WLAN-Setups. Das belegt den
Produktnutzen, aber keinen zugänglichen lokalen API-Vertrag für unsere Geräte.
Noise Floor allein ergibt weder RSSI noch SNR. `SonosNetDisabled=0` bedeutet lediglich,
dass SonosNet nicht deaktiviert ist; die aktuell beobachtete Verbindung lautet `WiFi`.
Ethernet `Link=0` beweist keinen Geräteausfall. Interface-Drops beweisen keine Audioaussetzer.

Sonos dokumentiert [Wiedergabezustand](https://docs.sonos.com/reference/playback-getplaybackstatus-groupid)
und [Gruppenlautstärke/Mute](https://docs.sonos.com/reference/groupvolume-object) in der
Control API. Diese Cloudverträge sind keine Garantie für lokale SOAP-Signaturen.
Die lokal angekündigten Dienste sind deshalb nur Kandidaten für einen lesenden
Machbarkeitsnachweis; Cloudzugriff wurde weder verwendet noch als Fallback beschlossen.

**Verbleibende Prüfungen:** Keine Aktivierung mit anschließendem realem Collector-bis-
Web-Metriknachweis, kein neuer Browser-/Grafana-Vergleich, keine Messung von RSSI/SNR,
Peerlinks oder Wiedergabezuständen und kein neuer Build-/Gesamttestlauf. Der Auftrag
änderte nur Dokumentation; bisherige Implementierungs- und Testnachweise bleiben
zeitlich getrennt. Für künftige Codeänderungen gelten die bestehenden Release-, Format-,
Katalog- und passenden Verhaltenstestgates.

Ein unabhängiger lesender Subagent-Codeabgleich bestätigte MET-001 bis MET-008 und die
15 Katalogdefinitionen. Die Ausbauempfehlung wurde dabei um die ausdrückliche Trennung
zwischen bereits exportiertem Collector-Heartbeat und neu zu exportierender Topologie-
bzw. Ereignispfadqualität präzisiert. Das Review ist kein zusätzlicher Laufzeitnachweis.

## 9. Umgesetzte Erweiterung vom 30.09.2026

Die in Abschnitt 8 empfohlene Ausbaufolge wurde vom Nutzer zur Umsetzung freigegeben.
Maßgebliche Abnahmekriterien und der abschließende Prüfstand stehen bei
[SM-004/005/008/014/015/020/022](planung.md#erweiterte-metrik--und-zustandsanzeige-30092026).
Der ursprüngliche Befund bleibt als zeitlich abgegrenzte Bestandsaufnahme erhalten.

Die Gerätepolicy steuert nun alle Detaildiagnosen: Interfacezähler, WLAN-Modus,
Ethernetports, Wiedergabe und Playerlautstärke/Mute. Beschreibung und Gruppentopologie
werden unabhängig davon weiter abgefragt. Jede Detailquelle hat eigene Erfolgszeit,
Fehlerkategorie, Planung und Backoff. Nicht unterstützte Quellen ergeben keine
erfundenen Nullwerte. Ein fehlgeschlagener Abruf erhält die letzte gute Beobachtung,
markiert sie aber als fehlgeschlagen. Volume/Mute gelten erst nach beiden erfolgreichen
Leseaktionen als frisch. Technische Modell-/Hardware-/Softwareangaben bleiben Zustandsdaten.

Das Frischebudget für Detailquellen ist `max(StaleAfter, 3 * effektives Intervall)`.
Das effektive Intervall ist mindestens das globale Pollingintervall. Live-Raten werden
aus zwei erfolgreichen Zählerständen desselben Interfaces und der tatsächlichen Zeitdifferenz
berechnet. Bei Erstprobe, langer Lücke oder Reset der betreffenden Richtung fehlt die Rate.
Bridge-, physischer und Loopbackverkehr werden nicht addiert. Historische Raten verwenden
weiterhin Prometheus `rate(...[2m])`; sie sind gleitende Raten aus Scrapes und können deshalb
von der zuletzt beobachteten Live-Rate abweichen.

Der generierte Exportkatalog enthält jetzt 31 Definitionen. Neu sind Topologie und die
vier Detailquellen im Quellenexport, GENA-Zustand, Lautstärke/Mute, Ethernet-Link,
effektives Intervall, Probedauer, Sweepdauer, Probe-/Fehler-/Timeoutzähler und das letzte
deduplizierte Discoveryergebnis. Probe-Fehler umfassen auch erwartete Discovery-Nichttreffer
und sind daher keine Geräte-Ausfallquote.

`GET /api/metrics/history/catalog` liefert 26 erlaubte Historienselektoren mit Einheit, Scope
und Labelschlüssel. Zur bisherigen Byte-/Raten-/Erfolgsansicht kommen Pakete, Fehler,
Drops und deren Raten, Quellenerfolg/-frische/-alter, GENA-Abo/-Initialnachricht/-Alter,
Probedauer, Playerlautstärke und Mute. `sourceFilter` akzeptiert ausschließlich die sieben
Collectorquellen; `networkInterface` bleibt auf Interfaces beschränkt. Webauswahl und
Achsen beziehen Selektor und Einheit aus diesem Katalog. Grafana liest dieselben
exportierten Metriken; keine freien PromQL-Eingaben über die API.

Lesende Modellprobe: Alle drei bekannten Geräte mit Software `86.10-80260` lieferten
WLAN `STATION_MODE`/`WiFi`, Ethernet-Link 0, gültige `GetTransportInfo`, `GetVolume`
und `GetMute`-Antworten. Die SOAP-Aktionen verwenden ausschließlich angebotene feste
Controlpfade, InstanceID 0 und Masterkanal. Ethernet-Speed bleibt ein Rohwert ohne
zugesagte Einheit. Lautstärke ist die Playerskala 0–100, keine gemessene akustische Lautheit.

Die einmalige Funkprüfung ergab Noise Floor mit expliziter dBm-Einheit und deaktiviertes
STP. Die PHY-Lese-/Resetsemantik ist offen. Diese Quelle wird deshalb nicht periodisch
erfasst; RSSI/SNR und aktive Netzwerkpfade bleiben offen. Gruppenbeziehungen auf der Karte
werden nicht als gemessene WLAN-/Meshverbindungen ausgegeben.

Im lokalen Stack wurden am 30.09.2026 alle drei Geräte über den bestehenden
Commandweg aktiviert. Der reale Collector lieferte sieben frische Quellen je Gerät,
insgesamt zwölf Interface-Raten und nichtleere Historien. Für gleiche UTC-Grenzen
und Schrittweite stimmen API und Grafana-Datenquellenproxy numerisch überein.
Der vollständige Prüfstand und verbleibende Abnahmen stehen bei
[SM-004/005/008/014/015/020/022](planung.md#erweiterte-metrik--und-zustandsanzeige-30092026).

Der reduzierte Snapshotexport erhält die neuen Messwerte und Quellenevidenz, ersetzt
Geräte-/Interface-/Portidentifikatoren durch lokale Aliase und reduziert gemeldete Zustände
auf geprüfte Werte. Technische Metadaten bleiben im reduzierten Export entfernt. Schema 1
bleibt durch additive Felder mit sicheren Defaults lesbar; neue Werte werden beim Import
auf Struktur, Größen und Zahlenbereiche geprüft.

## 10. Detaillierte Grafana-Dashboards und lesbare Gerätenamen

Die provisionierten Dashboards liegen in `infra/grafana/dashboards` und verwenden dieselbe
Prometheus-Datenquelle wie die Historien-API. Der bestehende Einstieg behält die UID
`sonosmonitoring-overview`. Drei weitere Dashboards ergänzen die Übersicht:

| Dashboard | Inhalt / Einsatz |
|---|---|
| **Übersicht** (`sonosmonitoring-overview`) | Bekanntes Inventar, Monitoringaktivierung, Geräte-/API-Erreichbarkeit, ausgewählter Interface-Durchsatz, Lautstärke, Quellenqualität/-alter, GENA und Collector-Heartbeat. Einstieg zur täglichen Prüfung. |
| **Gerätedetails** (`sonosmonitoring-device`) | Raum, Modell und Firmware, effektives Intervall, Beschreibungserfolg/-dauer, aktuelle Wiedergabe und gemeldete Zustandsverläufe, Playerlautstärke/Mute, WLAN/Ethernet, alle Quellen und GENA. Für die Diagnose eines ausgewählten Players. |
| **Netzwerk** (`sonosmonitoring-network`) | RX/TX-Raten und kumulative Rohzähler für Bytes, Pakete, Fehler und Drops, Interfaceauswahl, WLAN-/SonosNet-Zustand und Ethernet-Link. Für Vergleiche je Interface, ohne Bridge-, physische und Loopbackwerte zu addieren. |
| **Collector und Ereignisse** (`sonosmonitoring-collector`) | API-Scrape, Heartbeatalter und Budget, Sweepdauer, Probe-/Fehler-/Timeoutraten und Zähler, dedupliziertes Discoveryergebnis, Geräte-/Quellenqualität und der GENA-Ereigniskanal. Für den Betrieb der Erfassung. |

Oben zuerst **Gerät** wählen, anschließend **Interface** und gegebenenfalls **Quelle**.
Mehrfachauswahl und „All“ sind möglich. Für das Interface ist `br0` voreingestellt;
das ist eine praktische Ansicht und keine belegte Aussage über den Audiotransport.
Die Dashboardlinks übernehmen Auswahl und Zeitraum. Die Gerätedetails sind außerdem aus
benannten Messreihen über deren Serienlinks erreichbar.

Die Geräteauswahl zeigt einen lesbaren Namen aus Raum beziehungsweise Modell. Bei gleichen
Namen wird ein kurzer pseudonymer Zusatz angehängt. Eine lange Gerätekennung dient weiterhin
als stabiler interner Filterwert, erscheint aber nicht als normale Legende oder Tabellenüberschrift.
Grafanas getrennte `text`-/`value`-Gruppen bilden Anzeigename und Filterwert ab.
[Offizielle Grafana-Variablendokumentation](https://grafana.com/docs/grafana/latest/visualizations/dashboards/variables/advanced-variables/#filter-and-modify-using-named-text-and-value-capture-groups)
beschreibt diese Zuordnung.

Drei zusätzliche Info-Gauges erweitern den Exportkatalog von 31 auf **34 Definitionen**:

- `sonos_device_info` ordnet den stabilen Geräteschlüssel dem lesbaren Namen, Raum, Modell
  sowie Software-/Hardwareangaben zu; eine Reihe bleibt für jedes bekannte Gerät erhalten.
- `sonos_wireless_info` enthält aktuellen WLAN-Modus, Verbindungstyp und die gemeldete
  SonosNet-Deaktivierung; es wird nur bei einer erfolgreichen frischen Quelle exportiert.
- `sonos_playback_info` enthält aktuellen Transportzustand/-status; es wird ebenfalls nur
  bei einer erfolgreichen frischen Quelle exportiert.

Interfacecounter und andere Messreihen erhalten weiterhin keine Raum-/Firmwarelabels.
Die Dashboards verbinden diese Reihen per `device_id` mit dem Anzeigenamen aus
`sonos_device_info`. Eine Umbenennung oder ein Upgrade beginnt damit keine neue Counterreihe.
Inventar und Auswahl verwenden die aktuelle Namenszuordnung. Zeitreihen verwenden den Namen
vom Ende des ausgewählten Zeitraums (`sonos_device_info ... @ end()`), damit eine Umbenennung
innerhalb des Fensters die Messreihe nicht aufteilt und ältere Messungen vor Einführung der
Info-Metrik weiterhin unter diesem Namen sichtbar bleiben. Nur die Namenszuordnung ist dabei
auf das Zeitraumende festgelegt; Messwerte und Frischefilter bleiben beim tatsächlichen
Abfragezeitpunkt. Die [Prometheus-Dokumentation zum @-Modifier](https://prometheus.io/docs/prometheus/latest/querying/basics/#-modifier)
beschreibt die konstante Endzeitauflösung in einer Bereichsabfrage.

Endet der gesamte Zeitraum vor der ersten Info-Messung, fehlt die Namenszuordnung. Vorhandene
Messwerte bleiben dann als **„Unbekannter Player (kurzer Schlüssel)“** sichtbar; der Zusatz
enthält nur die ersten acht Zeichen des pseudonymen Schlüssels. Dieser Fallback erscheint
je Gerät ausschließlich ohne passende Endzeitmetadaten, erzeugt keine zweite Reihe bei
bekanntem Namen und ersetzt keine fehlenden Messwerte. Historische Namen werden nicht
rückwirkend erfunden oder nachgeschrieben. Die Historien-API fragt weiterhin anhand stabiler
Geräteschlüssel ab und benötigt diese Metadatenverknüpfung nicht. Ein Inventareintrag allein
beweist keine aktuelle Erreichbarkeit: Beschreibungserfolg und Quellenalter separat prüfen.

**Werte richtig lesen:** Statuspanels zeigen Zustände, Alters-/Dauerpanels Sekunden.
Playerlautstärke bleibt die Geräteskala **0–100**, kein Anteil 0–1 und keine akustische Messung.
Fehlende Werte bleiben „Keine Daten“ statt künstlicher Null. Die Interface-Raten verwenden
`rate(...[2m])` und werden bei fehlgeschlagener oder veralteter `ifconfig`-Quelle ausgeblendet.
Kumulative Rohzähler dürfen die letzte gute Beobachtung behalten; ihre Aktualität steht in
den separaten Quellenpanels. Die Interpretation von Drops, WLAN-Modus und Ethernet-Link
bleibt auf den belegten Gerätevertrag beschränkt. Die Dashboards erfinden weder RSSI/SNR
noch aktive Meshpfade.

Collector-Probe-Fehler enthalten erwartete Discovery-Nichttreffer und sind keine Geräte-Ausfallquote.
GENA-Abo, Initialnachricht und Alter der letzten Nachricht stehen getrennt bereit.
Langes Nachrichtenalter allein bedeutet keinen Fehler: Ohne Topologieänderung kann ein
gültiges Abo ruhig bleiben. Zur Einordnung zusätzlich Quellenfrische, SOAP-Topologie und
Collector-Heartbeat prüfen. Die konkreten SSDP-/GENA-Einsatzbedingungen stehen im
[Betriebshandbuch](betrieb.md).

Die statische Dashboardprüfung vergleicht Metrikreferenzen mit dem generierten Katalog,
Gerätenamen, Auswahlvariablen, Einheiten und Aktualitätsfilter. Ein erfolgreicher JSON-/Vertragscheck
ersetzt weder die tatsächliche Grafana-Darstellung noch den Vergleich realer Abfragewerte.
Der nachgewiesene Stand wird bei [SM-014/015](planung.md) geführt.

## Quellen

Offizielle Dokumentation, recherchiert am 28.09.2026. Gerätebefunde stammen ausschließlich aus
der oben beschriebenen lokalen Erhebung und sind keine allgemeine Sonos-API-Garantie.

- M1: [Prometheus Storage](https://prometheus.io/docs/prometheus/latest/storage/)
- M2: [Prometheus HTTP API](https://prometheus.io/docs/prometheus/latest/querying/api/)
- M3: [Grafana Prometheus data source](https://grafana.com/docs/grafana/latest/datasources/prometheus/)
- M4: [Prometheus Metric and label naming](https://prometheus.io/docs/practices/naming/)
- M5: [VictoriaMetrics Single Node](https://docs.victoriametrics.com/victoriametrics/single-server-victoriametrics/)
- M6: [Timescale Continuous aggregates](https://docs.timescale.com/use-timescale/latest/continuous-aggregates/create-a-continuous-aggregate/)
- M7: [Grafana PostgreSQL data source](https://grafana.com/docs/grafana/latest/datasources/postgres/)
- M8: [Prometheus Recording rules](https://prometheus.io/docs/prometheus/latest/configuration/recording_rules/)
- M9: [Prometheus Alerting overview](https://prometheus.io/docs/alerting/latest/overview/)
