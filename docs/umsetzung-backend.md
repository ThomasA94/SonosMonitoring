# Backend-Umsetzung

Stand: 30.09.2026. Dieses Dokument beschreibt die Implementierung; der maßgebliche
Anforderungs- und Prüfstatus steht unter [Umsetzungsstand Backend](planung.md#umsetzungsstand-backend).
Code und XML-Dokumentation sind Englisch, Projektdokumentation Deutsch.
Das Frontend wurde in einem nachfolgenden Auftrag ergänzt; siehe [Frontend-Umsetzung](umsetzung-frontend.md).

## 1. Architektur und Verantwortungen

| Projekt | Verantwortung |
|---|---|
| Platform | Stabile Aggregate-, Tenant-, Routen-, Stream- und Busnamen ohne Framework-Abhängigkeit |
| Contracts | Eigenständige HTTP-DTOs ohne Referenz auf andere Anwendungsprojekte |
| Domain | Monitoring- und Medienvorgänge mit fachlichen Commands, Aggregaten und dauerhaften Events |
| Application | Commandausführung, stabile Identitäten und schmale Query-, Collector- und Mediengeräte-Ports |
| Infrastructure | Devbone-/Outbox-Komposition, Mongo-Abfragen, Sonos-Collector und optionaler Mediengateway samt PostgreSQL-Koordination |
| Api | HTTP-Verträge, Fehlerabbildung, lokale Zugriffskontrolle und Host-Komposition |
| Projections | Eigenständiger standardmäßiger RabbitMQ-zu-MongoDB-Worker ohne Domain-Referenz |

Slices liegen jeweils unter `Monitoring`, `Collection`, `Metrics` und `MediaControl`.
Transport, Aggregatentscheidung, externe Aufrufe, Zustandscache und Darstellung von Metriken
haben getrennte Verantwortungen. Eigene persistente Framework-Adapter oder Eventconsumer
wurden nicht eingeführt. Der Projektionsworker benötigt nur Standard-Raw/Merged-Dokumente;
ein zusätzliches Derived-Modell mit dupliziertem Zustand ist deshalb nicht nötig.

Die API greift für Policies auf das dokumentierte `Merged_DeviceMonitoring`-Format zu.
Quelle der Wahrheit für Policies bleibt der Event Store; Sonos bleibt Quelle seiner Messwerte.
Ein Cache-Snapshot ist keine atomare Aufnahme aller Geräte. Das separate Frontend
verwendet ausschließlich Contracts und HTTP.

## 2. Fachliches Modell

Ein Betreiber möchte die numerische Überwachung eines durch Discovery identifizierten Geräts
aktivieren. Er gibt die stabile UDN ohne `uuid:` und ein Intervall an. Die aktuellen
IP-Adressen und Messwerte sind keine Eingaben des Aggregats.

Das Aggregat besitzt die Regeln:

- Geräteschlüssel `RINCON_` plus ASCII-Großbuchstaben, Ziffern und Unterstriche; maximal 128 Zeichen.
- Intervall 5–300 Sekunden.
- Erste Aktivierung erzeugt den Stream. Eine erneute eigenständige Aktivierung eines aktiven
  Geräts ist ein Konflikt. Ein eigener Änderungsvorgang für aktive Intervalle ist noch nicht modelliert.
- Deaktivierung ist eine eigene Absicht und nur für eine aktive Policy zulässig.
- Reaktivierung darf ein neues Intervall setzen, aber die Gerätebindung nicht wechseln.
- Der Lautsprecher muss dafür nicht erreichbar sein. Keine Sonos-Aufrufe unter Aggregat-/Datenbanksperren.

Ein Gerät erhält deterministisch einen eigenen Stream: SHA-256 über
`SonosMonitoring/device/v1/` plus kanonischem Geräteschlüssel; die ersten 16 Bytes bilden
die GUID für `DeviceMonitoringId.With`. Diese Abbildung ist ein dauerhafter Vertrag.
IP-Wechsel erzeugen keine neue Policy. Es gibt kein unbeschränktes Haushalts-Sammelaggregat.

Der erste Eventvertrag heißt `sonosmonitoring.device_monitoring.created`,
danach `...enabled` beziehungsweise `...disabled`, jeweils Schreibversion 1.
Alle drei enthalten vollständige resultierende Policywerte, damit die Standardprojektion
ohne Domain-`Apply`-Aufrufe korrekt arbeiten kann. `Apply` rekonstruiert alte Fakten
ohne erneute fachliche Bewertung. Deaktivieren löscht den Stream nicht.

Der feste Tenant `SonosMonitoring` wird ausschließlich serverseitig gesetzt.
Dies ist ein Backend für eine Installation; daraus folgt keine Mehrbenutzer- oder
Mandantenisolationszusage. Die spätere Einführung mehrerer Installationen/Nutzer erfordert
eine eigene Zugriffs- und Migrationsentscheidung.

## 3. HTTP-Verträge

Standardadresse `http://127.0.0.1:48080`. Zeitstempel verwenden UTC-Offsets, numerische
Werte sprachunabhängiges JSON. Fehlermeldungen besitzen maschinenlesbare Codes.

| Methode / Pfad | Verhalten |
|---|---|
| GET /health/live | Prozess beantwortet Anfragen; Sonos-Ausfälle machen ihn nicht tot |
| GET /health/ready | Bei aktiviertem Backend: PostgreSQL-Schema und Mongo erreichbar, maximal drei Sekunden |
| GET /api/collection | Momentaufnahme mit Geräten, Quellenstatus, Suchbereichen, Heartbeat und Cachekapazität |
| GET /api/collection/export | JSON-Datei mit Schemafassung, Aufnahmezeit und reduzierten Kennungen; kein Liveabruf |
| GET /api/collection/history | Dauerhafte Beobachtungsänderungen mit Cursor, Quellenqualität und Erfassungslücken |
| GET /api/alerts | Validierte interne Warnungen und positive Erholungsmeldungen aus Prometheus |
| GET /metrics | Prometheus-Textformat mit pseudonymisierten Gerätekennungen |
| GET /api/metrics/catalog | Exportierte Metriknamen, Typen und Beschreibungen |
| GET /api/metrics/history/catalog | Historienselektoren, Einheiten, Scope und Labelschlüssel |
| POST /api/monitoring/{deviceKey}/enable | Aktivierung; Body `intervalSeconds`, optional `expectedToken` |
| POST /api/monitoring/{deviceKey}/disable | Deaktivierung; Body `expectedToken` erforderlich |
| GET /api/monitoring/{deviceKey} | Policyprojektion; vor erstmaliger Projektion 404 |
| GET /api/monitoring/{deviceKey}?minSequence=N | 202 mit `projection.pending` und Retry-After, bis die eigene Streamposition mindestens N erreicht |
| GET /api/metrics/history | Begrenzte Metrikauswahl, ein Gerät, Zeitbereich und Schrittweite |

Schreibanfragen benötigen `Idempotency-Key` als nichtleere GUID im D-Format. Der HTTP-Schlüssel
wird mit `RequestId.With` in die Framework-Identität überführt. GUID, Zielgerät und ursprüngliche
Eingabe bei Retries beibehalten. Der Gültigkeitsbereich ist genau der Policy-Stream.

**Vertrag für Wiederverwendung eines Schlüssels:** Der erste erfolgreiche Commit gewinnt.
Eine später abweichende Eingabe mit demselben Schlüssel liefert den ursprünglichen Beleg;
sie ändert nichts und wird nicht als neue Aktion ausgeführt. Das gilt auch über Prozessneustarts.
Der Client muss für eine andere Absicht eine neue GUID verwenden. Es gibt keine Anwendungstabelle
mit einem konkurrierenden Idempotenzstatus.

Bei der ersten Aktivierung ist `expectedToken: null` die Vorbedingung „Stream existiert noch nicht“.
Für spätere Aktionen den Token des bestätigten letzten Commits verwenden. Null schaltet
die Konkurrenzprüfung nicht aus. Validierungsfehler ergeben 400, fachliche/Konkurrenzkonflikte
409, fehlende Daten 404, Infrastrukturfehler 503 beziehungsweise einen sicheren generischen Fehler.
Interne Ausnahmen und Konfiguration werden nicht an Clients ausgegeben.

Erfolgreiche Writes liefern **202 Accepted** mit `aggregateId`, `concurrencyToken`,
`eventSequence`, `alreadyCommitted` und `queryUrl`.
Der Commitbeleg bestätigt PostgreSQL einschließlich Outbox, nicht die Sichtbarkeit in MongoDB.
Clients dürfen die Receipt-URL begrenzt nachfragen. Kein unbegrenztes Warten unter einer
Aggregatsperre und keine stillen synchronen Konsistenzversprechen.

**Frontendergänzung, 29.09.2026:** Der Policy-GET liefert additiv `concurrencyToken` aus
dem gespeicherten Event genau an der zurückgegebenen `eventSequence`. Damit kann ein Client
auch nach Neuladen bestehende Regeln ändern. Ist die Projektion älter als der Stream,
gehört der Token weiterhin zum angezeigten Stand; eine konkurrierende Änderung wird beim
Command abgelehnt. Der Token wird nie aus der Sequenz abgeleitet. Fehlt das passende Event,
bleibt er `null` und die UI erlaubt keine Änderung. Dieser zusätzliche Zugriff lädt den
kurzen Policystream über den vorhandenen Framework-Reader; es werden keine Messdaten geladen.
Nach eigenen Writes bleibt der Commitbeleg die bevorzugte Tokenquelle.
Die Metrikhistorienantwort verwendet jetzt einen benannten Contracts-Typ bei unveränderter JSON-Form.

Beispiel mit bereits entdecktem, hier nur illustrativem Geräteschlüssel:

```powershell
$key = [guid]::NewGuid().ToString()
$receipt = Invoke-RestMethod http://127.0.0.1:48080/api/monitoring/RINCON_EXAMPLE/enable -Method Post -Headers @{ 'Idempotency-Key' = $key } -ContentType application/json -Body '{"intervalSeconds":15}'
Invoke-RestMethod ("http://127.0.0.1:48080" + $receipt.queryUrl)
```

### Snapshot und Diagnoseexport (SM-006)

`GET /api/collection/export` liefert `application/json; charset=utf-8` als Attachment
`sonosmonitoring-snapshot.json` mit `Cache-Control: no-store`. Es gilt dieselbe lokale/API-Key-
Zugriffssperre wie für die übrige API. Der Endpunkt liest genau eine Cacheansicht; er startet
weder Discovery noch Geräteabrufe und benötigt keine Persistenzdienste. Auch ein deaktivierter
oder noch nicht gelaufener Collector kann einen leeren Export mit ehrlichen Null-Zeitpunkten liefern.

Der Dokumentrahmen enthält `format: "sonosmonitoring.collection-snapshot"`, `schemaVersion: 1`,
`capturedAtUtc`, `privacy: "reduced-identifiers-v1"` und `snapshot`. Die verschachtelte Struktur
entspricht dem Collection-Vertrag mit den folgenden Reduktionen:

- Geräte-UDNs einschließlich Koordinator-/Satelliten-/Elternreferenzen erhalten `device-N`.
  Dies gilt auch für Mitglieder, die der Collector nie selbst erreicht hat.
- Geräte- und gemeldete Mitgliedsadressen erhalten gemeinsam `address-N`; CIDRs werden `range-N`.
  Diese Felder sind im Export ausdrücklich keine verwendbaren Netzwerkziele.
- Raumnamen, Gruppenkennungen und Interface-Namen werden `room-N`, `group-N` beziehungsweise
  `interface-N`. Modellnamen entfallen (`null`), da das Gerät dort beliebigen Text liefern kann.
- Fehlende optionale Werte bleiben `null`. Bekannte Fehlercodes bleiben erhalten; unbekannte
  Kategorien werden `other_error`. Quellennamen stammen aus festen Kategorien.

Eine frische Aliaszuordnung pro Download erhält Gleichheit und Referenzen innerhalb der Datei.
Es gibt keine gespeicherte Zuordnungstabelle, keinen Hash der Originalkennung und keine
garantierte Identität über mehrere Exporte. Zufällig gleiche Aliase in verschiedenen Dateien
beweisen keine Gerätegleichheit. Parameter wie `raw=true` schalten die Reduktion nicht aus.
Zeitpunkte, Zähler, Sichtbarkeitsflags, Gruppenzusammenhänge, Quellenqualität, Teilfehler und
Discovery-/Kapazitätsgrenzen bleiben erhalten. Die Datei ist damit datensparsamer, aber nicht
anonym: Zeiten, Zähler und Strukturen können weiterhin Rückschlüsse erlauben.

`capturedAtUtc` bezeichnet die Aufnahme der Cacheansicht, nicht den Zeitpunkt einer atomaren
Haushaltsabfrage. `lastSuccessUtc`, `lastAttemptUtc` und `isStale` gehören zur damaligen
Quellenbeobachtung; Offline-Leser dürfen sie nicht als aktuelle Erreichbarkeit auslegen.
Das gesamte Dokument einschließlich des wiederverwendeten verschachtelten DTO-Vertrags gehört
zur Schemafassung. Künftige Collection-Vertragsänderungen müssen auch auf Exportkompatibilität
und Reduktion geprüft werden; inkompatible Änderungen erfordern eine neue Schemafassung.
Offline-Leser müssen Format und unterstützte Schemafassung vor der Interpretation prüfen.

Die Anwendung speichert keine Exporthistorie und importiert die Datei nicht in Livecache oder
Event Store. Der Download ist eine abgeleitete Leseansicht und erzeugt keine Domain-Events.
Die JSON-Datei kann ohne laufende API in der grafischen Offlineansicht des Webhosts gelesen werden.
Speicherung und Auswertung siehe
[Runbook](betrieb.md#diagnoseexport-sichern-und-offline-lesen).

## 4. Lokaler Zugriff

Ohne `Access:ApiKey` wird ausschließlich der tatsächliche Loopback-Socketpeer zugelassen.
`X-Forwarded-For` wird nicht als Vertrauensbeweis verwendet. Mit einem mindestens
32 Zeichen langen API-Key müssen **alle** Anfragen `X-Api-Key` tragen, auch Healthchecks und
Scrapes. Der Vergleich nutzt gleichlange Hashes und einen konstantzeitigen Vergleich.

Die Standardbindung bleibt Loopback. Für expliziten LAN-/Containerzugriff Binding,
`AllowedHosts`, Schlüssel und einen TLS-Endpunkt beziehungsweise VPN konfigurieren.
Dies ist ein einfacher Installationsschlüssel ohne Benutzerrollen; kein fertiges
Mehrbenutzer-Sicherheitsmodell. Secrets ausschließlich extern setzen.

## 5. Collector und Resilienz

`Collector.Enabled=false` und leere Zielbereiche sind der sichere Auslieferungszustand.
Aktivierung erfolgt in `.local/backend.json` beziehungsweise über Umgebungsvariablen:

```json
{
  "Collector": {
    "Enabled": true,
    "AllowedCidrs": ["192.168.6.0/24"],
    "SeedAddresses": [],
    "PollInterval": "00:00:15",
    "DiscoveryInterval": "00:10:00",
    "StaleAfter": "00:00:45"
  }
}
```

Nur ausdrücklich freigegebene IPv4-CIDRs werden per Unicast abgefragt. Bekannte Adressen
und Seeds kommen früh; vollständige Suchen mischen Adressen der Bereiche, deduplizieren
Überlappungen und beenden sich nicht nach dem ersten Gerät.
Gerätebeschreibung wird vor Diagnoseabrufen erneut validiert. Neue UDN an alter IP wird
nicht als Messung des alten Geräts verbucht.

Keine DNS-Ziele, keine automatischen Redirects und kein HTTP-Proxy. Nur feste GET-Pfade
auf Port 1400: `/xml/device_description.xml`, `/status/ifconfig`, `/status/wireless` und
`/status/enetports`, ergänzt um ausschließlich lesende SOAP-POSTs:
`GetZoneGroupState` an `/ZoneGroupTopology/Control`, `GetTransportInfo` an
`/MediaRenderer/AVTransport/Control` sowie `GetVolume`/`GetMute` an
`/MediaRenderer/RenderingControl/Control`. Rendereraktionen werden nur bei passend
angebotenem Service und fest erlaubtem Controlpfad verwendet; angebotene URLs werden
nicht als beliebige Ziele übernommen. InstanceID 0 und Masterkanal sind fest vorgegeben.
XML-DTDs/externe Entitäten sind gesperrt. Beschreibungen müssen Sonos/ZonePlayer, UDN und
Topologieservice erkennen lassen. Das beweist Protokollform, nicht Kryptographie oder
Modell-/Firmware-Kompatibilität aller Geräte.

| Einstellung | Vorgabe |
|---|---|
| PollInterval | 15 s; untere Planungstaktung für bekannte Geräte |
| DiscoveryInterval | 10 min |
| RequestTimeout | 2 s je Versuch |
| SweepTimeout | 90 s Gesamtabbruch je Runde |
| MaxParallelism | 16 gleichzeitige Geräte |
| MaxScanAddresses | 1024 deduplizierte Zieladressen |
| MaxResponseBytes | 256 KiB |
| MaxKnownDevices | 256 im flüchtigen Cache |
| MaxTopologyCandidates | 32 zusätzliche Topologie-Adressen pro Runde; erlaubt 1–256 |
| StaleAfter | 45 s |

Vollsuchrunden interleaven bekannte Ziele und die bereits netzübergreifend angeordneten
CIDR-Hosts, deduplizieren sie und rotieren nach den tatsächlich gestarteten Probes.
Ein Abbruch durch `SweepTimeout` beginnt die nächste Vollsuche deshalb am nächsten
Ziel. Reguläre Pollrunden für bekannte Geräte bleiben davon unabhängig. Der Cursor
ist flüchtig und beginnt nach einem Collector-Neustart bzw. Konfigurationswechsel neu.

### Optionale SSDP-Erkennung (SM-002, SM-016, SM-019)

SSDP bleibt standardmäßig aus. `Collector.SsdpEnabled=true` aktiviert vor jeder Vollsuche
je einen M-SEARCH auf den ausdrücklich konfigurierten `SsdpInterfaceAddresses`. Diese Liste
enthält **eigene lokale IPv4-Adressen des Collectors**, keine Geräteadressen und keine CIDRs.
Das Multicast-Suchpaket erreicht das jeweilige lokale Segment; `AllowedCidrs` begrenzt die
anschließenden HTTP-Geräteabrufe, nicht die Empfänger dieses Multicast-Pakets.
Sie wird nicht aus `AllowedCidrs` oder automatisch aus allen Netzwerkadaptern abgeleitet.
Auch mit SSDP bleiben erlaubte Ziel-CIDRs Pflicht; danach läuft die vollständige begrenzte
Unicast-Suche in allen Bereichen. Ein früher SSDP-Fund beendet diese Suche nicht.

Beispiel für einen Collector, dem `192.168.6.10` tatsächlich lokal zugewiesen ist:

```json
{
  "Collector": {
    "Enabled": true,
    "AllowedCidrs": ["192.168.6.0/24"],
    "SsdpEnabled": true,
    "SsdpInterfaceAddresses": ["192.168.6.10"],
    "SsdpSearchWindow": "00:00:03",
    "SsdpMaxDatagrams": 64
  }
}
```

Höchstens acht eindeutige, tatsächlich lokal zugewiesene IPv4-Adressen sind zulässig.
Loopback verlangt wie die HTTP-Simulatoren `AllowLoopback` und Umgebung `Testing`.
Die Interfaces suchen parallel über an ihre Adresse gebundene UDP-Sockets mit kurzlebigen
Quellports. Ziel ist fest `239.255.255.250:1900`, Suchtyp `ZonePlayer:1`, `MX: 1`.
Das Empfangsfenster beträgt 2–10 s (Default 3 s) und muss kleiner als `SweepTimeout` sein.
Je Interface werden 1–256 Datagramme akzeptiert (Default 64); auch unzulässige oder zu große
Pakete verbrauchen dieses Budget. Nur Pakete bis 8192 Bytes werden zur Auswertung behalten.
Keine passiven NOTIFY-Listener, keine Multicast-Gruppenmitgliedschaft und keine Hintergrundwarteschlange.

Die Anwendung beschränkt die Multicast-TTL bewusst auf 1 für lokale Segmente. Das ist eine
engere Projektgrenze als die allgemeine UPnP-Empfehlung; geroutete Zielnetze werden weiterhin
per Unicast durchsucht. Ein Suchpaket pro Interface und Vollsuchlauf hält die Last begrenzt;
UDP-Verluste werden durch spätere Suchen und die Unicast-Suche aufgefangen.

Die Prüfung verlangt eine vollständige ASCII-/CRLF-Antwort `HTTP/1.1 200 OK`, eindeutige Header,
passenden Suchtyp und eine syntaktisch gültige RINCON-USN. `LOCATION` muss exakt auf
`http://<UDP-Absender>:1400/xml/device_description.xml` zeigen. DNS, andere Hosts/Ports/Pfade,
Credentials, Query und Fragment werden verworfen. Nur nutzbare Hosts aus den bereits
freigegebenen Suchbereichen werden bevorzugt abgefragt; Kandidaten werden nach Adresse
dedupliziert. Die beworbene USN wird nicht als Identitätsnachweis gespeichert: erst die
HTTP-Gerätebeschreibung entscheidet über Aufnahme und stabile UDN. Der vorhandene
HTTP-Transport folgt weiterhin keinen Redirects.

`GET /api/collection` liefert `ssdp` mit Aktivierung, Suchstart/-abschluss, Kandidatenzahl,
Anzahl fehlgeschlagener Interfaces, Limitindikator und sicherem Fehlercode. Kandidaten sind
noch keine gefundenen Geräte. Keine Antwort ist ein erfolgreicher leerer Suchlauf und
beweist keinen Geräteausfall. Socketfehler einzelner Interfaces erhalten die anderen
Ergebnisse; die Unicast-Suche läuft weiter. `response_limit` zeigt ein ausgeschöpftes
Datagrammbudget und damit möglicherweise unvollständigen Empfang,
`socket_failure` Teilfehler, `search_cancelled` den Abbruch durch Runde/Host.
Ein erfolgreicher SSDP-Empfang belegt weder HTTP-Erreichbarkeit noch einen GENA-Rückweg.

Referenzen (29.09.2026): [UPnP Device Architecture 2.0, Abschnitt 1.3](https://upnp.org/specs/arch/UPnP-arch-DeviceArchitecture-v2.0-20140901.pdf)
und [SoCo-Discovery](https://github.com/SoCo/SoCo/blob/master/soco/discovery.py).
Die Prüfungen mit lokalen UDP-Simulatoren ersetzen keine reale Multicast-/Firewallabnahme.

### Fehlerbehandlung der HTTP-Erfassung

Es gibt höchstens einen Zusatzversuch für Transportfehler, Timeouts, HTTP 408/429/5xx.
Retry-After wird innerhalb einer begrenzten Wartezeit berücksichtigt. Wiederholt fehlerhafte
Diagnosen bekommen eine begrenzte Pause mit Zufallsanteil. Beschädigte Payloads und dauerhafte
HTTP-Ablehnungen werden nicht sofort mehrfach gelesen. Runden überlappen nicht; es gibt
keine unbeschränkte Arbeitswarteschlange.

Die beobachtete Diagnosequelle hat eigenen Erfolg, Versuchs-/Erfolgszeit, Fehlercode und
Stale-Status. Fehlende Daten werden nicht zu Nullwerten. Rohcounter bleiben erhalten;
Raten werden nicht aus unvollständigen Proben erfunden. Nach Ablauf des Gültigkeitsfensters
werden Counter nicht mehr exportiert. Policy-disabled/-unavailable stoppt den Counterexport
sofort. Heartbeat und Quellenzeiten erlauben die Unterscheidung zwischen Collector-Lücke
und Gerätefehler; noch keine automatische Störungs-/Alarmbewertung.

Mit aktiviertem Devbone-Backend werden nur aktivierte Policies diagnostisch gepollt.
Discovery/Identifikation bleibt aktiv. Eine nicht vorhandene Policy deaktiviert Diagnosen.
Policy-Lesefehler überspringen Diagnosen und erhalten alte Werte. Ohne aktiviertes
Persistenzbackend steht der lesende technische Collectorbetrieb für alle freigegebenen
gefundenen Geräte zur Verfügung.

Policyänderungen wirken erst nach Projektion und nächster Pollentscheidung; Broker-/Worker-Lag
kann diesen Zeitraum verlängern. Die API zeigt die ausstehende Projektionsposition.
Dies ist keine sofort wirksame Sicherheitsberechtigung. Das effektive Intervall ist mindestens
`Collector.PollInterval`; für Policies unter 15 s die Basistaktung entsprechend konfigurieren.
Anonyme Sonos-Ziele sind keine Benutzerberechtigungen.

Loopback-Ziele sind produktiv gesperrt. Die explizite Option `AllowLoopback` funktioniert
ausschließlich in Umgebung `Testing` und dient dem lokalen HTTP-Simulator.

### Gruppentopologie (SM-002, SM-003, SM-017, SM-019)

Nach erfolgreicher Geräteidentifikation fragt der Collector die Topologie vor der numerischen
Diagnose ab. Sie gehört wie die Geräteidentifikation zur Liveerfassung und bleibt unabhängig
von der Policy für numerische Zähler aktiv. Pro Gerät läuft jeweils nur ein HTTP-Aufruf;
Topologie verwendet dasselbe Request-, Größen- und Rundenbudget. Sie hat einen eigenen
Poll-/Backoff-Zustand: bei Erfolg frühestens nach `PollInterval`, bei Fehlern exponentielle Pause
bis 300 s plus weniger als 1 s Zufallsanteil. Kein sofortiger SOAP-Zusatzversuch.
Ein Quellenfehler wird aufgezeichnet; danach können Zähler und andere Geräte weiterlaufen.

`GET /api/collection` ergänzt jedes Gerät um `topologyStatus` und `groups`. Eine Gruppe enthält
`groupId`, `coordinatorDeviceId` und `members`; Mitglieder enthalten `deviceId`, optional
`roomName`, `isInvisible`, `parentDeviceId` für explizit verschachtelte Satelliten und
`reportedAddress` für eine kanonische gemeldete Beschreibungsadresse.
Die Gruppenkennung ist keine dauerhafte Gerätekennung. Fehlende Sichtbarkeitsangaben bleiben
`null`. Satellitenzuordnung belegt keine konkrete Stereo-, Sub- oder Surroundrolle.

Jede Antwort bleibt dem berichtenden Gerät zugeordnet; unterschiedliche oder veraltete
Gerätesichten werden nicht zu einer vermeintlich konsistenten Haushaltstopologie verschmolzen.
Nur eine vollständig gültige Antwort ersetzt alle Gruppen dieses Reporters. Fehler erhalten
den letzten guten Stand einschließlich Erfolgszeit; `StaleAfter` markiert ihn veraltet.
IP-Wechsel desselben Geräts erhalten dessen Identität. Eine Quelle, deren Gerätebeschreibung
nicht mehr validierbar ist, wird nicht erneut nach Topologie gefragt.

SOAP-Envelope und eingebettetes Topologie-XML werden getrennt ohne DTDs/externe Entitäten
gelesen. Unterstützt sind `ZoneGroupState/ZoneGroups`, `ZoneGroups` und die ältere Form mit
direkten Gruppen unter `ZoneGroupState`. Doppelte Gruppen-/Mitgliederkennungen, fehlende
Koordinatoren unter den direkten Mitgliedern und mehr als `MaxKnownDevices` Mitglieder
einschließlich Satelliten verwerfen die gesamte Antwort. Namen/Gruppenkennungen sind auf
256 Zeichen begrenzt. Aus `Location` wird ausschließlich eine kanonische IPv4-Adresse einer
exakten URL `http://<IPv4>:1400/xml/device_description.xml` übernommen. Andere Schemata,
DNS, Credentials, abweichende Ports/Pfade, Query und Fragment ergeben `reportedAddress: null`,
ohne ansonsten gültige Gruppeninformationen zu verwerfen. Die rohe URL wird nicht gespeichert.
Mitglieder ohne eigene erfolgreiche Gerätebeschreibung sind lediglich Referenzen, kein
Erreichbarkeitsnachweis. Gemeldete Adressen außerhalb der CIDRs bleiben als Referenz sichtbar.

Der Protokollabgleich beruht auf der [Community-Servicebeschreibung](https://sonos.svrooij.io/services/zone-group-topology)
und der [SoCo-Implementierung](https://github.com/SoCo/SoCo/blob/master/soco/zonegroupstate.py),
geprüft am 29.09.2026. Das ist keine offizielle universelle Sonos-Kompatibilitätszusage.
Insbesondere können Satelliten ältere Topologiesichten liefern. Reale Geräteantworten,
Gruppenwechsel und Rollen sind vor Produktabnahme gegen Sonos zu prüfen.

### Discovery aus Topologiehinweisen (SM-002, SM-019)

Jede Erfassungsrunde berücksichtigt nach Seeds und bekannten Geräten weitere Adressen aus
dem letzten Topologie-Snapshot. Gerätebeschreibung und Topologie des Reporters müssen frisch
und zuletzt erfolgreich sein. Fehlgeschlagene/veraltete Quellen behalten ihre Anzeige,
liefern aber keine neuen Abrufziele. Derselbe Snapshot bestimmt die gesamte Auswahl einer Runde;
neu empfangene Topologiewerte können frühestens in der nächsten Runde genutzt werden.

Die Auswahl akzeptiert ausschließlich nutzbare Hosts innerhalb der konfigurierten CIDRs.
Netzwerk-/Broadcastadressen werden bei Präfixen bis /30 ausgeschlossen; /31 und /32 verwenden
die bestehenden Hostregeln. Nur die feste Gerätebeschreibung wird nachgeladen. Angegebene UDN
und Raumname werden niemals zur Identität oder zum Zustand des abgefragten Geräts erklärt:
die tatsächliche HTTP-Beschreibung entscheidet. Eine neue IP derselben UDN erhält den
bestehenden Cacheeintrag und `firstSeenUtc`. Eine abweichende UDN wird als tatsächlich
gefundenes Gerät behandelt, ohne dem behaupteten Topologiemitglied zugeschrieben zu werden.

Mehrfach genannte Adressen und bereits geplante Seeds/bekannte Adressen werden dedupliziert.
`MaxTopologyCandidates` begrenzt zusätzliche Hinweise pro Runde (Default 32, maximal 256,
effektiv außerdem durch `MaxScanAddresses` begrenzt). Eine rotierende Auswahl verhindert,
dass dieselben unerreichbaren ersten Hinweise dauerhaft alle weiteren verdrängen. Jeder
ausgewählte Hinweis erhält mindestens `PollInterval` Pause; fehlgeschlagene Identifikationen
werden exponentiell bis 300 s plus weniger als 1 s Zufallsanteil zurückgestellt. Ein Erfolg
übergibt das Gerät an die normale bekannte-Geräte-Erfassung. Es gibt keine dauerhafte
Arbeitswarteschlange; Auswahl und Retry-Zustand sind auf den freigegebenen Hostumfang begrenzt.

`topologyDiscovery` unter `GET /api/collection` zeigt `lastSelectionUtc`, `candidateCount`
und `limitReached`. Es beschreibt geplante zusätzliche Abrufe, keine erfolgreich gefundenen
Geräte. Zurückgestellte Hinweise sind keine Ausfallmeldung. Periodische Vollsuchen behalten
alle freigegebenen CIDR-Hosts; Kandidatenlimit und Backoff schränken diese Vollsuche nicht ein.
Die bisherigen Parallelitäts-, Antwortgrößen- und Rundengrenzen gelten auch für Folgeabrufe.

## 6. Historie und Grafana

Prometheus ist der ausgewählte erste numerische Speicher. `Metrics:PrometheusUrl` aktiviert
den historischen API-Client, zum Beispiel `http://127.0.0.1:48090/`.
Er verwendet denselben Server wie die provisionierte Grafana-Datenquelle.

Historienparameter: `metric`, `deviceKey`, optional `networkInterface` oder `sourceFilter`,
`start`, `end`, `stepSeconds`. Der geschützte `GET /api/metrics/history/catalog` liefert alle
26 erlaubten Selektoren mit Einheit, Scope und Labelschlüssel aus dem gemeinsamen
`MetricHistoryCatalog`. Dazu gehören Byte-/Paket-/Fehler-/Drop-Counter und deren Raten,
Beschreibungs- und Quellenzustände, GENA, Probedauer, Playerlautstärke und Mute.
Quellenfilter sind auf die sieben exportierten Quellen beschränkt; Interfacefilter
gelten ausschließlich für Interfacewerte. Kein freier PromQL-Proxy.
Höchstens 11.000 Schritte, mindestens 15 s Schrittweite, maximal 16 Serien und 2 MiB Antwort.
Raten verwenden `rate(counter[2m])` in Bytes/s; sie sind Interface-, keine Peer-/Audiotransportraten.
Counter-Resets werden an Prometheus übergeben. Fehlende Messungen werden nicht nachträglich gefüllt.
Live-Raten im Snapshot verwenden dagegen die tatsächliche Differenz zweier erfolgreicher
Erfassungen desselben Interfaces und fehlen nach Erstprobe, Reset oder langer Lücke.

Die Gerätepolicy steuert alle fünf Detailquellen, nicht Beschreibung und Topologie.
`DeviceDiagnosticsCollector` plant sie unabhängig mit eigenem Backoff und einem begrenzten
Transportretry. 404/405/501 gelten als `not_supported`; Parsingfehler als `invalid_response`.
Ein Quellenfehler blockiert die übrigen Quellen nicht. Alte Werte bleiben mit Fehlerstatus
sichtbar; Lautstärke/Mute und Ethernetlink werden nur bei erfolgreicher frischer Quelle
numerisch exportiert. Das Diagnose-Frischefenster ist
`max(StaleAfter, 3 * max(Policyintervall, PollInterval))`.

Der Exportkatalog wird aus `CollectorMetricsFormatter` generiert; Vergleichsbasis ist
`contracts/metrics.json` mit aktuell 34 Definitionen. Drei getrennte Info-Gauges
ergänzen lesbare Geräte-/Firmwaremetadaten und frische WLAN-/Wiedergabezustände.
Die bestehenden Counter behalten ausschließlich ihre stabile pseudonyme Gerätekennung;
Raumumbenennungen teilen die Historie nicht auf. Grafana verknüpft die Messreihen mit
den Namen, statt Kennungen als Legende anzuzeigen. Der Historienselektorkatalog bleibt
bei 26 numerischen Auswahlwerten. Die Details stehen in der
[Dashboardanleitung](metriken.md#10-detaillierte-grafana-dashboards-und-lesbare-gerätenamen).
Betriebszähler zählen Beschreibungsproben einschließlich
Discovery-Nichttreffern. Discovery-Gauges zählen deduplizierte abgeschlossene Proben des
letzten vollständigen Suchlaufs, auch dessen Teilergebnis bei Abbruch. Details und
Einheiten stehen in [metriken.md](metriken.md#9-umgesetzte-erweiterung-vom-30092026).

Aufbewahrung wird pro Umgebung über `PROMETHEUS_RETENTION` festgelegt. Der Compose-Default
`400d` folgt dem Plan; Tests dürfen `7d` wählen. Abfrageauflösung verdichtet die Rückgabe,
nicht die gespeicherten Rohdaten. Speicherlimits, die unbemerkt die zugesagte Jahresdauer
verkürzen, werden nicht gesetzt. Kapazitätsmessung bleibt vor Dauerbetrieb erforderlich.

Siehe [Metrikbetrieb](../infra/prometheus/README.md) und
[Prometheus API](https://prometheus.io/docs/prometheus/latest/querying/api/).
Die [Speicherdokumentation](https://prometheus.io/docs/prometheus/latest/storage/) beschreibt
Retention und lokale TSDB. Ein TSDB-Ausfall erzeugt beim Pull-Verfahren Lücken; es gibt
keinen persistenten Messwertpuffer, der unbeobachtete Daten nachträglich erzeugt.

### Störungserkennung (SM-005, SM-011, SM-019)

Prometheus ist Besitzer der technischen Regeln in `infra/prometheus/sonos-alerts.yml`.
Die gemeinsame Vorlage `infra/prometheus/prometheus.yml.template` lädt sie über `rule_files`;
Compose bindet die Datei read-only ein. `scripts/metrics.ps1 Up` übernimmt geänderte
Compose-Mounts; `Restart` erstellt die Container mit aktuellen Mounts/Flags neu und erhält
die benannten Datenvolumes. Regeln werden im Intervall von 15 Sekunden ausgewertet.

| Regel | Bedingung | Mindestdauer | Nachlauf nach Wegfall der Bedingung |
|---|---|---|---|
| `SonosApiUnavailable` | `up=0` für den konfigurierten API-Scrape | 2 Minuten | 1 Minute |
| `SonosCollectorInterrupted` | API abrufbar, Collector aktiviert, Heartbeat fehlt oder ist zu alt | 2 Minuten | 1 Minute |
| `SonosDeviceDescriptionUnavailable` | Letzter Beschreibungsabruf fehlgeschlagen und derselbe Collector beobachtbar | 5 Minuten | 2 Minuten |

`sonos_collector_enabled` wird auch ohne ersten Heartbeat als 0/1 ausgegeben.
`sonos_collector_heartbeat_max_age_seconds` enthält aufgerundet SweepTimeout + PollInterval
+ 5 Sekunden Schedulerreserve; dadurch gelten längere freigegebene Runden nicht automatisch
als Unterbrechung. Die Aufnahme dieser beiden Gauges ist eine kompatible Erweiterung des
generierten Metrikkatalogs. Der bestehende Snapshot-Dateivertrag bleibt unverändert.
Die Recording Rule `sonos:collector_observing` verbindet erfolgreichen API-Scrape, Aktivierung
und frischen Heartbeat nach `job,instance`. Sie ist eine Voraussetzung für neue Gerätealarme.
Ein Ausfall eines Collectors unterdrückt keine Beobachtungen einer anderen Instanz.

Die Gerätebedingung bedeutet durchgehend fehlgeschlagener *letzter* Beschreibungsabruf in den
Scrapes; sie zählt keine Einzelversuche. XML-/Identifikationsfehler fallen ebenfalls darunter.
Ein gültiger Abruf bricht die Wartephase ab. Nach bereits ausgelöstem Alarm hält `keep_firing_for`
den Zustand bei kurzer Erholung beziehungsweise Datenlücke noch fest; ein Wiederfehler in
dieser Zeit bleibt dieselbe Alarminstanz. Das verhindert kurze Zustandswechsel, ist jedoch
keine Zustellungsdeduplizierung. Bestehende Gerätealarme können während des Nachlaufs noch
neben einem Erfassungsalarm sichtbar sein. Die Regeln haben stabile Namen/Labels; keine
Zeitstempel oder Fehlversuchzähler werden Teil der Alarmidentität.

Grenzen: Fehlende Geräte-Serien erzeugen keine erfundenen neuen Ausfälle. Entfernte Targets,
leerer Gerätecache nach Neustart, Collector-Deaktivierung oder längere Datenlücken können
einen bestehenden Gerätealarm nach dem Nachlauf verschwinden lassen. Das ist **kein Beleg
für eine erfolgreiche Wiederherstellung**; Originalbeobachtungen und Collectorstatus prüfen.
Ein kaputtes API-Key-/Scrape-Setup erscheint als API-Ausfall. Ein ausgefallener Prometheus
kann sich nicht selbst zuverlässig melden. Uhrzeiten von API und Prometheus müssen synchron
sein. Policies steuern weiterhin nur die numerische Diagnose; diese Regel bewertet alle
bekannten Gerätebeschreibungen und bietet noch keine Schlaf-/Wartungsausnahmen.

Zustände sind in Prometheus unter `/alerts` und über die geschützte eigene API `/api/alerts`
sichtbar. Die drei zusätzlichen positiven Erholungsregeln und deren begrenztes Abfragefenster
stehen unter [interne Alarmmeldungen](#interne-alarmmeldungen-sm-011).
Eine Alarm-UI, dauerhafte Störungsvorgänge, Quittierungen, Alarmmanager und externe Zustellung
sind nicht Bestandteil des aktuellen Backends. Es gibt keinen zweiten Regelbesitzer in Grafana.
Die Schwellen sind versionierte Startwerte und müssen vor realer Benachrichtigung am Bestand
kalibriert werden. Sie beweisen weder Hardwaredefekte noch Audioaussetzer.

Referenz: [Prometheus 3.13 Alerting Rules](https://prometheus.io/docs/prometheus/3.13/configuration/alerting_rules/)
und [Regeltests](https://prometheus.io/docs/prometheus/3.13/configuration/unit_testing_rules/).
Der Prüfablauf verwendet die tatsächlich in Compose gepinnte Version samt Digest.

## 7. Tests und Qualität

`scripts/verify.ps1` bündelt gepinnten Restore, nichtinkrementellen Release-Build mit
Warnungen als Fehler, Formatprüfung, Katalogkompatibilität **und** Katalogaktualität sowie Tests.
Die CI ruft denselben Ablauf mit echter Infrastruktur auf.
Produktionsprojekte erzeugen XML-Dokumentation, nullable ist aktiv, Paket-Lockdateien liegen bei.

- Unit: fachliche Zustandsübergänge/Ablehnungen, Event-Rehydration, Produktionsserializer,
  Requestidentität und Konkurrenzprüfung am Executor, Parser/Zielgrenzen/Cache,
  Metrikexposition, Historienparameter, Zugriffsschutz und transitive Architekturgrenzen.
- Integration: echte atomare Events/Outbox, deduplizierte Requests, konkurrierende Deaktivierung;
  zusätzlich gezielte Wiederzustellungs-/Rebuild-Prüfungen im zugehörigen Testverzeichnis.
- E2E: echte HTTP-API und Frameworkdienste; simulierte Sonos-Server mit Teilfehlern,
  Wiederanlauf, gespeicherten Zeitpunkten und Erholung. Frontend- und Browserprüfungen sind
  in der [Frontend-Umsetzung](umsetzung-frontend.md#reproduzierbare-prüfungen-und-auslieferung) beschrieben.

Die Testfixture erstellt GUID-benannte Datenbanken und isolierte RabbitMQ-Adressen.
Sie löscht nur ihre eigenen entbehrlichen Ressourcen. Keine normalen Sonos-Geräte
werden absichtlich gestört. Resultate unter `artifacts/test-results/` sind lokale Belege;
der dokumentierte aktuelle Prüfstand steht ausschließlich in der Planung.

## 8. Bekannte Grenzen

Noch nicht umgesetzt: Stereo-/Heimkinointerpretation, Netzwerkmodus/Signalwerte,
dauerhafte fachliche Störungsvorgänge mit Quittierung und eine dauerhafte Snapshotbibliothek.
Die optionale Mediensteuerung benötigt noch die in `planung.md` geführte reale Geräte-
und Resilienzabnahme.
Der gewählte Benachrichtigungskanal ist ausschließlich die interne API. GENA und der dauerhafte
Beobachtungsverlauf sind in den folgenden Ergänzungen beschrieben.
Keine Cloudintegration, keine erfundenen Meshverbindungen.

Gerätecache und bekannte IP-Adressen überleben keinen Prozessneustart; Discovery stellt
sie erneut her. Policies, Metrikhistorie und das aktivierte Beobachtungsjournal sind davon
getrennt persistent; Journal-Checkpoints füllen den flüchtigen Gerätecache nicht wieder auf.
Reale Modell-/Firmwareabnahme, geroutete Linux/LXC-Netztests, Dauertest/Ressourcenmessung,
einjährige Aufbewahrung und Restore auf dem tatsächlichen Betriebsziel bleiben eigenständige Abnahmen.

## Backend-Ergänzungen vom 29.09.2026

Die folgenden Ergänzungen gehören zum damaligen V1-Backendauftrag. Das Frontend und
die Mediensteuerung wurden später in eigenen Slices ergänzt; Mehrmandantenbetrieb
bleibt außerhalb des Umfangs. Der Prüfstand wird in `planung.md` getrennt von der
Implementierung geführt.

## Topologie-Ereignisempfang (SM-016)

`Collector:Gena:Enabled=true` aktiviert einen eigenen HTTP-Callback-Listener. Er benötigt
eine explizite, von den Geräten erreichbare IPv4-`CallbackAddress`; `ListenAddress` und `Port`
bestimmen die lokale Bindung (Standard `0.0.0.0:48081`). Keine automatische NAT-Erkennung.
Loopback und Port 0 sind nur für isolierte Tests erlaubt. Beispiele stehen in
`infra/backend.example.json`. Aktivierung setzt einen aktivierten Collector voraus.

Nur aktuell per HTTP identifizierte, erlaubte Geräte mit angekündigtem
`ZoneGroupTopology:1`-Ereignispfad `/ZoneGroupTopology/Event` erhalten eine Subscription.
Andere angekündigte URLs werden nicht aufgerufen. Der Listener startet vor `SUBSCRIBE`.
Ein zufälliger Callback-Pfad je Lease, der tatsächliche Socketpeer, SID, NT/NTS, SEQ und
begrenztes XML werden geprüft. Ein früher Initial-Callback darf der Antwort vorausgehen;
seine SID muss anschließend mit der Antwort übereinstimmen. Fehlender SEQ 0 wird nicht als
bestätigter Rückweg ausgegeben. Doppelte/veraltete Events werden ohne neue Wirkung bestätigt;
Sequenzlücken lösen eine aktuelle SOAP-Abfrage aus. Event-Payloads ersetzen niemals direkt
den aktuellen Gruppenstand. Maximal vier Callback-Verarbeitungen und vier Subscription-
Vorgänge laufen gleichzeitig; zusätzliche Topologieabrufe nutzen die bestehende Gerätesperre.

Die tatsächlich gewährte Lease bestimmt Ablauf und vorgezogene Erneuerung. Erneuerungen
senden SID/TIMEOUT ohne CALLBACK/NT. Fehlgeschlagene neue Subscriptions verwenden Backoff;
eine abgelehnte/abgelaufene Lease wird ersetzt. Ohne Initial-Callback wird nach dem doppelten
Initialbudget, mindestens fünf Sekunden, neu angemeldet. Eventstille nach einem bestätigten
Initial-Callback ist für sich kein Fehler. Beenden versucht begrenzt `UNSUBSCRIBE`.
Der Gerätestatus `gena` zeigt Lease, letzte Meldung und begrenzte Fehlercodes getrennt von
HTTP-Erreichbarkeit. Polling bleibt bei Callback-Ausfällen aktiv.

Protokollgrundlage: [UPnP Device Architecture 2.0, Eventing](https://upnp.org/specs/arch/UPnP-arch-DeviceArchitecture-v2.0-20140901.pdf)
und die [SoCo-Ereignisimplementierung](https://github.com/SoCo/SoCo/blob/master/soco/events_base.py).
Die Simulation belegt keine Firmware-, Routing- oder Firewallabnahme auf dem Zielnetz.

## Beobachteter Änderungsverlauf (SM-010)

`History:Enabled=true` und eine extern bereitgestellte PostgreSQL-`ConnectionString`
aktivieren ein separates, versioniertes Schema `sonos_history`. Die Erstellung erfolgt
explizit mit `scripts/initialize-history.ps1`; bestehende/falsche Schemata werden beim Start
abgelehnt. Es gibt keine automatische Migration und keinen Reset. Das Schema darf in
derselben gesicherten Datenbank wie der Event Store liegen, verändert aber keine Frameworktabellen.

Architekturentscheidung: Dieses Journal speichert beobachtete Telemetrieänderungen, keine
fachlichen Benutzeraktionen und keine Projektion des Event Stores. Monitoring-Policies
behalten unverändert ihren Command-/Event-/Outbox-Weg. Genau ein Collector pro Journal ist
vorgesehen. Ein Standardintervall von zwei Sekunden vergleicht kanonische Zustände mit dem
dauerhaften Checkpoint: Geräteadresse, Quellenqualität und Gruppen-/Koordinator-/Satelliten-
zuordnung je Reporter. Counter, Namen und reine Reihenfolgenänderungen erzeugen keinen Eintrag.
Schnellere Zwischenzustände können zusammenfallen; die Historie ist ausdrücklich kein
verlustfreies Geräteevent-Audit. Beobachtungs- und Aufzeichnungszeit werden getrennt ausgegeben.

Neue Geräte erzeugen `baseline`, geänderte Evidenz `change`; Neustart, Erfassungsunterbrechung,
Wiederaufnahme und Speicherunterbrechung erzeugen `gap`. Wiederaufnahme ist kein Beweis für
Geräteerholung. Unbeobachtete Zwischenstände werden nicht erfunden. Checkpoint und Einträge
werden gemeinsam transaktional geschrieben. Die Erfassung läuft bei Speicherfehlern weiter;
die letzte erfolgreiche Aufzeichnungszeit bleibt über die API sichtbar.

Aufbewahrungsentscheidung: Standard 90 Tage und maximal 20.000 Einträge; die zuerst erreichte
Grenze gilt. Konfigurierbar sind 1–400 Tage, 100–100.000 Einträge und 1–60 Sekunden Sampling.
Keine zusätzliche Verdichtung: Einträge bleiben unverändert bis zur Löschung. Höchstens 256
aktuelle Gerätezustände und Checkpoints werden behalten; nach Verdrängung beginnt die nächste
Beobachtung mit einer neuen Baseline. Ein einzelner Zustand ist auf 256 KiB beschränkt.
Diese Grenzen sind unabhängig von der mindestens einjährigen numerischen Metrikaufbewahrung.

Bei aktivierter Historie wird die 256er-Grenze auch gegen geladene Einstellungen und
vor jedem Konfigurations-Save geprüft. Ungültige gespeicherte Einstellungen verhindern
den Start mit klarer Fehlermeldung; ein ungültiger HTTP-Save liefert 400 und ersetzt
weder Datei noch laufende Konfiguration. Ohne Historie gelten die allgemeinen Collectorgrenzen.
Die Mengenretention läuft bei neuen Einträgen sowie beim ersten erfolgreichen Schreibzyklus
einer Storeinstanz. Unveränderte Folgeproben überspringen den Bestandsscan; die
Altersretention bleibt aktiv. Neue Einträge und Mengenbereinigung bleiben in derselben
Transaktion, und eine geänderte Mengenbegrenzung greift beim ersten Schreiben nach Neustart.

`GET /api/collection/history?after=0&limit=100&deviceId=RINCON_...` liefert aufsteigende
Sequenzen, `nextCursor`, den global frühesten erhaltenen Cursor, `lastRecordedAtUtc` und die
Retention. `deviceId` ist optional; globale Lücken bleiben auch bei Gerätefilter sichtbar.
Maximal 200 Einträge und ein begrenztes Payload-Budget je Seite; mit `nextCursor` fortsetzen.
Ein überholter Cursor beweist keine vollständige ältere Historie. Nicht konfiguriert oder
Speicher nicht erreichbar ergibt 503, ungültige Parameter 400. Zugriffsschutz entspricht der
Live-API; Kennungen sind hier nicht wie beim Diagnoseexport reduziert.

## Interne Alarmmeldungen (SM-011)

Der Nutzer hat **ausschließlich interne Meldungen über die API** ausgewählt. `GET /api/alerts`
nutzt dieselbe `Metrics:PrometheusUrl` wie die numerische Historie. Die URL ist eine HTTP(S)-
Basisadresse mit abschließendem `/`, ohne Zugangsdaten, Query oder Fragment. Ausschließlich
der feste Prometheus-Pfad `/api/v1/rules` wird gelesen. Prometheus bleibt Besitzer der Regeln.

Vor einer erfolgreichen Antwort müssen beide Recording Rules, die drei Störungsregeln und
die drei Erholungsregeln vorhanden, fehlerfrei und höchstens 60 Sekunden alt sein.
Pending/Firing/Recovered, Beginn, Auswertungszeit, Schwere, Scope und stabile Identitäten
werden ausgegeben. `identity` bezeichnet Regel, Job, Instanz und pseudonymes Gerätelabel;
`occurrenceIdentity` ergänzt den Beginn der ursprünglichen Störung auf Unixsekundenbasis.
Wiederholtes Lesen und ein erneuter positiver Nachweis derselben Störung erzeugen dadurch
keine neue Vorkommnisidentität. Für die Anzeige einer Nachricht pro Übergang kann ein Client
nach `(occurrenceIdentity, state)` deduplizieren. Ein neues Störungsvorkommnis erhält eine neue ID.
`recoveryAtUtc` bezeichnet den Beginn der positiven Erholungsphase, nicht den exakten
Gerätezeitpunkt. `recovered` wird erst nach der Bestätigungsminute ausgegeben; der erfolgreiche
Bewertungszeitpunkt steht separat in `evaluatedAtUtc`.
`deviceLabel` lässt sich über `metricLabel` der geschützten Live-Geräteansicht zuordnen.
Der reduzierte Diagnoseexport enthält dieses beständige Label nicht. Fremde Annotationen,
freie Links und beliebige Prometheus-Labels werden nicht an Clients weitergereicht.

Nicht konfiguriert ergibt 503; veraltete, fehlerhafte oder unerreichbare Regeln ergeben 502.
Antwortlimit 2 MiB, maximal 1.024 Meldungen, Zeitbudget zehn Sekunden. Die aktuelle Meldungsliste
ist keine dauerhafte Inbox: keine Quittierungen oder externe Zustellung. Das Verschwinden einer
Meldung bedeutet nur „nicht mehr aktiv“, niemals automatisch „Gerät wieder gesund“; fehlende
Serien und abgeschaltete Erfassung können ebenfalls Alarme beenden.

Explizite Erholungsmeldungen benötigen eine zuvor tatsächlich feuernde Störung, die inzwischen
weder Pending noch Firing ist, und eine Minute lang positive passende Beobachtungen. Für die API
ist dies `up=1`, für den Collector die bestehende Beobachtbarkeitsregel, für Geräte zusätzlich
ein erfolgreicher und gemäß konfigurierter Quellenalterung frischer Beschreibungsabruf.
`sonos_source_observation_fresh` exportiert diese vorhandene Quellenqualität; C# bewertet keine
zweite Alarmregel. Fehlende Geräte-/API-Serien, deaktivierter Collector und veraltete letzte
Erfolge liefern keine Geräteerholung. `recovered` bestätigt nur die benannte technische Quelle,
keine störungsfreie Audiowiedergabe.

Prometheus erhält mit `sonos:alert_firing_started_timestamp_seconds` den Beginn tatsächlich
feuernder Vorkommnisse in seiner TSDB. Die Erholungsregeln verwenden maximal 24 Stunden dieser
Evidenz; pro Regel/Ziel wird das jüngste passende Vorkommnis ausgegeben. Das ist ein bewusst
begrenztes Abfragefenster, keine lückenlose Meldungshistorie. Clients müssen innerhalb dieses
Fensters abfragen. Ein TSDB-Verlust, eine neue Regelinstallation ohne Vorgeschichte oder
fehlende Beobachtung liefern keine erfundene Erholung. API-Neustarts benötigen keinen lokalen
Meldungsspeicher. Fachliche Störungsvorgänge, Quittierung und eine dauerhafte Inbox bleiben
separate, nicht priorisierte Erweiterungen.

Die Zuordnung verwendet ganze Sekunden, weil die tatsächlich eingesetzte Version diese in
`ALERTS_FOR_STATE` schreibt: [Prometheus v3.13.3, AlertingRule.Eval](https://github.com/prometheus/prometheus/blob/v3.13.3/rules/alerting.go#L502-L504).


## Laufende Erfassungskonfiguration (SM-021, DEC-017)

GET /api/collection/configuration liefert revision und settings; POST auf demselben Pfad
akzeptiert diese Struktur plus einen nichtleeren GUID-Header Idempotency-Key. Beide sind durch
denselben API-Key-/Loopbackzugriff wie die vorhandenen Endpunkte geschützt und senden no-store.
revision ist der zuvor gelesene opaque GUID-Stand; der Initialstand hat die leere GUID.
Nach erfolgreichem Speichern entspricht die neue Revision der Request-ID. Gleiche letzte ID
und identische Einstellungen ergeben erneut Erfolg ohne Worker-Neustart. Veraltete Revision
oder geänderte Nutzlast unter derselben ID: 409 request.conflict. Es gibt kein unbeschränktes
Requestarchiv; ein Retry nach einer weiteren Speicherung wird als Konflikt behandelt.
400 collection.configuration.invalid bezeichnet unzulässige Einstellungen;
503 collection.configuration.storage einen fehlgeschlagenen dauerhaften Schreibzugriff.
Die HTTP-Antwort enthält keine Dateipfade, Schlüssel oder Ausnahmedetails.

Technische Installationseinstellungen gehören zur Collector-Orchestrierung, nicht zur fachlichen
Gerätepolicy. Sie werden in einer separaten Datei gespeichert, nicht als Projektion oder als
zweiter fachlicher Schreibweg. Das ist die begründete Abgrenzung gemäß DEC-017; fachliche
Monitoringänderungen nutzen weiterhin Devbone. Web referenziert ausschließlich Contracts.
Die gemeinsame CollectorConfigurationValidator-Implementierung gilt für Start und Übernahme.

Die API benötigt einen beschreibbaren dauerhaften CollectionSettings:Path (Umgebung:
CollectionSettings__Path). Default: .local/collector-settings.json relativ zum ContentRoot.
Vor der ersten Speicherung gelten Collector-Deploymentwerte; danach ersetzt die gespeicherte
vollständige Konfiguration diese Defaults. AllowLoopback stammt immer ausschließlich aus dem
Testing-Deployment. Beschädigte gespeicherte Dateien führen zu einem sichtbaren Startfehler.
Ein API-Prozess ist alleiniger Besitzer dieser Datei; mehrere schreibende API-Replikate sind
nicht unterstützt. Ein temporäres Dokument wird auf demselben Dateisystem geflusht und atomar
umbenannt. Erst nach dem Commit werden die alten Worker beendet und die neuen gestartet.
Ein Clientabbruch nach Commit unterbricht die Aktivierung nicht. Ein Prozessabsturz in diesem
Fenster lädt beim Wiederanlauf die neue Revision. Der Livecache wird bewusst neu aufgebaut.

## Lokale Mediensteuerung (SM-013)

Die [SM-013-Planung](planung.md#ergänzung-lokale-mediensteuerung-30092026) bleibt die
maßgebliche Anforderungs- und Statusquelle. Der optionale Slice trennt fachliche
Medienabsicht, technische Ausführungskoordination und lokalen Sonos-Gateway. Eine
API-Annahme ist weder eine Geräteantwort noch ein beobachteter Zielzustand. Der
Medienvorgang hat einen eigenen Eventstream mit dem Aggregatnamen `MediaOperation`
und der kanonischen Identity mit Präfix `mediaoperation`. Die Eventverträge heißen
`sonosmonitoring.media_control.operation.created`,
`sonosmonitoring.media_control.dispatch_started` und
`sonosmonitoring.media_control.result_recorded`, jeweils Version 1. Benannte
Commands und die Eingabevalidierung gehören dem Aggregat;
die technischen Schritte werden getrennt koordiniert. RabbitMQ-Wiederzustellung oder
MongoDB-Rebuild lösen keine Sonos-Schreiboperation aus.

`MediaControl:Enabled` ist ein Deployment-Schalter und standardmäßig `false`.
Bei Aktivierung benötigt die API das explizit provisionierte Schema `sonos_media` Version 1.
`MediaControl:ConnectionString` kann eine getrennte PostgreSQL-Verbindung angeben;
ein leerer Wert verwendet die konfigurierte Event-Store-Datenbank. Das Schema hält die
dauerhafte Betreiberfreigabe, deren Generation, Annahmereihenfolge, Zielmengen,
Ausführungsschritte, Ziel-Epochen (`target_epochs`) und Unklarheitssperren.
Die Freigabe beginnt nach Schemaerstellung
immer `false`; der Deployment-Schalter allein gibt keine Lautsprecheraktion frei.
`MaxPending=128`, `LifetimeSeconds=30` und `ExecutionSeconds=30` stehen im lokalen
Beispiel. Die Anwendung prüft zulässige Bereiche beim Start. Das Gateway begrenzt seine
einzelnen Netzwerkaufrufe zusätzlich; die Collector-Abfragezeit von zwei Sekunden im
Beispiel ist eine andere Konfiguration und kein Gesamtbudget für einen Medienvorgang.

Für die explizite Erstinstallation nach dem Release-Build:

```powershell
dotnet src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll --write-media-control-schema .local/media-control-schema.sql
./scripts/initialize-media.ps1 -ContainerName sonosmonitoring-dev-postgres-1 -Database sonosmonitoring_dev -User sonosmonitoring_dev
```

Der erste Befehl erzeugt nur SQL und greift auf keine Datenbank zu. Das zweite Skript
erstellt ausschließlich einen fehlenden `sonos_media`-Namespace. Der lokale
`start-backend.ps1 Up` führt die additive Einrichtung nach dem Event-Store-Schema aus;
bei vorhandenem Namespace prüft es Tabellen, Spalten und Versionsmarker und lehnt
inkompatible oder teilweise Zustände ab. Bereits vorhandene `.local/backend.json`
wird nicht ersetzt: `MediaControl:Enabled` dort bewusst auf `true` setzen und die API
anschließend starten. Eine Schema-Erstellung ist keine Upgrade-Migration.

### Medien-HTTP-Verträge

Alle `/api/media`-Routen nutzen den bestehenden API-Key-/Loopbackschutz. Der Webhost
ruft sie ausschließlich über seinen serverseitigen API-Client auf. Jede POST-Anfrage
benötigt einen stabilen `Idempotency-Key` als nichtleere UUID im D-Format. Dieselbe
UUID mit veränderter Eingabe ergibt `409 media.idempotency.conflict`; nach einer
unklaren Antwort bleiben UUID, Ziel, Kontext und Body für einen ausdrücklichen Retry
identisch. Keine automatische Wiederholung einer Geräteaktion. Für jedes
Konfliktziel wird die aktuelle Epoche vor dem Domain-Commit erfasst; eine spätere
Risikoannahme erhöht sie. Ein verzögert nachkommender, zuvor nur fachlich
committeter Vorgang mit alter Epoche kann dadurch nicht später technisch
aufgenommen werden. Betreiberentscheidungen binden ebenfalls die gelesene
Freigabegeneration: Eine verspätete ältere Aktivierung kann einen neueren Entzug
nicht rückgängig machen.

| Methode / Pfad | Vertrag |
|---|---|
| `GET /api/media/control` | `MediaControlStatusDto`: dauerhafte Freigabe, Generation und genau benannte `MediaBlockDto`-Sperren |
| `POST /api/media/control/enable`, `/disable` | Eigene Betreiberentscheidungen mit `{}` und UUID; Entzug invalidiert wartende alte Generationen dauerhaft |
| `POST /api/media/control/accept-risk` | `AcceptMediaRiskRequest` mit konkreten `operationIds` und `confirmDelayedEffect=true`; Nachlesen allein entsperrt nie |
| `GET /api/media/players/{deviceKey}/state` | Frische `MediaPlaybackDto` mit `MediaTargetContextDto`, Zeit/Quelle/Metadaten, getrennten Player-/Gruppenwerten und `availableActions` |
| `GET /api/media/players/{deviceKey}/queue?offset=0&limit=20`, `/favorites?...` | Begrenzte `MediaPageDto` mit opaken Referenzen, Index und Queue-Revision |
| `POST /api/media/players/{deviceKey}/{action}` | `MediaActionRequest` mit ursprünglichem Kontext und aktionsspezifischen Feldern; `202 MediaOperationDto` |
| `GET /api/media/operations/{id}?minSequence=N` | Vorgangsphase, Code und zuletzt freigegebener Step; bei noch fehlender Projektion `202 projection.pending` |

Falls der fachliche Commit bereits vorliegt, die technische Aufnahme aber noch
nicht nachweisbar ist, liefert der API-Leseweg `202 media.admission.pending`.
Das ist kein Auftrag zum erneuten Senden mit einer neuen UUID.

Die benannten Aktionen sind `resume`, `pause`, `stop`, `volume`, `mute`, `unmute`,
`next`, `previous`, `seek`, `mode`, `crossfade`, `start-favorite`, `start-stream`,
`queue-play`, `queue-append`, `queue-remove`, `queue-reorder`, `queue-clear`,
`group-volume`, `group-mute`, `group-unmute`, `join` und `leave`. Nur die vom
aktuellen Playerzustand gelieferten `availableActions` sollen angeboten werden;
der Server prüft Fähigkeit, Haushalt, Rollen, Topologie, Quelle und Queue-Revision
vor Annahme erneut. `Value` ist eine absolute Player- oder Gruppenlautstärke bzw.
Crossfade-Einstellung, `PositionSeconds` eine absolute Suchposition, `Reference`
eine opake Medienreferenz oder der Zielkoordinator, `Index`/`NewIndex` gehören zu
Queueänderungen und `Mode` benennt einen Wiedergabemodus. `queue-play` bindet
eine opake Queuereferenz und deren Revision, keinen blind wiederverwendeten Index.
Der Antwortvertrag gibt keine rohe Wiedergabe-URL, Credentials oder UPnP-XML aus.
`start-stream` nimmt nur eine zuvor von der API ausgegebene opake Medienreferenz an;
die UI bietet dafür vorhandene Favoriten an, kein Freitextfeld für beliebige URLs.

Die technische PostgreSQL-Koordination weist einen alleinigen Executor über eine
dauerhafte Session mit Advisory Lock zu. Kurze Transaktionen ordnen Vorgänge nach
betroffenen Zielmengen und geben jeden geplanten schreibenden Schritt genau einmal
frei. Netzwerk-I/O zum Gerät findet außerhalb dieser Datenbanktransaktionen statt.
Ein vor dem Senden dauerhaft freigegebener Schritt wird nach Absturz, Antwortverlust,
Neustart oder Replay nicht blind erneut gesendet. Unklare Wirkung erzeugt eine
dauerhafte Zielsperre; ein erfolgreicher Read oder ein scheinbar passender Zustand
hebt sie nicht auf. Ein Entzug erhält eine neue Freigabegeneration und verwirft
wartende Vorgänge der alten Generation. Die Risikoannahme nennt exakte blockierende
UUIDs und bestätigt ausdrücklich mögliche verspätete Altwirkungen. Statuswerte
trennen `accepted`, `dispatch_started`, `device_acknowledged`,
`desired_state_observed`, `rejected`, `expired`, `revoked` und `outcome_unknown`.
`media.partial.revoked` erklärt, dass vor dem Entzug freigegebene Schritte bereits
am Gerät wirken können; der Step im Vorgangsstatus macht die Grenze sichtbar.
Die echte Geräte-/Restart-/Race-Abnahme ist separat in `planung.md` geführt.

Der Metrikkatalog enthält `sonos_media_configured`,
`sonos_media_coordination_available` und `sonos_media_operations{status}`.
Die Zählungen stammen aus der autoritativen Koordination und verwenden nur eine
begrenzte Statusdimension, keine Geräte- oder Vorgangs-IDs als Labels. Fehlende
Koordinationsverbindung wird als eigener Verfügbarkeitswert sichtbar. Namen und
Einheiten stehen im bestehenden [Metrikkatalog](../contracts/metrics.json).

### Angezeigtes Gruppenziel als Vorbedingung (SM-013.6)

`POST /api/media/players/{deviceKey}/join` akzeptiert additiv `destinationContext` in derselben
Form wie `context`. Es muss zum als `reference` gewählten Koordinator gehören. Quelle und Ziel
werden vor Annahme und Versand frisch geprüft. Neue Webaktionen senden den angezeigten
Zielkontext; ein veralteter Kontext ergibt einen Konflikt. Fehlt das optionale Feld bei einem
älteren Client, erfasst die API das Ziel wie bisher während der Annahme. Bestehende gespeicherte
Events und Wiederholungs-Fingerprints behalten ihre Semantik. Details und Prüfstand:
[SM-013.6](planung.md#gruppenverwaltung-und-wiedergabeziele-sm-0136-07102026).
