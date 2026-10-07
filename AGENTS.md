# Entwicklungsregeln für `<Projekt>`

Diese Vorlage beschreibt dauerhafte Arbeitsregeln für Anwendungen mit der Devbone
Event-Driven Platform, DDD, CQRS und Event Sourcing. Sie soll eine nachvollziehbare
Architektur, fachlich aussagekräftige Modelle und reproduzierbare Qualität ermöglichen.

## Verwendung und Geltungsbereich

- Als `AGENTS.md` in ein neues Repository kopieren und `<Projekt>` durch den Anwendungsnamen
  beziehungsweise Root-Namespace ersetzen. Anschließend diesen Einrichtungsabsatz entfernen.
- Die Ordnerstruktur, C#-Stilregeln und Arbeitsabläufe sind Konventionen dieser Vorlage.
  Sie sind keine technischen Anforderungen des Frameworks. Optionale Komponenten nur anlegen,
  wenn die Anwendung sie benötigt; Abweichungen mit Begründung dokumentieren.
- Die zur tatsächlich aufgelösten Paketkombination passende Framework-Referenz ist für
  API-Signaturen und technische Semantik maßgeblich. Keine Versionsnummern oder Grenzwerte
  aus einem anderen Projekt ungeprüft übernehmen.
- Persistenzmodus, Broker, Authentifizierung, Frontend, Deployment und Branchstrategie
  bewusst für das Projekt festlegen. Diese Vorlage setzt weder Kafka noch einen bestimmten
  Identity Provider oder eine feste Anzahl von Hosts voraus. Für ein Webfrontend sind Blazor
  und MudBlazor die bevorzugte Wahl; begründete Abweichungen bleiben möglich.

## Dokumentation und Anforderungen

Vor der Arbeit vorhandene Dokumentation und ihre tatsächlichen Ablageorte ermitteln: vom
Repository-Einstieg und dessen Verweisen ausgehend, bei Bedarf durch eine Dateisuche. Keine
bestimmten Dokumentnamen oder Verzeichnisse voraussetzen. Die folgende Tabelle beschreibt
Dokumentrollen, keine vorgeschriebenen Pfade. Ein Dokument darf mehrere Rollen erfüllen.

| Dokumentrolle | Verantwortung |
|---|---|
| Agent-Regeln | Dauerhafte Arbeitsregeln, die nicht bereits aus dem Code hervorgehen |
| Projekteinstieg, üblicherweise eine README | Architekturüberblick, Voraussetzungen und konkrete Befehle für Einrichtung, Start, Build und Tests |
| Anforderungs- und Entscheidungsdokumentation | Anforderungen, Prioritäten, Status, Abnahmekriterien, Entscheidungen, Einschränkungen und offene Fragen |
| Framework-Referenz | API-Guide und Kompatibilitätsmatrix für die verwendete Paketkombination, lokal oder als eindeutig versionierte externe Referenz |
| Betriebshandbuch / Runbook | Bei entsprechendem Betriebsumfang: Diagnose, Reparatur, Wiederaufbau, Replay und Restore |
| Erkenntnissammlung | Bei Bedarf: reproduzierbare Framework-Erkenntnisse, Probleme und Verbesserungsvorschläge |
| Vertragskataloge | Generierte und eingecheckte Vergleichsbasis für Vertragsänderungen |

- Vorhandene Dokumente an ihrem Ablageort weiterführen. Fehlende Dokumentrollen nur bei Bedarf
  ergänzen, ihren Ort passend zur Projektstruktur wählen und vom Projekteinstieg aus verlinken.
  Keine parallele Dokumentation anlegen, nur weil ein erwarteter Dateiname fehlt.
- Anforderungen mit stabilen IDs und konkreten Abnahmekriterien führen. Den geforderten
  Testumfang und den tatsächlich nachgewiesenen Prüfstand getrennt festhalten.
- Verworfene Anforderungen mit Begründung markieren; IDs und Entscheidungshistorie erhalten.
  Neue Ideen nicht ohne Priorisierungsentscheidung zum Umfang der laufenden Aufgabe machen.
- Erkenntnisse bei der betroffenen Anforderung dokumentieren, nicht ausschließlich im Chat.
  Ein zusätzlicher Detailplan verweist auf diese Anforderungen und führt keinen konkurrierenden Status.
- Dokumentation zusammen mit der Änderung aktualisieren. Regeln und Entscheidungen an einer
  maßgeblichen Stelle pflegen, andernorts darauf verweisen.

### Lessons Learned: Framework-Issues, Feedback und Feature Requests

Die Erkenntnissammlung ist der nachvollziehbare Rückkanal zum Framework-Team. Ihren
vorhandenen Ablageort und Übergabeprozess zuerst ermitteln; bei Bedarf im Projekt festlegen
und vom Projekteinstieg verlinken. Anwendungsspezifische Entscheidungen bleiben bei den
betroffenen Anforderungen und verweisen auf den Framework-Befund.

- Pro Befund einen Eintrag mit stabiler ID führen. Neue Einträge anhängen; bestehende IDs
  weder umnummerieren noch wiederverwenden. Vor einem neuen Eintrag nach demselben Problem
  suchen und vorhandene Erkenntnisse ergänzen.
- **Issue:** Beobachtetes Verhalten weicht vom dokumentierten oder vereinbarten Vertrag ab.
  Solange die Ursache offen ist, als Verdacht kennzeichnen, nicht als bestätigten Framework-Bug.
- **Feedback:** Nutzungsprobleme, unklare Dokumentation, überraschende APIs oder bewährte
  Vorgehensweisen beschreiben. Beobachtung und gewünschte Verbesserung getrennt darstellen.
- **Feature Request:** Fehlende Fähigkeit anhand eines konkreten Anwendungsfalls begründen.
  Bestehende Alternativen, deren Grenzen und überprüfbare Akzeptanzkriterien nennen.
  Einen Wunsch nicht als Defekt darstellen und die Framework-Implementierung nicht vorwegnehmen.

Jeder Eintrag enthält, soweit für seine Art relevant:

- ID, Titel, Art, Datum, betroffene Anforderung und Bearbeitungsstatus beziehungsweise
  einen Verweis auf die maßgebliche Statusquelle;
- tatsächlich aufgelöste Paketversionen, verwendete Referenzversion und relevante Umgebung;
- Anwendungsfall, erwartetes und beobachtetes Verhalten beziehungsweise gewünschte Fähigkeit;
- minimale Reproduktionsschritte, relevante Konfiguration und konkrete Prüfbelege;
- Auswirkungen, Dringlichkeit mit Begründung, bekannte Alternativen und eventuelle Workarounds;
- offene Fragen, nächster Schritt, Rückmeldungen und Verweise auf zugehörige Issues oder Releases.

**Vorbereitung und Übergabe**

- Zuerst eigene Konfiguration und Implementierung prüfen, dann die passende Paketkombination,
  Paket-READMEs und den API-Guide abgleichen. Beobachtungen berichten; vermutete Ursachen
  ausdrücklich als Hypothesen markieren.
- Bei Issues möglichst einen kleinen ausführbaren Reproduktionsfall erstellen. Den betroffenen
  Framework-Typ oder Aufruf bei Bedarf isoliert prüfen, damit Anwendungs- und Framework-Verhalten
  unterscheidbar werden. Fehlende Infrastruktur oder Reproduktion als offene Prüfung festhalten.
- Nur tatsächlich ausgeführte Prüfungen als verifiziert bezeichnen. Ein erfolgreicher Build
  oder Unit-Test bestätigt keinen behaupteten Fehler oder Fix im realen Command-bis-Query-Weg.
  Feedback und Feature Requests benötigen keinen künstlichen Fehlerreproduktionsfall.
- Reproduktionsdaten und Logs auf das Nötige begrenzen und von Secrets sowie personenbezogenen
  Daten bereinigen. Workarounds mit ihren Grenzen, Risiken und Entfernungskriterien dokumentieren.
- Übergaben über den vereinbarten Kanal im Rahmen des autorisierten Auftrags durchführen.
  Für dateibasierte Übergaben `LESSONS_LEARNED_<APPNAME>.md` verwenden, für zugehörige
  Antworten `LESSONS_LEARNED_<APPNAME>_ANSWERED.md`. `<APPNAME>` durch den tatsächlichen
  Anwendungsnamen ersetzen und in beiden Dateien identisch schreiben. Keine Rundennummern
  im Dateinamen verwenden; Befunde und Antworten über stabile IDs zuordnen. Der Ablageort
  bleibt projektabhängig und wird wie oben beschrieben ermittelt beziehungsweise festgelegt.
- Übergebene Fassungen als Referenz erhalten. Antworten anhand der stabilen IDs zuordnen und
  im maßgeblichen Eintrag verlinken; spätere Ergänzungen nachvollziehbar nachführen.
  Bei einem Issue-Tracker dessen IDs und Verlauf nutzen, statt einen zweiten Status zu pflegen.

**Rückmeldung, Verifikation und Abschluss**

- Eingang einer Antwort, Annahme eines Vorschlags, Veröffentlichung eines Fixes und erfolgreiche
  Prüfung in der Anwendung unterscheiden. Eine Antwort allein schließt einen Befund nicht ab.
- Bei einem Fix die konkrete Paketkombination erfassen, den ursprünglichen Reproduktionsfall
  erneut ausführen und den betroffenen Anwendungsweg prüfen. Sinnvolle Regressionstests ergänzen;
  einen Workaround erst entfernen, wenn seine Ablösung nachgewiesen ist.
- Einen behobenen Befund erst nach erfolgreicher Prüfung als `resolved` markieren und Version,
  Prüfdatum sowie Ergebnis festhalten. Angekündigte oder veröffentlichte, aber ungeprüfte Fixes
  bleiben ausdrücklich als solche erkennbar.
- Für Feedback und Feature Requests die Entscheidung mit Begründung festhalten, etwa angenommen,
  zurückgestellt oder abgelehnt. Eine Zusage ist noch keine Umsetzung. Bei übernommenen Features
  die Akzeptanzkriterien gegen die ausgelieferte Version prüfen; bei Dokumentationskorrekturen
  die korrigierte Referenz prüfen. Auch abgelehnte und anwendungsseitig gelöste Befunde erhalten.
- Bestätigte Erkenntnisse in die betroffenen Regeln, Tests, Anforderungen oder Betriebsabläufe
  übernehmen und auf den ursprünglichen Befund verweisen. Projektspezifische Beobachtungen
  nicht zu allgemeinen Framework-Garantien erklären.

## Struktur und Architekturgrenzen

Die folgende Struktur ist ein Vorschlag für neue Projekte. In bestehenden Projekten zuerst
die tatsächlichen Projektgrenzen und Verzeichnisse prüfen. Dokumentation, Kataloge, Skripte
und Infrastrukturdateien folgen den im Projekt festgelegten Ablageorten.

```text
src/
  <Projekt>.Domain          Aggregate, Domain-Events, Commands und Command-Handler
  <Projekt>.Application     Anwendungsabläufe, CommandExecutor, Policies und Query-Ports
  <Projekt>.ReadModels      Projektionsdokumente und deren Abfragen
  <Projekt>.Infrastructure  Framework-Konfiguration, Event Store, Routing und Outbox
  <Projekt>.Contracts       HTTP-DTOs und Fehlercodes
  <Projekt>.Platform        Gemeinsame Aggregat-, Stream- und Topologienamen
  <Projekt>.Observability   Gemeinsame Telemetrie und Healthchecks, falls benötigt
  <Projekt>.Authentication  Gemeinsame Authentifizierung, falls mehrere Hosts sie benötigen
  <Projekt>.Api             HTTP-Schnittstelle für Lese- und Schreibzugriffe
  <Projekt>.Projections     Projektionsworker und anwendungseigene Derived-Handler
  <Projekt>.Web             Optionales Frontend
tests/
  <Projekt>.<Bereich>.Tests
  <Projekt>.Integration.Tests
```

- Fachliche Zustandsänderungen laufen über Commands und den Event Store. Projektionen sind
  abgeleitete Modelle, keine zweite autoritative Schreibseite. Auch fachliche Schreibvorgänge
  in Hintergrundabläufen und Importen benutzen den vorgesehenen Anwendungsweg.
- Das Frontend greift über die API auf Daten zu. Als Anwendungsreferenzen sind `Contracts`
  und bei Bedarf `Observability` vorgesehen; keine Domain-, Persistenz- oder
  Projektionsabhängigkeiten. Direkte und transitive Abhängigkeiten automatisiert prüfen.
- `Contracts` hat keine Abhängigkeiten zu anderen Anwendungsprojekten. `Platform` enthält
  gemeinsame Namen ohne Framework-Abhängigkeiten. Dort keine Infrastrukturimplementierung ablegen.
- `Observability` und gemeinsam genutzte `Authentication` dürfen keine Domain- oder
  Persistenzabhängigkeiten in andere Hosts einschleusen. Fachliche Berechtigungsentscheidungen
  gehören nicht in eine gemeinsame Tokenvalidierungsbibliothek.
- Projektionshandler benötigen keine Domain-Referenz allein für vermeintliche Typsicherheit:
  Ein Merged-Zustand als `JsonObject` ist keine vom Compiler geprüfte Bindung an Domain-Typen.
  Tatsächlich notwendige Ausnahmen gezielt abgrenzen und begründen.
- Jede fachliche Regel hat genau einen Besitzer. Wiederverwendung über eine klar abgegrenzte
  Implementierung ermöglichen, statt Regeln in API, UI und Projektion unabhängig nachzubauen.
- Aggregat-, Stream- und Topologienamen zentral definieren. Notwendige Kopien in Compose oder
  Deploymentdateien automatisiert mit der maßgeblichen Definition vergleichen.
- Gemeinsame Build-Einstellungen in `Directory.Build.props`, Paketversionen in
  `Directory.Packages.props`, Formatierung in `.editorconfig` und SDK-Stand in `global.json`
  pflegen. Projektdateien enthalten die jeweiligen Besonderheiten.

### Vertical-Slice-Architektur und Dateiaufteilung

- Code nach fachlichen Bereichen und Anwendungsfällen als Vertical Slices organisieren.
  Innerhalb der beteiligten Projekte denselben Slice wiedererkennbar abbilden, statt alle
  Features in globalen Sammelordnern für Commands, Events oder Services zu vermischen.
- Die Projekt- und Abhängigkeitsgrenzen oben bleiben erhalten. Ein Slice umfasst den Weg
  durch die benötigten Schichten; er verlangt weder ein eigenes Projekt noch ein eigenes
  Aggregat pro Anwendungsfall. Gemeinsam geltende Invarianten bleiben beim selben Aggregat.
- Innerhalb eines fachlichen Bereichs beziehungsweise Slices passende Unterordner verwenden,
  etwa `Commands`, `Events`, `Handlers`, `Queries`, `Endpoints` oder `Components`. Nur benötigte
  Ordner anlegen. Gemeinsam verwendete Aggregate und Verträge beim fachlichen Besitzer ablegen,
  statt sie je Slice zu kopieren.

Beispiel innerhalb eines Domain-Projekts; die Namen stehen für den jeweiligen Fachbereich:

```text
<Fachbereich>/
  Aggregates/
    <Aggregat>.cs
  <Anwendungsfall>/
    Commands/
      <Absicht>Command.cs
    Handlers/
      <Absicht>CommandHandler.cs
    Events/
      <EingetreteneTatsache>.cs
```

- Jede Klasse erhält eine eigene Datei mit passendem Typnamen. Auch Handler, Validatoren und
  Hilfsklassen nicht als zusätzliche Klassen in derselben Datei sammeln. Interfaces und andere
  eigenständige Typen ebenfalls getrennt ablegen.
- Ausnahmen höchstens für eng zugehörige Enums oder kleine, fachlich zusammengehörige Records,
  wenn die gemeinsame Datei Lesbarkeit und Auffindbarkeit verbessert. Daraus keine Sammeldateien
  für unabhängige Verträge oder ganze Features machen.
- Tests nach denselben fachlichen Bereichen und Anwendungsfällen strukturieren.

### SOLID, insbesondere Single Responsibility

- SOLID bei Entwurf und Änderungen beachten. **SRP hat besonderes Gewicht:** Eine Klasse oder
  Komponente hat eine klar benennbare Verantwortung und einen zusammengehörigen Änderungsgrund.
  HTTP-Transport, fachliche Entscheidung, Persistenz, Darstellung und technische Orchestrierung
  nicht in einer Klasse vermischen. Methodenanzahl oder Dateigröße allein bestimmen keine Verantwortung.
- **Open/Closed:** Stabile Implementierungen über gezielte Erweiterungspunkte erweitern, wenn
  tatsächlich Varianten benötigt werden; keine vorsorglichen Frameworks für hypothetische Fälle bauen.
- **Liskov Substitution:** Implementierungen müssen die zugesagten Verträge ihrer Abstraktionen
  erfüllen, einschließlich Vorbedingungen, Ergebnissen und Fehlerverhalten.
- **Interface Segregation:** Kleine, am Bedarf ihrer Nutzer ausgerichtete Schnittstellen anbieten.
  Keine universellen Service-Interfaces, deren Nutzer jeweils nur einen Bruchteil benötigen.
- **Dependency Inversion:** Fachliche Abläufe hängen von geeigneten Ports ab; Infrastruktur
  implementiert diese. Konkrete Implementierungen am Composition Root verbinden.
- SOLID soll Verständlichkeit und Änderbarkeit verbessern. Keine zusätzlichen Abstraktionsschichten
  oder Interfaces ohne konkreten Nutzen einführen; Verantwortung und fachliche Kohäsion entscheiden.

## C#-Stil und Code-Dokumentation

- Vor jeder Änderung die für die betroffenen Dateien geltenden `.editorconfig`-Dateien lesen
  und beachten, einschließlich übergeordneter und verzeichnisspezifischer Regeln. Formatierung,
  Benennung und konfigurierte Analyzer-Schweregrade sind verbindlich. Regeln nicht abschwächen,
  um eine Änderung durchzubringen; vorhandene Formatierungsprüfungen verwenden.
- Bei Widersprüchen zwischen den folgenden Stilvorgaben und `.editorconfig` hat die für die
  jeweilige Datei wirksame `.editorconfig` Vorrang. Widersprechende Textvorgaben an ihrer
  maßgeblichen Stelle korrigieren; die Konfiguration nicht zur Umgehung einer Regel ändern.
  Die folgenden Vorgaben ergänzen Aspekte, die `.editorconfig` nicht festlegt.
- UTF-8, vier Leerzeichen Einrückung und abschließenden Zeilenumbruch verwenden.
- Geschweifte Klammern verwenden und öffnende Klammern auf eine eigene Zeile setzen.
- Instanzmember ausdrücklich mit `this.` ansprechen.
- Boolesche Umkehrungen als expliziten Vergleich wie `condition == false` schreiben,
  keinen unären Negationsoperator `!` verwenden. Der Null-Forgiving-Operator bleibt erlaubt,
  wenn die Nullable-Flussanalyse ihn erfordert.
- Konstanten in `UPPER_SNAKE_CASE`, private Felder in `_camelCase` benennen.
- Nullable Reference Types aktivieren.
- Typen und Methoden mit aussagekräftigen XML-Summaries dokumentieren. Parameter, Rückgaben,
  Ausnahmen und Semantik erklären, soweit dies über die Signatur hinaus informiert. Das gilt auch für private Methoden und Properties.
- Kommentare erläutern Gründe, Invarianten und nicht offensichtliches Verhalten.
  Öffentliche XML-Dokumentation beim Build erzeugen und fehlende Dokumentation sichtbar machen.
- Code und Bezeichner, XML-Summaries und weitere XML-Code-Dokumentation sowie Kommentare
  immer auf Englisch verfassen. Fachbegriffe im Code konsistent auf Englisch verwenden.
  Die Sprache der Projektdokumentation und der Benutzeroberfläche separat festlegen.

## Fachliche Modellierung von Commands und Events

- Vor einem Schreib-Slice den Vorgang beschreiben: Wer möchte was erreichen, welche
  Vorbedingungen gelten, was entscheidet das Aggregat und welche Tatsache steht danach fest?
- Commands drücken eine fachliche Absicht aus, Events benennen eingetretene Tatsachen in der
  Vergangenheitsform. HTTP-Verben, Formularfelder und Persistenzoperationen bestimmen nicht
  die fachlichen Grenzen.
- Keine generischen Create-/Update-/Delete-Commands oder Property-Patches als Ersatz für
  unterschiedliche Vorgänge verwenden. Auch ein fachlich klingender Name genügt nicht:
  erlaubte Eingaben, Invarianten und Ereignisbedeutung müssen zusammenpassen.
- Zusammengehörige Änderungen einer Aktion dürfen atomar in einem Command behandelt werden.
  Eigenständige Absichten mit eigenen Regeln trennen; keine künstliche Zerlegung je Feld.
- Das Aggregat entscheidet anhand von Command und aktuellem Zustand über die Events.
  Command, HTTP-Request, Event und Lesedokument sind eigenständige Verträge; keine automatische
  Eins-zu-eins-Abbildung aus einem allgemeinen Zustands-DTO ableiten.
- Atomare Regeln gehören in eine gemeinsame Konsistenzgrenze. Abfragen anderer Aggregate
  oder Projektionen erzeugen keine gemeinsame Transaktion. Mehrstufige Abläufe brauchen eine
  ausdrückliche Strategie für Zwischenstände, Wiederholung und gegebenenfalls Kompensation.
- Aggregatgrenzen auch auf Wachstum und Schreibkonkurrenz prüfen. Kein unbeschränkt wachsendes
  Sammelaggregat schaffen, das unabhängige Nutzeraktionen serialisiert.
- Fachliche Szenarien mit Ausgangszustand, Absicht, erwartetem Ereignis und Ablehnungen prüfen.
  Ein erfolgreicher technischer Command-bis-Query-Test allein nimmt das fachliche Modell nicht ab.

## Framework-Komposition und Schreibkonsistenz

- Paketkombination und Event-Store-Schema an der Kompatibilitätsmatrix ausrichten. Direkte und
  transitive Versionen prüfen. Private Paketquellen eindeutig zuordnen; Zugangsdaten außerhalb
  des Repositorys halten und die benötigte CI-Berechtigung dokumentieren.
- Vorhandene `AddDevbone*`-Registrierungen und Adapter verwenden. Persistenzmodus bewusst wählen;
  keine eigenen Adapter oder zusätzlichen Registrierungen ohne nachgewiesenen Bedarf ergänzen.
- Bei dauerhafter Veröffentlichung die transaktionale Outbox passend zum Persistenzmodus
  konfigurieren. Ein erfolgreicher Datenbank-Commit und ein separater Broker-Publish bilden
  keine gemeinsame Transaktion; direktes Publish ist kein Ersatz für eine dauerhafte Outbox.
- Request-Schlüssel über alle Retries derselben fachlichen Aktion stabil halten. Bei Neuanlagen
  muss auch die Ziel-ID stabil bleiben: Framework-Deduplizierung gilt innerhalb eines
  Aggregatstreams. Gültigkeitsbereich und Verhalten bei gleicher ID mit anderer Eingabe festlegen.
- Erwartete Concurrency-Tokens nach der verwendeten API behandeln; `null` nicht ungeprüft als
  Ausschalten der Konfliktprüfung interpretieren. Folgetokens aus bestätigten Commit-Ergebnissen
  übernehmen und Konflikte fachlich behandeln.
- Eigene Consumer auf mehrfache Zustellung auslegen, nach `EventId` deduplizieren und erst nach
  erfolgreicher Verarbeitung bestätigen. Dedup-Markierung und Nebenwirkung müssen auch bei
  Abstürzen konsistent bleiben; externe Nebenwirkungen benötigen eigene Idempotenz.
- Commit-Erfolg bedeutet noch keine Sichtbarkeit im Lesemodell. Wartezustände, begrenzte
  Nachprüfungen und Timeout-Verhalten explizit behandeln. Wenn die verwendete Paketkombination
  Projektionsbarrieren unterstützt und diese eingesetzt werden, außerhalb der Aggregatsperre
  abwarten; sie garantieren keine Konsistenz anderer Aggregate.

## Eventverträge, historische Daten und Migrationen

- Events als dauerhafte Verträge behandeln. Stabile Namen und Schreibversionen über
  `[EventContract("name", version)]` deklarieren.
- Kompatible Erweiterungen bevorzugen: optionale Properties mit sinnvollen Defaults,
  Nullable-Erweiterungen und zusätzliche Enum-Werte unter Beachtung bestehender Leser.
- Umbenennungen, Typ- und Bedeutungsänderungen als Vertragsänderung behandeln. Eine höhere
  Versionsnummer allein stellt keine Kompatibilität her. Unterstützte Versionierung und
  Migration anhand der verwendeten Framework-Version festlegen.
- Historische Payloads sowie die dafür erforderlichen Typen und `Apply`-Handler lesbar halten,
  solange entsprechende Streams erhalten bleiben. Feature-Flags dürfen die Verarbeitung
  bereits geschriebener Events nicht abschalten.
- Events mit dem produktiven Serializer hin- und zurücklesen und historische Payloads testen.
  Konstruktorbindung, Defaults und Null-Semantik ausdrücklich prüfen; ein erfolgreicher Commit
  allein beweist nicht, dass das Event später deserialisiert werden kann.
- Eindeutigkeit als persistierte Reservierung behandeln. Scope, Tenant-Bezug, Schlüssel und
  Zusammensetzung bewusst festlegen; zusammengesetzte Werte eindeutig kodieren.
- Änderungen an `[Unique]`, Reservierungen oder Vertragsnamen werden nicht automatisch migriert.
  Neue Deklarationen reservieren nichts rückwirkend; entfernte Deklarationen löschen keine
  vorhandenen Reservierungen. Einen Befund wie `uniqueness-changed` ausdrücklich bewerten.
- Für inkompatible Änderungen Migration, Backfill, Wiederaufbau oder gegebenenfalls Reset
  dokumentieren. Ein Schema-Erstellungsskript ist kein Nachweis für eine Upgrade-Migration.
- Ein Reset ist nur eine ausdrücklich beschlossene Option für entbehrliche Entwicklungsdaten.
  Betroffene Umgebung und Folgen benennen. Diese Vorlage erteilt keine pauschale Freigabe zum
  Leeren von Datenbanken oder Brokerdaten; mit echten Nutzerdaten gilt diese Option nicht.
  Kompatible Änderungen bevorzugen, Replay für jeden erhaltenen Stream sichern.

## Projektionen und Abfragen

Abfragen bestimmen die Form des Lesemodells. Zusätzliche Derived-Projektionen und duplizierte
Daten sind sinnvoll, wenn sie Abfragen vereinfachen. Speicherbedarf, Dokumentgrößen,
Schreiblast und Wiederaufbau bleiben dabei reale Kosten.

- Event-Payload und Projektion gemeinsam entwerfen: Die Standardprojektion führt die
  Domain-`Apply`-Methoden nicht aus. Benötigte Ergebniswerte müssen im Payload vorliegen.
- Vollständige Ergebniswerte dürfen enthalten sein; nicht jedes Event benötigt einen
  vollständigen Aggregatsnapshot. Bei Collections, entfernten Werten und `null` die tatsächliche
  Merge-Semantik prüfen. Payload-, Aggregat- und angereicherte Dokumentgrößen berücksichtigen.
- Jede weitere Projektion kann unabhängig hinterherhinken. Müssen zwei Anzeigen denselben
  Stand wiedergeben, sollen sie dasselbe Dokument lesen. Keine zweite Kopie einer fachlichen
  Entscheidungsregel in einer Projektion etablieren.
- Automatische Referenzauflösung prüfen: In den entsprechenden Framework-Versionen können
  `…Id`-/`…Ids`-Felder mit kanonischen IDs konfigurierter Aggregate im Merged-Zustand zu Objekten
  werden. Referenzabsicht, Tenant-Grenzen und erwartete JSON-Form testen; reine Akteursangaben
  nicht versehentlich als aufzulösende Referenzen modellieren.
- Derived-Handler erhalten aktuellen Merged-Zustand, der neuer als die auslösende
  Benachrichtigung sein kann. Daraus keine ungesicherten Ereignisdeltas oder Zähler ableiten.
  Historische Auswertungen benötigen eine Rekonstruktion aus der erhaltenen Eventhistorie.
- Idempotente Handler schreiben. Eventsequenzen gelten je Aggregatinstanz, nicht global.
  Ausgaben mehrerer Quellaggregate gegen konkurrierende Schreibzugriffe absichern.
- Beim Kopieren veränderlicher Fremddaten Abhängigkeiten über die vorgesehenen
  Framework-Mechanismen und tatsächliche Merged-Lesezugriffe registrieren. Automatische
  Anreicherung allein ersetzt kein Dependency-Tracking des Derived-Handlers.
- Änderungen an Transformation, Ausgabeform oder Abhängigkeitsverhalten durch eine passende
  `DefinitionVersion` kennzeichnen. Bei gemeinsamer Ausgabe alle betroffenen Writer abstimmen.
  DefinitionVersion unabhängig von Build- und Imageversionen halten.
- Ein Rebuild aus aktuellen Merged-Eingaben ist keine automatische Dokumentmigration.
  Bestehende Dokumente einschließlich veralteter Felder gezielt ersetzen oder migrieren.
  Insert-only-Writer und Ausgaben ohne verbleibende Quelle brauchen eine eigene Reparaturstrategie.
  Abgeschlossene Scanplanung nicht mit fertig verarbeiteten Lesemodellen gleichsetzen.
- Replikate derselben Worker-Komposition teilen die Deployment-Identität; unterschiedlich
  zusammengesetzte Hosts erhalten eigene Identitäten. Ablösung alter Deployments und Definitionen
  nach den Mechanismen der verwendeten Framework-Version durchführen.

## API, Sicherheit und optionale UI

- Fachliche Ablehnungen über stabile Fehlercodes und konsistente HTTP-Fehlerverträge abbilden,
  bei HTTP-APIs vorzugsweise ProblemDetails. Keine internen Ausnahmen oder Geheimnisse ausgeben.
- Für wiederholbare Schreibaktionen einen dokumentierten Idempotenzvertrag anbieten. Header,
  Gültigkeitsbereich, Ziel-ID und Ausnahmen pro API-Konvention festlegen; nicht allein aus dem
  HTTP-Verb auf die fachliche Identität einer Aktion schließen.
- Tenant, Besitz und Objektberechtigungen serverseitig prüfen, einschließlich Suche, Medien,
  Exporten und Referenzen. Gleicher Tenant bedeutet keine Zugriffsberechtigung.
- Regeln mit sofortiger Wirkung, etwa Berechtigungsentzug, anhand einer autoritativen Quelle
  prüfen. Die tolerierte Verzögerung anderer Berechtigungsprüfungen ausdrücklich festlegen.
  Rollen im Token, auf Aggregaten oder in einem Berechtigungsdienst sind Projektentscheidungen.
- Externe Identitäten stabil zuordnen. Bei Ableitung aus Issuer und Subject Änderungen am Issuer
  als Identitätsmigration behandeln. Bootstrap- und Betreiberzugriffe begrenzen und dokumentieren.
- Konfiguration beim Start validieren. Fehlende notwendige Einstellungen und ungültige
  Schlüssel verständlich melden. Unsichere Entwicklungsmechanismen außerhalb der vorgesehenen
  Umgebung ablehnen; Secrets nicht einchecken, loggen oder in Images einbauen.
- Bei Feature-Flags die API als maßgebliche Quelle verwenden und API sowie UI konsistent
  steuern. Sichtbarkeit und Aktualisierungsverhalten festlegen; interne Flags nicht automatisch
  öffentlich ausgeben. Historische Eventverarbeitung unabhängig von Produktflags erhalten.
- Bei Cookie-Authentifizierung den Schutz schreibender Endpunkte gegen CSRF berücksichtigen.
- Für das Webfrontend Blazor mit **MudBlazor als bevorzugter Komponentenbibliothek** einsetzen.
  Vorhandene Komponenten, Layouts und Themes nutzen; individuelle Komponenten dort ergänzen,
  wo sie fachlich oder für die Bedienbarkeit benötigt werden. Abweichungen begründen.
- Das Webfrontend muss auf Mobilgeräten und Desktop vollständig nutzbar sein. Responsive Layouts,
  Navigation, Formulare, Tabellen und Dialoge für kleine und große Ansichten gestalten;
  wesentliche Funktionen dürfen auf Mobilgeräten nicht entfallen oder unzugänglich werden.
- Zentrale Abläufe in mobilen und Desktop-Ansichten prüfen, einschließlich Touch-, Maus- und
  Tastaturbedienung. Unbeabsichtigten horizontalen Seitenoverflow vermeiden; breite Inhalte
  durch passende Ansichten oder gezielt begrenzte Scrollbereiche bedienbar halten.
- Bei einer UI Lade-, Fehler-, Konflikt- und Projektionswartefälle umsetzen. Anforderungen an
  Barrierefreiheit, Geräte und Eingabeverfahren in der Abnahme berücksichtigen.
- Wenn Mehrsprachigkeit benötigt wird, UI-Texte zentral verwalten, Ressourcen prüfen und die
  Auswahl- und Fallbackreihenfolge festlegen. Zur Laufzeit gepflegte Fachinhalte bleiben Daten;
  eine bestimmte Standardsprache oder Navigationstechnik ist keine Framework-Vorgabe.

## Verträge und automatisierte Prüfungen

- Den Eventkatalog generieren und gegen eine eingecheckte Vergleichsbasis prüfen.
  `DevboneStrictEventContracts` für die entsprechende Absicherung aktivieren.
- Kompatibilitätsprüfung und Aktualitätsprüfung unterscheiden: Eine kompatible Erweiterung
  kann trotzdem eine Aktualisierung des eingecheckten Katalogs erfordern.
- Änderungen am Katalog inhaltlich bewerten, bevor die neue Basis übernommen wird.
  Generierte Kataloge nicht von Hand korrigieren. Reproduzierbare Befehle und eindeutige
  Ausgabepfade dokumentieren.
- Bei mehreren Derived-Handlern einen Projektionskatalog oder gleichwertige automatisierte
  Prüfung für Ausgabeformen und Writer-Versionen vorsehen. Bei eigenen Dashboards Metriknamen,
  Einheiten und Labels gegen Instrumentierung beziehungsweise einen Metrikkatalog prüfen.
  Diese Prüfwerkzeuge sind bei Bedarf im Projekt anzulegen, keine vorausgesetzte Framework-API.
- Fachliche Regeln ohne Infrastruktur testen; Derived-Handler gegen einen kontrollierten
  Projektionsstore prüfen. Vertragsprüfungen ergänzen Verhaltenstests und ersetzen sie nicht.
- Mit echten verwendeten Infrastrukturkomponenten mindestens einen vollständigen Weg vom
  Command bis zum abgefragten Lesemodell, erneute Zustellung und einen Wiederaufbau nachweisen.
  Sicherheitsrelevante Änderungen auch mit unberechtigten Zugriffen prüfen.
- Integrationstests verwenden isolierte, entbehrliche Ressourcen und verändern keine lokale
  Entwicklungs- oder geteilte Umgebung. Parallelität nach Fixture-Isolation und Ressourcenbedarf
  begrenzen; Serialisierung nicht ungeprüft zur allgemeinen Pflicht machen.
- Testfilter nur verwenden, wenn die Fixtures unabhängig davon vollständig initialisieren.
  Infrastrukturtests dürfen in einer dafür vorgesehenen CI-Stufe nicht unbemerkt übersprungen werden.
- Einen dokumentierten Prüfablauf für Restore, Release-Build, Warnungsregeln, Architektur,
  Formatierung gemäß `.editorconfig`, Kataloge und Tests anbieten. CI muss diesen Ablauf aus
  einem frischen Checkout ausführen können.
  Warnungen im vereinbarten Build-Gate als Fehler behandeln; notwendige Ausnahmen eng begrenzen
  und begründen. Falls erforderlich nicht inkrementell bauen, damit Warnungen neu ermittelt werden.
- Gleichzeitige Builds und Tests in denselben Ausgabeverzeichnissen vermeiden. Aktuelle
  Exit-Codes und Ergebnisse auswerten; alte Ergebnisdateien sind kein Nachweis.
- Für Änderungen passende Prüfungen ausführen; vor Integration die vereinbarten Gates bestehen.
  Build, technische Integration und Produktabnahme als unterschiedliche Nachweise behandeln.

## Twelve-Factor App als pragmatische Orientierung

Die [Twelve-Factor-App-Regeln](https://12factor.net/) so weit wie sinnvoll berücksichtigen.
Sie sind eine Orientierung, keine zwingende Konformitätsprüfung. Nutzen und Aufwand für die
Anwendung abwägen; relevante Abweichungen mit Grund und Betriebsfolgen dokumentieren.

1. **Codebase:** Versionierten Quellstand eindeutig den Deployments zuordnen.
2. **Abhängigkeiten:** Explizit deklarieren und reproduzierbar bereitstellen.
3. **Konfiguration:** Umgebungsabhängige Werte außerhalb des Codes injizieren, etwa über
   Umgebungsvariablen oder eine geeignete Secret-Verwaltung.
4. **Backing Services:** Datenbanken, Broker und weitere Dienste als angebundene Ressourcen behandeln.
5. **Build, Release, Run:** Artefakterstellung, Konfigurationszuordnung und Ausführung trennen.
6. **Prozesse:** Anwendung möglichst zustandslos ausführen; dauerhaften Zustand extern halten.
7. **Port Binding:** Netzwerkdienste über klar konfigurierte Ports bereitstellen.
8. **Nebenläufigkeit:** Unabhängige Prozesstypen bedarfsgerecht skalieren.
9. **Disposability:** Zügigen Start und geordnetes Beenden ermöglichen.
10. **Dev/Prod Parity:** Entwicklungs-, Test- und Produktionsumgebungen möglichst ähnlich halten.
11. **Logs:** Logereignisse an die Betriebsumgebung ausgeben und dort verarbeiten lassen.
12. **Admin-Prozesse:** Wartungsaufgaben als kontrollierte einmalige Prozesse mit passendem
    Release- und Konfigurationsstand ausführen.

Event Sourcing verlangt dauerhafte Daten, aber keinen dauerhaften lokalen Prozesszustand.
Die vorhandenen Konsistenz-, Berechtigungs- und Wiederanlaufregeln gelten auch bei Skalierung
oder Neustarts; Twelve Factor rechtfertigt keine Aufweichung dieser Regeln.

## Lokale Infrastruktur, Deployment und Betrieb

- Lokalen Start und notwendige Provisionierung skripten; manuelle Schritte und Zuständigkeiten
  zusätzlich im Projekteinstieg erklären. Projektbezogene Namen, isolierte Volumes und konfigurierbare,
  konfliktfreie Hostports verwenden. Lokale Dienste standardmäßig an Loopback binden.
- Infrastruktur vor dem Workerstart bereitstellen. Für den entsprechenden Projektionsbetrieb
  MongoDB als initialisiertes Replica Set betreiben. Bei Kafka alle benötigten Topics inklusive
  interner Topics und DLQs vorab provisionieren; andere Transporte nach ihrem API-Guide behandeln.
- Schema-Erstellung, Upgrade und Versionsprüfung unterscheiden. Ein Erstellungsskript nicht
  über ein vorhandenes Schema ausführen. Abweichende Versionen erkennbar ablehnen.
- Reproduzierbare Testdaten bei Bedarf über die autorisierten Anwendungswege anlegen,
  beispielsweise die HTTP-API. Wiederholungen dürfen keine Duplikate erzeugen. Seeding nur in
  vorgesehenen Umgebungen auslösen und nicht pauschal bei jedem Produktionsstart ausführen.
- Für ausgelieferte Container Build, Start und vereinbarte Schwachstellenprüfungen ausführen.
  Ohne Rootrechte betreiben, Buildwerkzeuge aus Runtime-Images heraushalten, Secrets erst zur
  Laufzeit injizieren und Basisimages mit nachvollziehbaren Versionen und Digests festlegen.
  Branch-, PR- und Veröffentlichungspolitik passend zum Projekt dokumentieren; relevante
  Änderungen dürfen nicht ungeprüft integrieren.
- Zusammengehörige API- und Worker-Verträge bei Rollouts kompatibel halten; gemischte Versionen
  und an Definitionen gebundene Cursor beziehungsweise Wartebelege berücksichtigen.
- Liveness prüft die Lebensfähigkeit des Prozesses; fremde Dienstausfälle sollen keine nutzlosen
  Neustartschleifen auslösen. Readiness prüft die Fähigkeit zur vorgesehenen Arbeit beziehungsweise
  Verkehrsannahme. Readiness allein ist kein Neustartsignal.
- Lag oder einzelne unverarbeitbare Events nicht pauschal zur Readiness-Regel machen: anhand
  der zugesagten Funktion entscheiden und Fehler sowie Verzögerung zusätzlich überwachen.
  Öffentliche Health-Antworten knapp halten, sensible Diagnosen gesondert absichern.
- Telemetriequellen ausdrücklich abonnieren und den Export prüfen. Bei asynchroner
  Veröffentlichung auch die Trace-Fortsetzung über den verwendeten Outbox-/Brokerweg prüfen.
  Fehlende Exportkonfiguration und fehlgeschlagene Exporte unterscheiden.
- Verarbeitungsalter, Fehlerraten, Rückstände und DLQs beobachten. Labels mit unbeschränkter
  Kardinalität vermeiden. Bei mehreren Replikaten Doppelzählungen gemeinsamer Bestände verhindern;
  ein einzelner Messprozess ist nur eine mögliche Lösung.
- Ein erfolgreicher Prozessstart, Broker-Lag von null oder eine grüne Readiness belegen kein
  korrektes Lesemodell. Eigene Messwerte mit Umgebung und Prüfzeitpunkt dokumentieren.
- Diagnose an der betroffenen Schicht beginnen: persistiertes Event und Outbox, Zustellung,
  Raw-Zustand, Merged-Zustand, Derived-Ausgabe, Abfrage. Reparaturumfang möglichst eng begrenzen.
- Runbook-Verfahren mit Voraussetzungen, Zielumfang, Wirkung und Erfolgskontrolle dokumentieren
  und erproben. Ein Replay repariert nicht automatisch jede Derived-Ausgabe; vorhandene
  Checkpoints und Invalidierungen sowie die Writer-Semantik berücksichtigen.
- Broker-Replays mit anderen Consumern abstimmen. DLQs können Originalpayloads enthalten und
  benötigen passende Zugriffs- und Aufbewahrungsregeln.
- Vor Betrieb mit erhaltenswerten Daten Backup und Restore autoritativer Daten sowie den
  Wiederaufbau abgeleiteter Modelle nachweisen. Nicht rekonstruierbare Medien, Schlüssel und
  weitere benötigte Daten in das Sicherungskonzept einbeziehen.

## Zusammenarbeit und Abschluss

- Einfache, klar abgegrenzte Aufgaben an Subagents delegieren, wenn sie unabhängig von sinnvoller
  Hauptarbeit laufen können und die Umgebung Delegation unterstützt. Dazu gehören gezielte
  Suchen, mechanische Änderungen und das Ausführen eines bestimmten Testprojekts.
- Das günstigste ausreichend leistungsfähige Modell wählen. Bei tatsächlicher Schwierigkeit
  eskalieren, nicht allein wegen der Verfügbarkeit eines stärkeren Modells.
- Auftrag, Pfade, Kontext, Einschränkungen und erwartetes Ergebnis ausdrücklich mitgeben;
  vorhandenen Gesprächskontext nicht voraussetzen. Dateiverantwortung trennen.
- Architekturentscheidungen und wesentliche Abwägungen bleiben in der Hauptarbeit, sofern
  nicht ausdrücklich anders beauftragt. Ergebnisse und Änderungen vor Übernahme prüfen.
- Vor Änderungen den Arbeitsbaum und geltende Regeln prüfen. Vorhandene fremde Änderungen
  erhalten; keine sachfremden Umbauten oder unautorisierten Daten-Resets vornehmen.
- Eine Aufgabe erst als abgeschlossen markieren, wenn Verhalten, betroffene Dokumentation
  und Kataloge aktualisiert sowie die vereinbarten Prüfungen bestanden sind. Offene Abnahmen,
  Einschränkungen und nicht ausführbare Prüfungen konkret benennen.
