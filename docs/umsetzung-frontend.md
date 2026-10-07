# Frontend: Umsetzung und Prüfung

Stand: 02.10.2026. Anforderungen und Abnahmestand bleiben in [planung.md](planung.md)
maßgeblich. Der Auftrag zur vollständigen Frontendimplementierung priorisiert die V1-Oberfläche
für die vorhandenen Backendfähigkeiten (SM-003–SM-007, SM-010/011, SM-014/015, SM-018/020).
SM-009 (unbelegte Meshdaten) und SM-012 (Mehrmandantenbetrieb) bleiben spätere fachliche
Erweiterungen. Die optionale Mediensteuerung (SM-013) ist unten als gesonderter Slice
beschrieben; ihr tatsächlicher Abnahmestand steht in [planung.md](planung.md).

## Erweiterte Liveinformationen (SM-004/005/008/014/015/020/022)

Die Metrikauswahl liest den geschützten API-Katalog mit 26 Selektoren und dessen Einheiten.
Geräte-, Interface- und Quellenwerte erhalten passende Filter; eine leere Diagnosereihe
erklärt fehlende Geräteaktivierung und verlinkt zur Überwachungsregel. Quellenstatus,
Collector-Heartbeat und effektives Geräteintervall sind zusätzlich sichtbar.

Die Systemseite zeigt Sweepdauer, Probeversuche, Fehler und Timeouts sowie die
geprüften Adressen und gefundenen Geräte des letzten deduplizierten Discoverylaufs.
Der Hinweis zur Fehlerzählung erklärt, dass erwartete Discovery-Nichttreffer
enthalten sind und der Zähler deshalb keine Geräte-Ausfallquote darstellt.

Die Einstellungen bieten kurze SSDP-/GENA-Erklärungen und aufklappbare Schritte
für Einsatz, Netzvoraussetzungen und Erfolgskontrolle auf Deutsch und Englisch.
Die [Netzanleitung](discovery.md#ssdp-und-gena-im-alltag-sm-002-sm-016-sm-019)
beschreibt die Einrichtung ausführlich. GENA beschleunigt derzeit nur den
Topologieabgleich; die übrigen Diagnosen bleiben regelmäßige Abfragen.

Gerätedetails, Topologieknoten und zugängliche Liste verwenden dieselbe Darstellung für
Beschreibungs-, Diagnose- und Topologiequalität, Quellenalter und ausgewählte RX-/TX-Raten.
Weitere Details zeigen WLAN-Modus, Ethernetlink, technische Versionen, Wiedergabe,
Playerlautstärke/Mute und GENA-Zustand. Status und letzte gute Beobachtung bleiben getrennt.
Fehlende Daten sind unbekannt, deaktivierte oder nicht unterstützte Quellen werden erklärt.
Wiedergabe- und Boolzustände sind zentral auf Deutsch und Englisch übersetzt.

Kanten bleiben berichtete Gruppen-/Satellitenbeziehungen. Die Darstellung behauptet keine
gemessenen Netzwerkpfade. Knoten zeigen kompakte Zustände; Auswahl und Liste enthalten
die vollständigen Quellenalter und Details. Reporterdetails sind einklappbar, damit die
Karte auf kleinen Ansichten früh erreichbar bleibt. Periodische Liveupdates erhalten Knotenpositionen und Zoom;
die Liste macht die Informationen auch ohne Canvas zugänglich. Snapshotimporte lesen
die additiven Felder mit sicheren Defaults für ältere Exporte und prüfen neue Zahlenbereiche.
Maßgebliche Abnahme und Prüfbelege:
[planung.md](planung.md#erweiterte-metrik--und-zustandsanzeige-30092026).

## Lieferplan

1. Eigenständiger .NET-10-Webhost, Blazor Interactive Server und MudBlazor 9.10.0.
   Nur Contracts als Anwendungsreferenz; lokale Webassets, zentral gepinnte Pakete.
2. Responsive Navigation, deutsche/englische Ressourcen, persistierte Sprachauswahl,
   Lade-, Leer-, Fehler-, Verbindungs- und veraltete Zustände.
3. Liveübersicht, filterbare Geräteliste, Gerätedetails, Quellen und Interface-Zähler;
   reporterbezogene Cytoscape-Topologiekarte mit gleichwertiger Liste, Zoom und verschiebbaren Knoten.
4. Metrikdiagramme und Werte aus derselben API wie Grafana, explizite Messlücken;
   paginierter Beobachtungsverlauf und interne Meldungen mit positiven Erholungsnachweisen.
5. Versionierten Diagnoseexport herunterladen und begrenzt als getrennte Offlineansicht öffnen.
6. Überwachungsregeln aktivieren/deaktivieren: stabile Retry-Identität, echte Konflikte,
   Commitbeleg und begrenztes Warten auf Projektion.
7. Architektur-, Ressourcen-, Transport-, Sicherheits- und Verhaltenstests; Release-/Formatgate,
   Browserprüfung bei 360 und 1440 Pixeln, Betriebs- und Startdokumentation.

## Entscheidungen

- Interactive Server hält API-Schlüssel aus Browser und Browserstorage heraus. Der Webhost
  benötigt eine dauerhafte Browserverbindung; SignalR-Verbindungsverlust wird sichtbar angezeigt.
- Standard ist Loopback auf Port 48082. Für LAN ist ein eigener `Web:AccessKey` (mindestens
  32 Zeichen) und HTTPS erforderlich. Cookie-Anmeldung mit CSRF-Schutz, begrenzter Lebensdauer
  und Anmelderatenlimit. Der Backendschlüssel steht ausschließlich in `Api:ApiKey`.
  Dies ist ein Einzelinstallationszugriff, kein Benutzer-/Rollenmodell.
- Sprache: gespeicherte Auswahl vor unterstützter Browsersprache, sonst Deutsch;
  Datum und Zahlen folgen dieser Kultur. Die Browserzeitzone bestimmt die Ortszeit;
  jeder Zeitstempel nennt seinen UTC-Offset, die Seite nennt die erkannte Zone.
- Gruppen sind Beobachtungen je Reporter, keine Netzwerkpfade. Keine Zusammenführung
  widersprüchlicher Reporter zu einer erfundenen Gesamttopologie.
- Quellenqualität stammt aus dem API-Vertrag. Ein fehlgeschlagener UI-Abruf lässt den letzten
  Stand sichtbar, kennzeichnet ihn jedoch als nicht aktualisiert. Kein grüner Gesamtzustand
  allein aus einem alten Snapshot.
- Der Monitoring-GET erhält additiv einen optionalen `concurrencyToken` aus dem gespeicherten
  Event genau an der angezeigten Projektionsposition. Kein Ableiten aus Sequenznummern.
  Folgeaktionen übernehmen primär den Commitbeleg. Ein Konflikt verlangt erneutes Laden.
  Der zusätzliche Eventstream-Lesezugriff erfolgt nur für eine einzelne Policyansicht;
  er ist für kurze Policystreams vorgesehen, nicht für Messreihen.

## Referenzen

MudBlazor [Installation](https://mudblazor.com/getting-started/installation) und lokal
aufgelöstes Paket 9.10.0; Framework-Semantik aus der [versionsgebundenen Referenz](framework.md).

## Prüfstand

Tatsächliche Ergebnisse und verbleibende Abnahme stehen ausschließlich in der maßgeblichen
[Planung](planung.md#umsetzungsstand-backend).

## Start und Konfiguration

```powershell
dotnet restore SonosMonitoring.slnx --locked-mode
dotnet build SonosMonitoring.slnx -c Release --no-restore
dotnet run --project src/SonosMonitoring.Api --no-launch-profile
# Zweites Terminal:
dotnet run --project src/SonosMonitoring.Web --no-launch-profile
```

Browser: `http://127.0.0.1:48082`. Für den vollständigen Backendbetrieb zuerst die
bereits dokumentierten Schritte `scripts/start-backend.ps1 Up`, `Projections` und `Api`
ausführen; der Webhost läuft zusätzlich. Der Webhost führt keine Discovery durch.

| Einstellung | Standard / Bedeutung |
|---|---|
| `Urls` / `ASPNETCORE_URLS` | `http://127.0.0.1:48082` |
| `Api__BaseUrl` | `http://127.0.0.1:48080/`, feste HTTP(S)-Adresse der API |
| `Api__ApiKey` | Optionaler Backendzugang; nie an den Browser ausgegeben |
| `Web__AccessKey` | Ohne Eintrag nur tatsächlicher Loopback-Peer; mit Eintrag mindestens 32 Zeichen, Anmeldung auch lokal erforderlich |
| `AllowedHosts` | Standard `localhost;127.0.0.1;[::1]`; für LAN explizit den eigenen Hostnamen ergänzen |

Ein leer konfigurierter Webschlüssel ist ungültig; für Loopbackbetrieb die Einstellung
weglassen. Zugang, API-Adresse und TLS bleiben Deploymentkonfiguration. Die Erfassung wird
über den neuen Bereich **Einstellungen** bedient; siehe unten.
Keine Schlüssel in URLs, Repository oder Browserstorage hinterlegen.

Für LAN Kestrel mit HTTPS-Zertifikat konfigurieren, `Urls` auf die eigene HTTPS-Adresse
setzen und den eigenen Hostnamen erlauben. Nichtlokale HTTP-Anfragen werden auch mit Cookie
abgelehnt. Weiterleitungsheader werden bewusst nicht vertraut: Ein Proxy muss über eine
lokale Verbindung zum Webhost sprechen und selbst TLS/Zugriff gewährleisten, oder HTTPS
bis zu Kestrel verwenden. Die öffentliche Freigabe einer ungeschützten Loopback-Proxyroute
ist kein unterstützter Betrieb. Vor LAN-Freigabe `Web__AccessKey` setzen.
Container mit eigenem Netzwerk verwenden HTTPS bis Kestrel und einen konfigurierten
Backendhost; `127.0.0.1` im Container bezeichnet nur diesen Container.

Cookies sind HttpOnly und SameSite Strict, bei HTTPS zusätzlich Secure. Die Session gilt
höchstens acht Stunden; auch SignalR-Verbindungen enden mit Ablauf der Anmeldung.
Sprachauswahl ist ein eigenes einjähriges Cookie; Auswahl vor Browsersprache, sonst Deutsch.
CSRF-Token schützen Anmeldung, Abmeldung und Sprachwechsel; zehn Anmeldeversuche pro
Minute und tatsächlichem Peer sind erlaubt. Ein Mehrbenutzer-/Rollenmodell entsteht dadurch nicht.
ASP.NET-Data-Protection-Schlüssel bei dauerhaftem Containerbetrieb in einem geschützten
Volume erhalten; ohne Erhalt werden bestehende Sessions bei Austausch des Containers ungültig.

## Bedienung und Fehlerfälle

- Übersicht, Geräte, Gruppen, Meldungen und Erfassung fragen alle zehn Sekunden neu ab,
  ohne überlappende Anfragen. Navigation beendet die laufenden Abrufe. Pro Abruf gelten
  15 Sekunden und 4 MiB. Fehler lassen den letzten Stand sichtbar und kennzeichnen ihn.
- Die Karte zeigt die Sicht genau eines Reporters. Auswahl, Zoom und Ziehen funktionieren
  mit Maus/Touch; die fokussierte Karte unterstützt Pfeiltasten, +/− und 0. Die Listenansicht
  enthält dieselben Mitglieder und führt zu den gleichen Gerätedetails. Satelliten werden
  nur als ausdrücklich gemeldete Elternbeziehung dargestellt.
- Metriken bieten Stunde, Tag, Woche, Monat, Jahr und eigene UTC-Zeiträume. Die API bestimmt
  Einheit und Prometheus-Ausdruck. Die automatische Schrittweite begrenzt die Darstellung
  auf ungefähr 720 Abfragepositionen. Zeitraum, Einheit und tatsächliche Schrittweite stehen
  am Diagramm; nichtendliche und fehlende Werte unterbrechen die Linie. Exakte Werte sind
  in einer paginierten Tabelle zugänglich. Mit diesen Bounds und demselben Ausdruck ist
  ein Vergleich zum vorhandenen Grafana-Dashboard möglich.
- Der Änderungsverlauf lädt 50 Einträge je Anfrage, behält höchstens 1.000 in der aktuellen
  Ansicht und erlaubt dann den nächsten Abschnitt. Retentionsverluste und gespeicherte
  Erfassungslücken sind ausdrücklich sichtbar. Der Gerätefilter verwendet die stabile Kennung.
- Meldungen werden nach Vorkommnis und Zustand dedupliziert. Eine Erholung wird ausschließlich
  angezeigt, wenn die API `recovered` liefert. Keine Quittierung oder externe Zustellung.
- Snapshot-Uploads sind auf 4 MiB, JSON-Tiefe 32, Schema 1 und strukturell valide Daten
  begrenzt. Die Datei verbleibt nur in der aktuellen Ansicht; weder Restore noch Liveabfrage
  noch Commands werden daraus erzeugt. Keine Links zu Aliaskennungen in der Liveansicht.
- Monitoringaktionen behalten GUID, Ziel und Body bei Wiederholung einer unklaren Antwort
  in der offenen Ansicht. Nach bestätigtem Commit wird ausschließlich die Projektion abgefragt,
  maximal fünf Versuche je Betätigung. Erneutes Prüfen verwendet denselben Commitbeleg.
  Konflikte verlangen Neuladen; Folgeaktionen verwenden den echten Token des sichtbaren Stands.
  Ein Schließen/Neuladen der Browserseite verwirft den lokalen Aktionszustand. Vor einer neuen
  Aktion den neu geladenen Policystand prüfen; ein noch verzögerter Stand kann einen Konflikt
  auslösen, überschreibt aber keine konkurrierende Änderung.

## Reproduzierbare Prüfungen und Auslieferung

`scripts/verify.ps1` umfasst jetzt auch `SonosMonitoring.Web.Tests`; Architekturtests prüfen
die direkte/transitive Projektgrenze sowie das aufgelöste Paketmodell. Der ergänzte echte
Command-bis-Query-Test prüft den Token auch nach API-Neustart und verwendet ihn zum Deaktivieren.
`-IncludeInfrastructure` verwendet weiterhin ausschließlich die isolierten Backend-Testressourcen.

```powershell
npm ci --prefix tests/browser
npm exec --prefix tests/browser -- playwright install chromium
npm test --prefix tests/browser
node tests/browser/media.cjs
# Gegen das veröffentlichte Artefakt im Produktionsmodus:
$env:TEST_WEB_PUBLISHED = '1'
npm test --prefix tests/browser
# Optional mit bereits installiertem Edge:
$env:PLAYWRIGHT_CHANNEL = 'msedge'
npm test --prefix tests/browser
```

Die Browserprüfung startet einen API-Simulator und den echten Release-Webhost auf zufälligen
Loopback-Ports. Sie berührt keine Entwicklungsdaten, führt keine Gerätesuche durch und beendet
ihre eigenen Prozesse. Berichte/Screenshots: `artifacts/test-results/browser/`.
Die Tests decken Darstellung und Browserabläufe gegen echte HTTP-Verträge mit simulierten
Antworten ab; echte Persistenz wird separat durch die Backend-E2E geprüft.

`scripts/publish.ps1 -BuildImages` erstellt zusätzlich `sonosmonitoring-web:local` aus
`infra/Dockerfile.web`. `scripts/scan-images.ps1` berücksichtigt alle drei Hosts.
`scripts/smoke-web.ps1` prüft das Webimage als unprivilegierten Benutzer in einem
Container ohne Netzwerk einschließlich Startseite und lokal ausgelieferten Webassets.
CI führt Release-/Vertrags-/Testgates, Browserprüfung mit Chromium und Imageprüfungen aus.
Das ist eine gelieferte CI-Konfiguration, kein Nachweis einer bereits gelaufenen Remote-CI.

## Erfassung über die Webseite (SM-021)

Unter **Einstellungen** (/settings) lassen sich alle regulären Collector-Optionen ändern:
Aktivierung, mehrere IPv4-CIDRs, Seeds, Abfrage-/Discovery-/Frischeintervalle, Anfrage- und
Durchlaufzeitlimits, Adress-/Geräte-/Kandidaten-/Parallelitäts-/Antwortgrenzen, SSDP-Aktivierung,
lokale Schnittstellen und Empfangsgrenzen sowie sämtliche GENA-Routing-/Lease-/Zeitparameter.
Zeitfelder verwenden Sekunden. IPv4-Listen können zeilenweise oder kommasepariert eingegeben werden.
Die serverseitige gemeinsame Validierung bleibt maßgeblich, auch für direkte API-Aufrufe.

**Speichern und anwenden** persistiert zuerst atomar, beendet die bisherige Collector-Generation
einschließlich GENA-Abonnements und startet die neue. Die Livebeobachtungen werden dabei neu
aufgebaut; historische Beobachtungen und fachliche Gerätepolicies bleiben erhalten. Ein API-Neustart
ist nicht nötig. Bei einem Speicherfehler bleibt die aktive Generation erhalten. GENA benötigt
weiterhin reale Erreichbarkeit des Rückrufports; eine gültige Konfiguration bestätigt diese nicht.

Der Editor lädt nur beim Öffnen oder expliziten Neuladen. Automatisches Polling überschreibt
keine Eingaben. Bei unklarer Antwort bleiben Formular und Request-ID für dieselbe Wiederholung
fixiert. Konflikte verlangen **Neu laden**. Serverseitig bestätigte Validierungsfehler erlauben
Korrekturen. Ein Seitenwechsel verwirft nur den lokalen Entwurf; danach den gespeicherten Stand prüfen.
Die Form läuft durch den bestehenden geschützten Blazor-Circuit und den serverseitigen API-Client.

Zugangsschlüssel, Datenbank-/Brokerzugänge, TLS, Container-Netze und Prometheus-Retention sind
Host-/Infrastrukturkonfiguration. Der Testing-Schalter AllowLoopback ist bewusst kein Webfeld.
Gerätebezogene fachliche Überwachungsregeln bleiben im vorhandenen Command-/Event-Store-Weg.

## Mediensteuerung (SM-013)

`/media` ergänzt die Navigation um eine geschützte Liveansicht. Die gesonderte
Steuerfreigabe wird dort gezeigt und bewusst aktiviert oder entzogen. Sie ist nach
Schemaerstellung standardmäßig aus und benötigt zusätzlich die optionale
Backendkonfiguration `MediaControl:Enabled=true`. Bereits vorhandene
`.local/backend.json` wird durch den Starthelfer nicht ersetzt; die Einstellung muss
dort ausdrücklich ergänzt werden. Ein Medienpanel erscheint außerdem in den
Live-Gerätedetails. Importierte Offline-Snapshots erhalten keinen Steuerungsweg.
Beide Ansichten verwenden ausschließlich Contracts und den serverseitigen API-Client;
Browser und Blazor-Circuit sprechen Sonos-Geräte nicht direkt an.

Die Playerauswahl stammt aus dem aktuellen Collection-Snapshot und verwendet die
stabile UDN. Das Panel liest einen frischen `MediaPlaybackDto` samt ursprünglichem
`MediaTargetContextDto`, nennt die betroffenen Gruppenräume und trennt ausdrücklich
Player- von Gruppenlautstärke und Mute. Slider senden erst beim Betätigen von
**Lautstärke übernehmen**, nicht beim Verschieben. Quelle, Titel, Künstler, Album,
Position und Zeitstempel bleiben bei fehlender Geräteinformation unbekannt.
Das sichtbare Panel liest den Zustand ungefähr alle fünf Sekunden neu. Bedienbare
Aktionen stammen aus `availableActions` der API; fehlende Fähigkeiten bleiben
gesperrt. Die Mitgliederverwaltung bietet einzeln spielende Räume zum Beitritt an; die API prüft
Haushalt, Gruppenrollen und aktuelle Topologie erneut. Favoriten und Queue werden
seitenweise gelesen. Quellen-, Queue- und Gruppeneingriffe verlangen eine
Bestätigung mit Hinweis auf mögliche Wiedergabeunterbrechung und Queueänderung.
Queueaktionen tragen die gelesene Revision; `queue-play` verwendet die opake
Referenz des Eintrags. `start-stream` verwendet eine ausgewählte, vom Server
ausgegebene Favoritenreferenz; freie URLs können nicht eingegeben werden. Die
Bedienung ist auf Deutsch und Englisch zentral übersetzt.

Jede Medienaktion fixiert Ziel, Kontext, Parameter und UUID beim ersten Senden.
Nach einer unklaren HTTP-Antwort kann der Betreiber nur genau dieselbe Anfrage
erneut senden oder den ursprünglichen Vorgang nach seiner UUID lesen. Es gibt
keine automatische Wiederholung als neue Aktion. Ein `202` bedeutet dauerhaft
angenommen; `dispatch_started`, `device_acknowledged` und
`desired_state_observed` sind sichtbar getrennt. Der Status `outcome_unknown`
bleibt als mögliche verspätete Wirkung kenntlich. Automatische Vorgangsabfragen
enden nach zwölf Versuchen; manuelle Prüfung bleibt möglich. Der zuletzt
freigegebene Ausführungsschritt wird angezeigt. `media.partial.revoked` erklärt,
dass bereits freigegebene Schritte trotz Entzug noch wirksam bleiben können.
Der regelmäßige
Playback-Read entsperrt keinen unklaren Vorgang.

Die dauerhaften Sperren kommen aus `GET /api/media/control`, nicht aus dem
zufällig gerade angezeigten Wiedergabezustand. Die Risikoannahme auf `/media`
listet Status, ursprünglichen Wert und die genauen blockierenden Vorgangs-UUIDs;
sie ist nur für `outcome_unknown` verfügbar und erfordert ein eigenes Häkchen
für eine möglicherweise verspätete Altwirkung. `dispatch_started` wird angezeigt,
kann dort aber nicht zurückgerufen oder vorschnell entsperrt werden. Entzug und späteres
erneutes Aktivieren geben alte wartende Generationen nicht wieder frei. Ein
verlorener Antwortbeleg für Freigabe oder Risikoannahme wird nur mit derselben
UUID und demselben Body ausdrücklich wiederholt. Der bestehende Webzugang,
Cookie-CSRF-Schutz und HTTPS-Anforderungen gelten weiter.

Die Medienansicht verwendet flexible Felder, Umbruch und begrenzte Listen bei
360 und 1440 Pixeln; Regler, Buttons, Dialogersatz und Auswahlfelder sind per
Touch, Maus und Tastatur bedienbar und beschriftet. `tests/browser/media.cjs`
startet einen isolierten API-Simulator und den echten Webhost für diese Abläufe.
Simulator-/Persistenz-/Geräteabnahme und tatsächliche Prüfergebnisse stehen
ausschließlich in der [SM-013-Planung](planung.md#ergänzung-lokale-mediensteuerung-30092026).

## Grafische Topologie und Bibliotheksentscheidung (SM-003/004, DEC-016)

Die Ansicht **Topologie** (/groups) stellt genau einen Reporter dar. Koordinatoren, Mitglieder,
ausdrücklich gemeldete Satelliten-Eltern und Gruppenumrandungen werden aus demselben Snapshot
wie die Liste erzeugt. Unbeobachtete Referenzen sind grau, veraltete/fehlgeschlagene
Gerätebeschreibungen haben einen gelben/roten Rand. Anklicken oder Auswahlfeld öffnet Details;
Liveverknüpfungen gibt es nur für tatsächlich beobachtete Geräte. Offline-Snapshots verwenden
dieselbe Darstellung ohne Liveverknüpfungen. Positionsänderungen bleiben bei Aktualisierungen
derselben Struktur erhalten; Reporter-/Strukturwechsel oder **Neu anordnen** setzen das Layout neu.
Positionen gelten für die offene Ansicht und werden nicht dauerhaft gespeichert.

Verglichen wurden [Cytoscape.js](https://js.cytoscape.org/) und
[vis-network](https://visjs.github.io/vis-network/docs/network/). Beide unterstützen interaktive
Netzwerkgraphen. Cytoscape.js **3.34.3** wurde wegen integrierter Compound-Nodes für Gruppen,
Touchgesten, Datenaktualisierung und MIT-Lizenz gewählt. MudBlazor bleibt die Komponentenbibliothek
für die übrige UI; die vorhandenen Charts ersetzen keinen interaktiven Knotengraphen.
Die initiale Anordnung nutzt kompakte Spalten, auf schmalen Ansichten eine Mitgliederspalte.
Zoom, Pan, Knotenverschieben, Touch und eine vollständige Listenalternative sind verfügbar.
Resize-/DOM-Beobachter räumen Canvas und Eventhandler beim Verlassen der Ansicht auf.

Quelle der Produktorientierung: [SonoStats](https://apps.apple.com/us/app/sonostats/id6772244030),
erneut geprüft am 29.09.2026. Die gewünschte visuelle Orientierung ist übernommen; unbelegte
Routerpfade, Signalstärken, Stereo-/Heimkinorollen oder Peer-Durchsatz werden nicht erfunden.
Diese benötigen weiterhin eigene Mess-/Interpretationsnachweise (SM-008/009).

JavaScript und Lizenz liegen eingecheckt unter wwwroot/vendor/cytoscape; keine CDN-Anfragen.
Die exakt gepinnte npm-Abhängigkeit samt Integritäts-Lock liegt im Webprojekt. Reproduzierbares Update:

```powershell
npm ci --prefix src/SonosMonitoring.Web --ignore-scripts
npm run vendor --prefix src/SonosMonitoring.Web
npm run verify-vendor --prefix src/SonosMonitoring.Web
```

Danach Versions-/Lizenz-/Assetänderung prüfen und die Browsertests ausführen. Ein regulärer
.NET-Build benötigt weder Node noch npm, da das geprüfte Browserasset eingecheckt ist.

## Anwendungslogs und Browserzeitzone (SM-023/SM-024)

Die Navigation führt unter **Anwendungslogs** nach `/logs`. Die Seite liest ausschließlich
`GET /api/diagnostics/logs` über den serverseitigen API-Client. Dienst, Mindestschweregrad
und Suchtext werden als vollständiger Filtersatz angewandt. Die letzten 200 Treffer
erscheinen absteigend, Aktualisierung alle zehn Sekunden ist pausierbar.
Speicherstatus jeder Quelle, übersprungene beschädigte Zeilen, abgeschnittene Ergebnisse
und API-Ausfälle bleiben sichtbar. Alte Ergebnisse werden bei fehlgeschlagener
Aktualisierung ausdrücklich als alter Stand behandelt. Rohtexte werden als Text gerendert.

API/Collector, Projektionsworker und Webhost verwenden gemeinsam das technisch abgegrenzte
Observability-Projekt. Es hat nur eine Contracts-Referenz; der Webhost erhält dadurch keine
Domain- oder Datenbankabhängigkeiten. Speicherung und Betriebsgrenzen:
[Betriebshandbuch](betrieb.md#anwendungslogs-sm-023).

Die IANA-Zeitzone kommt aus `Intl.DateTimeFormat().resolvedOptions().timeZone`.
Sie gilt pro interaktivem Browser-Circuit; die Server-/Containerzeitzone ist unerheblich.
Ohne verfügbare Browserzone bleibt ein sichtbarer UTC-Fallback. Sommer-/Winterzeit wird
für den jeweiligen Zeitstempel angewandt. Bei Navigation wird die Zone erneut gelesen.
Historienfilter werden aus lokaler Browserzeit nach UTC konvertiert; wegen Zeitumstellung
fehlende oder doppeldeutige Ortszeiten werden mit einem Hinweis abgewiesen.
API-Verträge, gespeicherte Zeitstempel und Exporte bleiben UTC.

Prüfanforderungen und ausgeführte Nachweise stehen bei SM-023/024 in [planung.md](planung.md).

## Gruppen direkt verwalten (SM-013.6)

Unter **Mediensteuerung → Wo soll es spielen?** einen Raum wählen. Die Auswahl benennt dessen
aktuelle Wiedergabegruppe. Im Gruppenpanel weitere einzeln spielende Räume hinzufügen oder
Mitglieder entfernen und die betroffenen Räume bestätigen. Anschließend Favorit, Queueeintrag
oder Wiedergabebefehl für die angezeigte Gruppe wählen. Die Auswahl allein überträgt keine Musik.
Der Koordinator bleibt erhalten; zum Auflösen alle anderen Mitglieder einzeln entfernen.
Ein Raum aus einer anderen Gruppe muss zuerst dort entfernt werden. Stereo-/Heimkinoverbünde
werden nicht verändert. Die Topologieansicht verlinkt die Verwaltung.

Die Bestätigung fixiert auch den angezeigten Zielkontext eines Beitritts. Bei parallelen
Änderungen in der Sonos-App aktualisieren und neu prüfen. Annahme bedeutet noch keine
beobachtete Gerätewirkung. Während einer offenen Aktion ist der Wiedergabezielwechsel gesperrt;
bei Antwortverlust nur denselben Vorgang wiederholen oder seinen Status prüfen.
Recherche, API-Erweiterung, Grenzen und tatsächlicher Prüfstand stehen bei
[SM-013.6](planung.md#gruppenverwaltung-und-wiedergabeziele-sm-0136-07102026).
