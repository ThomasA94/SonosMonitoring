# C#-Code-Review vom 08.10.2026

Geprüfter Stand: `ae59cbad9264632dd5122b539aff5f4fc8f3191f`.
Auftrag: Korrektheit, Sicherheit, Performance, Wartbarkeit und Fehlerbehandlung;
Ursprünglicher Review nach `AGENTS.md`, ohne Änderung der Anwendungsimplementierung.
Der anschließend autorisierte Umsetzungsauftrag wird unten getrennt fortgeschrieben;
die ursprünglichen Befunde und Nachweise bleiben als Historie erhalten. Zeilenangaben
in den Befundabschnitten beziehen sich auf den ursprünglich geprüften Stand.

## Ergebnis und Einordnung

Sechs P2-Befunde und ein P3-Befund. P2 bezeichnet einen unter den beschriebenen
Bedingungen relevanten Funktions-, Resilienz- oder Performancefehler; P3 einen
nachrangigen Dokumentations-/Wartbarkeitsmangel. Kein belegter P0-/P1-Befund.
Die IDs setzen die vorhandenen REV-001–REV-005 fort und werden nicht wiederverwendet.
Die folgenden Befunde wurden zur Umsetzung angenommen; der aktuelle Umsetzungs- und
Prüfstand steht im Abschnitt „Umsetzung des Folgeauftrags“. Anforderungsstatus und
Priorisierungsentscheidungen bleiben in [planung.md](planung.md).

| ID | Priorität | Schwerpunkt | Betroffene Anforderungen | Nachweis |
|---|---|---|---|---|
| REV-006 | P2 | Korrektheit / Konfigurationsvalidierung | SM-010, SM-021 | Isolierte Ausführung der Validierung und des Writer-Einstiegs |
| REV-007 | P2 | Korrektheit / Fehlererholung | SM-002, SM-019 | Drei unterbrochene Sweeps mit simuliertem HTTP-Transport |
| REV-008 | P2 | Fehlerbehandlung / dauerhafter Vorgangsstatus | SM-013.3, SM-013.8 | Statische Ablaufanalyse |
| REV-009 | P2 | Korrektheit / Teilwirkungen | SM-013.4, SM-013.5 | Statische Ablaufanalyse bis zur UI |
| REV-010 | P2 | Performance / Isolation unabhängiger Ziele | SM-013.3, SM-013.8 | Statische Scheduler- und Budgetanalyse |
| REV-011 | P2 | Performance / Historienpersistenz | SM-010, SM-019 | SQL- und Aufrufanalyse; keine Lastmessung |
| REV-012 | P3 | Wartbarkeit / Framework-Referenzbindung | SM-020 | Versionsvergleich zwischen Guide, Paketdefinitionen und Lockfiles |

## REV-006 — Historiengrenze auch auf gespeicherte und zur Laufzeit geänderte Einstellungen anwenden

**Fundstellen:** [ObservationHistoryRegistration.cs](../src/SonosMonitoring.Infrastructure/History/ObservationHistoryRegistration.cs),
Zeilen 24–27; [CollectorRuntime.cs](../src/SonosMonitoring.Infrastructure/Collection/Configuration/CollectorRuntime.cs),
Zeilen 62–82; [PostgresObservationHistory.cs](../src/SonosMonitoring.Infrastructure/History/PostgresObservationHistory.cs),
Zeilen 214–217.

Die Historienregistrierung prüft `Collector:MaxKnownDevices` ausschließlich aus
`IConfiguration`. Tatsächlich verwendete Einstellungen können jedoch aus
`collector-settings.json` stammen oder später über `ApplyAsync` ersetzt werden.
Die dortige Collectorvalidierung erlaubt mehr als 256 Geräte, während der Writer
jeden Batch mit mehr als 256 Beobachtungen vollständig ablehnt.

**Auslöser und Wirkung:** Historie mit dem Deploymentdefault 256 aktivieren und über
die Erfassungseinstellungen `MaxKnownDevices=257` bei ausreichend großem Suchnetz
speichern. Sobald 257 Geräte im Snapshot stehen, scheitert jede Historienprobe vor
dem Datenbankzugriff. Auch die ersten 256 Geräte erhalten keine neuen Einträge.
Der Worker behandelt dies als Speicherstörung und versucht denselben ungültigen Batch
alle zehn Sekunden erneut. Ein Neustart behebt den Widerspruch nicht, wenn die
gespeicherte Datei erneut geladen wird und der Deploymentwert 256 bleibt.

**Tatsächlich geprüft:** `AddObservationHistory` akzeptierte die Deploymentkonfiguration;
`CollectorConfigurationValidator.Validate` akzeptierte die abweichenden 257 Geräte.
`WriteAsync` mit 257 gültigen Beobachtungen warf vor jeder Datenbankverbindung:
`Observation batch exceeds its device limit. (Parameter 'samples')`.
HTTP-Speicherung und ein laufender PostgreSQL-Worker wurden hierfür nicht ausgeführt.

**Korrektur:** Die aktive Historiengrenze bei Erstladung und jedem Save gegen die
effektiven Collectoroptionen prüfen, bevor die Datei geschrieben wird. Die Grenze an
einer maßgeblichen Stelle definieren. Falls größere Installationen unterstützt werden
sollen, Batch-/Checkpointsemantik ausdrücklich gemeinsam erweitern.

**Regression:** Bei aktiver Historie sowohl eine gespeicherte 257er-Konfiguration als
auch einen entsprechenden HTTP-Save kontrolliert ablehnen; die bisherige Konfiguration
und die laufende Historie müssen erhalten bleiben. Den 256er-Grenzfall akzeptieren.

## REV-007 — Abgebrochene Discovery-Sweeps müssen bei bisher nicht erreichten Adressen fortsetzen

**Fundstelle:** [CollectorWorker.cs](../src/SonosMonitoring.Infrastructure/Collection/CollectorWorker.cs),
Zeilen 132–166; erneute Terminierung auch nach Timeout in Zeilen 87–90.

Jede Vollsuche erzeugt die CIDR-Hosts erneut ab Index null. Ein Gesamttimeout verwirft
den Fortschritt; die nächste Vollsuche verwendet dieselbe Reihenfolge. Das Mischen
mehrerer CIDRs löst die Benachteiligung später Adressen innerhalb dieser Netze nicht.

**Auslöser und Wirkung:** Frühe Adressen antworten nicht, ihre Request-/Retryzeiten
verbrauchen das Sweepbudget. Ein erreichbarer Player weiter hinten wird dann auch
über beliebig viele Discoveryintervalle nicht angesprochen. SSDP ist optional;
ohne bereits bekannten Topologiereporter gibt es auch keinen alternativen Hinweis.
Das betrifft zulässige Konfigurationen, etwa große Suchnetze mit vielen stillen Hosts.

**Tatsächlich geprüft:** Produktions-`CollectAsync` dreimal mit sechs Hosts aus
`192.168.50.0/29`, Parallelität 1 und je 500 ms Abbruchbudget aufgerufen. Ein injizierter
Handler simulierte eine zehn Sekunden ausbleibende Antwort und beachtete Cancellation.
Ausgabe: `192.168.50.1, 192.168.50.1, 192.168.50.1`. Keine der fünf späteren Adressen
wurde erreicht. Hierfür wurden weder Sockets geöffnet noch reale Geräte abgefragt;
die private Methode und der Transport wurden ausschließlich im lokalen Repro per
Reflection angesprochen/ersetzt.

**Korrektur:** Einen begrenzten Scanfortschritt beziehungsweise eine faire Rotation
für noch nicht geprüfte Hosts erhalten. Bekannte Geräte dürfen weiter bevorzugt werden,
ohne den Discoveryrest dauerhaft zu verdrängen.

**Regression:** Mehrere Sweeps absichtlich vor dem letzten Host abbrechen. Ein gesunder
Player am Ende muss innerhalb einer begrenzten Anzahl von Sweeps trotzdem entdeckt
werden; Zeitbudget, Deduplizierung und Parallelitätslimit müssen weiter gelten.

## REV-008 — Nicht eingeordnete Medienvorgänge nach Ablauf dauerhaft abschließen

**Fundstellen:** [MediaRequestService.cs](../src/SonosMonitoring.Application/MediaControl/MediaRequestService.cs),
Zeilen 98–116; [MediaEndpoints.cs](../src/SonosMonitoring.Api/MediaControl/MediaEndpoints.cs),
Zeilen 116–120; [PostgresMediaCoordinationStore.cs](../src/SonosMonitoring.Infrastructure/MediaControl/PostgresMediaCoordinationStore.cs),
`AdmitAsync` und `RecoverAsync`.

Der Vorgangsstream wird vor der Einordnung in `sonos_media.operations` mit `accepted`
committed. Scheitert danach `AdmitAsync`, etwa wegen voller Queue, abgelaufener Frist,
Entzug oder Verbindungsabbruch, kann ein Stream ohne Koordinationsdatensatz verbleiben.
Das ist als Zwischenzustand dokumentiert, besitzt aber keinen dauerhaften Abschlussweg,
sobald eine nachträgliche Einordnung unmöglich geworden ist.

**Auslöser und Wirkung:** Queue füllen, eine weitere gültige Aktion bis zum Eventcommit
ausführen und ihre Einordnung mit `media.queue.full` ablehnen. Nach Ablauf der ursprünglichen
Frist kann derselbe Auftrag nicht mehr eingeordnet werden. Die Statusabfrage liefert
bei sichtbarer Projektion trotzdem unbegrenzt `202 media.admission.pending`, weil sie
weder Frist noch endgültige Ablehnung auswertet. `RecoverAsync` bearbeitet ausschließlich
vorhandene Koordinationszeilen. Es wird kein Gerätewrite ausgelöst, aber der dauerhafte
Status behauptet weiterhin einen überholten Wartezustand.

**Nachweisgrenze:** Aus dem Code abgeleitet; die konkrete Fehlerfolge wurde in diesem
Review nicht gegen den Event Store und PostgreSQL ausgeführt.

**Korrektur:** Einen idempotenten Abschluss-/Reconciliationweg für nicht eingeordnete
Streams vorsehen. Ein vorübergehender Admissionfehler darf bis zur Originalfrist mit
derselben Identität nachgeholt werden; danach muss ein bestätigter terminaler Zustand
abfragbar sein. Keine neue Frist oder Generation für denselben Auftrag vergeben.

**Regression:** Fehler nach Eventcommit und vor Admission injizieren, Frist verstreichen
lassen und neu starten. Die Statusabfrage muss schließlich einen dauerhaften Endzustand
liefern; identische Retries dürfen keinen Gerätewrite und keine Fristverlängerung bewirken.

## REV-009 — Fehler nach bestätigtem Teilschritt dürfen nicht wie vollständige Ablehnungen erscheinen

**Fundstellen:** [MediaExecutionService.cs](../src/SonosMonitoring.Application/MediaControl/MediaExecutionService.cs),
Zeilen 96–119; [MediaDeviceGateway.cs](../src/SonosMonitoring.Infrastructure/MediaControl/Transport/MediaDeviceGateway.cs),
Zeilen 440–450 und 613–615; [MediaPanel.razor](../src/SonosMonitoring.Web/Components/MediaControl/MediaPanel.razor),
Zeilen 54–61.

Die Fehlerklassifikation betrachtet nur `released` und die definitive Ablehnung des
aktuellen Geräteaufrufs. Bereits bestätigte frühere Schritte werden dabei nicht als
Teilwirkung im abschließenden Ergebnis ausgewiesen.

**Auslöser und Wirkung:** Bei `start-favorite` wird `SetAVTransportURI` erfolgreich
bestätigt; das anschließende `Play` liefert einen SOAP Fault. `SendAsync` kennzeichnet
diesen Fault als definitive Ablehnung, und `ProcessAsync` speichert für den gesamten
Vorgang `rejected`. Die Quelle wurde jedoch bereits geändert. Die UI zeigt „Abgelehnt“
und den technischen Fehlercode; eine Teilwirkungswarnung existiert nur für
`media.partial.revoked`. Bei Queue-Play kann entsprechend die Quelle oder Position
bereits geändert sein, obwohl das abschließende Play scheitert.

**Nachweisgrenze:** Statisch über Vorbereitung, Ausführung, Fehlerabbildung und UI
verfolgt; kein neuer mehrstufiger SOAP-/Datenbanktest ausgeführt.

**Korrektur:** Bestätigte Schritte im Resultat berücksichtigen und partielle Wirkung
explizit als Status oder stabilen Fehlercode einschließlich Benutzerhinweis darstellen.
Eine definitive Ablehnung des zweiten Aufrufs beweist nur dessen Ablehnung. Bereits
geschriebene Eventverträge bei einer Erweiterung kompatibel halten.

**Regression:** Erfolgreichen Quellenwechsel und danach definitiven Play-Fault simulieren.
Status und UI müssen den erfolgten Quellenwechsel nennen; weder automatisch wiederholen
noch eine vollständige Rücknahme suggerieren.

## REV-010 — Ein langsames Medienziel lässt Aufträge unabhängiger Ziele ablaufen

**Fundstelle:** [MediaControlWorker.cs](../src/SonosMonitoring.Infrastructure/MediaControl/MediaControlWorker.cs),
Zeilen 52–57; Defaults in [MediaControlOptions.cs](../src/SonosMonitoring.Infrastructure/MediaControl/MediaControlOptions.cs),
Zeilen 13–15.

Der einzige Executor wartet innerhalb eines globalen `foreach` auf den vollständigen
`ProcessAsync`-Ablauf jedes Vorgangs. Auch disjunkte `Targets` werden dadurch seriell
ausgeführt. Ausführung und Gültigkeit haben standardmäßig jeweils 30 Sekunden Budget.

**Auslöser und Wirkung:** Zwei Aufträge für voneinander unabhängige Player fast
gleichzeitig einordnen. Verbraucht A durch mehrere langsame Leseprüfungen oder einen
hängenden Geräteaufruf das Gesamtbudget, erreicht der Worker B erst an oder nach dessen
Frist und markiert ihn als `expired`. B wird verworfen, obwohl sein Gerät gesund ist
und keine Zielkonkurrenz besteht. Ein Einzelexecutor zur Besitzsicherung erfordert
keine serielle Geräte-I/O für sämtliche Ziele. Die Planung beschreibt ausdrücklich
begrenzte Parallelität zwischen unterschiedlichen Zielen.

**Nachweisgrenze:** Statische Ablauf-/Budgetanalyse, keine neue Zeitmessung mit zwei
Produktions-Gateways. Der Collector läuft unabhängig weiter; betroffen ist die Medienqueue.

**Korrektur:** Innerhalb der exklusiven Executorsitzung eine begrenzte Zahl disjunkter
Zielmengen ausführen. Zielreihenfolge, Mehrzielreservierungen und Entzug müssen erhalten
bleiben. `ReleaseAsync` verwendet dieselbe Npgsql-Verbindung: Parallelisierung darf
diese Verbindung nicht gleichzeitig benutzen; kurze Freigabetransaktionen separat
serialisieren.

**Regression:** A bis zum Ausführungsbudget verzögern, B auf disjunkten Zielen sofort
bestätigen lassen. B muss vor seiner Frist ausgeführt werden, während ein weiterer
Auftrag auf A weiterhin korrekt wartet beziehungsweise abläuft.

## REV-011 — Historienretention scannt den Bestand bei jeder Probe erneut

**Fundstelle:** [PostgresObservationHistory.cs](../src/SonosMonitoring.Infrastructure/History/PostgresObservationHistory.cs),
Zeilen 410–425; Aufruf bei jeder Probe in Zeile 100.

`ORDER BY sequence DESC OFFSET @max_entries` muss den übersprungenen Bestand durchlaufen,
auch wenn keine Zeile zu löschen ist. Diese Bestandsprüfung läuft in jeder
Schreibtransaktion, einschließlich Proben ohne fachlich geänderte Beobachtung. Mit dem
erlaubten Maximum von 100.000 Einträgen und einer Sekunde Sampleintervall wird damit
pro Sekunde erneut ein sechsstelliger Bestand übersprungen. Sie hält zusätzlich den
globalen Writer-Lock und verbraucht einen Teil des festen zehnsekündigen Gesamtbudgets.

**Nachweisgrenze:** Der lineare Aufwand folgt aus Queryform und Aufruffrequenz. Ein
konkreter Timeout oder Ressourcenverbrauch auf der Zielhardware wurde nicht gemessen;
keine Behauptung, dass der Defaultbestand bereits zu langsam ist.

**Korrektur:** Den Zähl-/Löschpfad an tatsächliche Inserts koppeln und einen transaktional
gepflegten Bestand beziehungsweise geeigneten Grenzcursor nutzen. Altersbereinigung
separat begrenzt takten. Eine vereinbarte harte Maximalzahl nicht durch unkontrollierte
asynchrone Bereinigung aufweichen.

**Regression:** Mit 100.000 isolierten Einträgen und unveränderten Proben den Queryplan
und die Arbeitsmenge prüfen. Unveränderte Proben sollen nicht jedes Mal den ganzen
Bestand durchlaufen; Alters- und Mengenretention müssen korrekt bleiben.

## REV-012 — Der lokale Framework-Guide gehört nicht zur aufgelösten Paketkombination

**Fundstellen:** [FRAMEWORK_API_GUIDE.md](framework/FRAMEWORK_API_GUIDE.md), Zeilen 26–46;
[Directory.Packages.props](../Directory.Packages.props),
[Infrastructure-Lockfile](../src/SonosMonitoring.Infrastructure/packages.lock.json) und
[framework.md](framework.md), Abschnitt „Ergebnis und Quellenbindung“.

Der lokale Guide beschreibt ausdrücklich Ddd **12.3.3**, EventSourcing **12.2.1**,
EventProjection **12.5.1** und Outbox **2.3.2**. Tatsächlich verwendet das Projekt Ddd
**12.3.2**, EventSourcing **12.2.0**, EventProjection **12.5.0** und Outbox **2.3.1**.
Auch weitere Adapterversionen weichen ab. Die Projektdokumentation beschreibt die
Referenz dagegen als zur Paketkombination passend. Ein erfolgreicher Build beweist
nicht, dass die neuere dokumentierte Semantik für die älteren Pakete gilt.

**Wirkung:** Künftige Änderungen und Fehleranalysen können API-/Konsistenzgarantien der
falschen Version übernehmen. Damit fehlt die in `AGENTS.md` verlangte eindeutige
Referenzbindung. Dies ist ein Befund zur Anwendungspflege, kein behaupteter Framework-Bug.

**Korrektur:** Den zur bestehenden Kombination gehörenden Guide aus dem dokumentierten
Quellstand wiederherstellen oder ein bewusst geprüftes Paketupgrade durchführen. Keine
Versionsnummern im Guide manuell passend schreiben. Herkunft und Referenzprüfung in
`framework.md` nachvollziehbar aktualisieren; einen automatisierten Abgleich ergänzen.

**Regression:** Die im Guide beschriebenen verwendeten Pakete gegen direkte und
transitive Lockfileversionen prüfen; absichtliche Abweichungen ausdrücklich begründen.

## Sicherheitsprüfung und Wartbarkeit

Im untersuchten Code wurde kein zusätzlicher konkret belegter Authentifizierungs-,
SQL-Injection-, XXE- oder willkürlicher SSRF-Befund festgestellt. Geprüft wurden insbesondere:

- API-Zugang über tatsächliche Peeradresse beziehungsweise konstantzeitlich verglichenen
  Schlüssel sowie Hostfilter in den Hostkonfigurationen.
- Separater Webzugang, Antiforgery für Cookie-ändernde POSTs, lokale Redirectvalidierung,
  Loginlimit und konfigurierter Ablauf authentifizierter Verbindungen.
- Parameterisierte SQL-Werte; SQL-Suffixe stammen in den untersuchten Pfaden aus dem Code.
- Feste Gerätepfade und Aktionen, literale CIDR-geprüfte Ziele, unterbundene Redirects/Proxies,
  begrenzte Antwortkörper und XML-Parser mit gesperrten DTDs/externen Entitäten.
- Eingeschränkte Logdateiquellen und erneute Bereinigung gelesener Logeinträge.

Das ist eine begrenzte Codeprüfung, kein Penetrationstest, neuer CVE-/Image-Scan oder
Nachweis der LAN-/TLS-Konfiguration. Das dokumentierte Installationsmodell ohne
Mehrbenutzer-/Mandantenautorisierung wurde nicht als neue Produktanforderung umgedeutet.
Für Nullwerte, Bereichsgrenzen und konkurrierende Zustandsübergänge wurden die erreichbaren
Validierungen verfolgt; über die Befunde hinaus wird keine Fehlerfreiheit behauptet.

Positiv sind die getrennten HTTP-/Domain-/Persistenzverträge, eigene Typdateien, dokumentierte
Methoden, zentrale Buildvorgaben und die vorhandenen Architektur-/Vertragstests.
Ein konkreter SRP-Verbesserungspunkt ist `MediaDeviceGateway`: Zustandslesen,
Fähigkeitsableitung, Referenzcache, Aktionsplanung und Dispatchprüfung teilen eine große
Klasse. Bei der nächsten fachlich erforderlichen Änderung diese Verantwortungen gezielt
trennen. Insbesondere die fachliche Eingabevalidierung bleibt beim Domainbesitzer;
geräteabhängige Vorbedingungen gehören an die Infrastrukturgrenze. Dies ist eine
Wartbarkeitsempfehlung, kein zusätzlicher priorisierter Featureauftrag.

## Tatsächlicher Prüfstand dieses Reviews

- `pwsh -NoProfile -File scripts/verify.ps1`: **Exit-Code 0**. Locked-Restore,
  nichtinkrementeller Release-Build mit **0 Warnungen / 0 Fehlern**, Formatprüfung,
  Eventkatalog-Kompatibilität und Aktualitätsvergleich, Metrikkatalogvergleich sowie
  Grafana-Vertragsprüfung (**4 Dashboards / 94 Queries**) bestanden.
- **296 Unit-Tests und 47 Web-Tests bestanden**, jeweils 0 fehlgeschlagen und
  0 übersprungen. Aktuelle Ergebnisse: `artifacts/test-results/unit.trx` und `web.trx`.
- Ein erster Lauf scheiterte mit `NU1301` an der Socket-/Netzsperre der Sandbox;
  der anschließend autorisierte Lauf mit Netzwerkzugriff bestand. Dieser anfängliche
  Umgebungsfehler wird nicht als Codefehler gewertet.
- `dotnet run --project artifacts/review-repro/ReviewRepro.csproj -c Release`:
  **Exit-Code 0**; gezielte Reproduktionen zu REV-006/REV-007 wie oben beschrieben.
  Der lokale Harness referenziert die zuvor gebauten Assemblies; seine Dateien liegen
  im ignorierten Artefaktverzeichnis. Er ist kein eingecheckter Regressionstest.
- Keine neuen Infrastruktur-, Browser-, Last-, Restore- oder realen Sonos-Tests ausgeführt.
  Die grünen bestehenden Tests widerlegen die zusätzlichen Randfälle nicht und belegen
  insbesondere keinen Fix. Keine Anwendungs-/Frameworkpakete geändert, keine
  Datenbanken zurückgesetzt und keine Gerätebefehle gesendet.

## Umsetzung des Folgeauftrags, 08.10.2026

Die sieben Befunde REV-006–REV-012 sind implementiert und am 08.10.2026 mit den unten
genannten Prüfungen verifiziert. Ihr Status lautet **behoben (resolved)**.
Die folgenden Änderungen verwenden unveränderte Paketversionen, Eventverträge und
PostgreSQL-Schemata. Bestehende Ereignisse und Daten bleiben lesbar.

| ID | Umsetzung | Regression |
|---|---|---|
| REV-006 | Gemeinsame Historiengrenze; Prüfung geladener und vor dem Save vorgeschlagener Collectoroptionen. HTTP-Validierung liefert 400. | `ObservationHistoryConfigurationGuardTests`: gespeicherte 257er-Konfiguration abweisen, 256 akzeptieren, Save ohne Dateiersetzung ablehnen. `CollectorConfigurationTests`: HTTP 400 und unveränderter Stand. |
| REV-007 | Rotierender Cursor über den deduplizierten Vollsuchplan; Fortschritt nur für tatsächlich gestartete Probes. Reguläres Polling bleibt unabhängig. | `DiscoveryTargetSchedulerTests`: unterbrochene Sweeps bis zum letzten gesunden Host; überlappende Seeds/Ranges mit nur ein oder zwei Probes pro Runde; begrenzte Parallelität. |
| REV-008 | Hintergrund-Reconciliation und GET/Retry schließen nicht mehr zulässige, unaufgenommene Intentionen dauerhaft. Aufnahme und Abschluss teilen denselben PostgreSQL-Lock; Original-ID, Generation und Frist bleiben erhalten. | `MediaAdmissionRecoveryTests`: tatsächlicher Aufnahmefehler nach Event-Commit, Neustart, Ablauf bzw. Entzug, Hintergrundabschluss ohne GET, identischer Retry ohne Gerätesend und konkurrierende Aufnahme/Schließung. |
| REV-009 | Sicher abgelehnter Folgeschritt nach bestätigtem früherem Schritt trägt `media.partial.rejected`; DE-/EN-Oberfläche erklärt die bleibende Teilwirkung. | `MediaExecutionServiceTests`: erste gegenüber zweiter Sendablehnung, Ereignisfolge, Code und Sendanzahl. `MediaOperationLocalizationTests`: beide Sprachen. |
| REV-010 | Begrenzte gleichzeitige Ausführung disjunkter Ziele, Standard 4; kurze Zugriffe auf die Eigentümerverbindung serialisiert; Abbruch und Drain vor Freigabe der Sitzung. | `MediaParallelExecutionTests`: B beendet, während A im Send wartet; zweites A bleibt wartend; jeder Auftrag genau ein Send und persistierter Schritt. `MediaExecutionSchedulerTests`: Überschneidung, Datenbanksperren und Ablauf hinter einer Sperre. |
| REV-011 | Mengenscan nur bei Inserts und erstem erfolgreichen Schreiben je Storeinstanz; Altersretention bleibt aktiv, Insert und Begrenzung bleiben atomar. | `ObservationHistoryTests`: 100.000 zusätzlich isoliert angelegte Zeilen, unveränderte Probe ohne Mengenscan, erneute Storeinstanz setzt die Grenze durch; vorhandene Alters-/Mengenretentionstests. |
| REV-012 | Originalguide aus gepinntem Framework-Commit wiederhergestellt; Herkunft und Blob-Hash dokumentiert. Kein Paketupgrade. | `FrameworkApiGuideVersionTests`: tatsächlich verwendete direkte/transitive Lockfileversionen und zentrale Paketversionen gegen die Guide-Matrix. |

**Betriebsfolgen und Grenzen:**

- Alte unaufgenommene Medienintentionen werden beim nächsten erreichbaren Scan bzw.
  gezielten Abruf abgeglichen. Projektionen liefern nur Kandidaten; aus ihnen folgt
  keine Ausführungsberechtigung. Projektion-/Datenbankausfälle können die Sichtbarkeit
  weiter verzögern. Ein persistierter terminaler Fence überlebt einen Audit-Ausfall.
- `MediaControl:MaxConcurrentExecutions` ist auf 1–16 begrenzt und im Beispiel mit 4
  gesetzt. Gleiche oder überlappende Ziele bleiben geordnet; es gibt keine automatischen
  Wiederholungen bereits freigegebener Gerätewrites.
- Statusabrufe setzen nur den technischen Abschluss; das semantische Audit übernimmt
  der Executor. Auch innerhalb des Workers wird kein Audit-Command parallel zu einem
  noch aktiven Vorgang ausgeführt. Damit konkurrieren Abruf, Entzug und Geräteablauf
  nicht um denselben Concurrency-Token. Die Koordinationsentscheidung gilt sofort;
  ihre Event-Nachführung kann bis zum Ende des aktiven Ausführungsbudgets warten.
- Bei aktivierter Historie scheitert ein Start mit bereits gespeicherten unzulässigen
  Collectorlimits verständlich. Betreiber müssen diese Einstellungen korrigieren;
  die Anwendung schneidet den Gerätestand nicht stillschweigend ab.
- Der Discoverycursor überlebt Rundenabbrüche, aber keinen Prozess- oder
  Konfigurationsneustart. Eine echte Lastmessung des Journals und reale Sonos-, Firmware-,
  LAN-/TLS- und Dauerbetriebsabnahmen sind damit nicht behauptet.
- Die weitergehende SRP-Aufteilung von `MediaDeviceGateway` bleibt gemäß ursprünglichem
  Review eine Wartbarkeitsempfehlung ohne priorisierten Featureauftrag.

Bedienung und Wiederanlauf stehen in [betrieb.md](betrieb.md), Erfassung und Historie
in [umsetzung-backend.md](umsetzung-backend.md). Anforderungen bleiben in
[planung.md](planung.md) maßgeblich.

### Tatsächlicher Prüfstand der Umsetzung

- `pwsh -NoProfile -File scripts/verify.ps1 -IncludeInfrastructure`: **Exit-Code 0**.
  Locked-Restore, nichtinkrementeller Release-Build (**0 Warnungen / 0 Fehler**),
  Formatprüfung, Event- und Metrikkataloge, Grafana-Verträge (**4 Dashboards / 94 Queries**),
  Prometheus-Alarmprüfung sowie PostgreSQL- und Prometheus-Backup/Restore bestanden.
- **307 Unit-, 48 Web-, 31 Integrations- und 50 E2E-Tests bestanden**, jeweils
  **0 Fehler / 0 übersprungen**. Die Integration verwendet echte isolierte PostgreSQL-,
  RabbitMQ- und MongoDB-Dienste; Gerätezugriffe bleiben simuliert.
- Nach der letzten Worker-Absicherung erneut `scripts/verify.ps1`: **Exit-Code 0**;
  nach der Ergänzung der Testvorbedingung erneuter Release-Build des Integrationsprojekts
  mit **0 Warnungen / 0 Fehlern** und vollständiger Integrationstestlauf:
  **31/31 bestanden, Exit-Code 0**. Die unveränderten E2E-Pfade betreiben keinen Media-Worker.
- Nachweise: `artifacts/review-verification.log`, `artifacts/review-final-gates.log`,
  `artifacts/review-final-integration.log` und die aktuellen TRX-Dateien unter
  `artifacts/test-results/`. Die Skriptprüfungen protokollieren Backup/Restore separat.
- Die ersten Läufe fanden zusätzlich einen echten Audit-/Widerrufs-Wettlauf sowie
  Fehler im neuen Testaufbau; diese wurden korrigiert. Der Discoverytest wartet jetzt
  auf tatsächlich gestartete Probes statt auf ein knappes Zeitfenster. Der Neustarttest
  prüft vor dem absichtlichen Stop die abgeschlossene Veröffentlichung des initialen
  Events. Damit vermischt seine kurze Projektionsfrist den Aufnahmefehler nicht mit
  dem separaten, dokumentierten Publish-/Settlement-Fenster einer fünfminütigen
  Outbox-Lease. Die frühen Projektions-Timeouts sind kein bestätigter Framework-Bug;
  bei erneutem Timeout zeigt der Test Wasserstand und begrenzte Outbox-Metadaten.
- Keine neue Browser-/Screenreader-, Last-, Image-/CVE- oder reale Sonos-Abnahme.
  Die vorhandenen offenen Produktabnahmen bleiben bestehen. Keine realen
  Gerätebefehle, Paketupgrades oder Anwendungsdaten-Resets waren Teil dieser Änderung.
