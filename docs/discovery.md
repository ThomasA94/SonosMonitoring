# Discovery über Subnetze und lokale Sonos-APIs

Recherche: 28.09.2026. Technische Ergänzung zu [SM-002 und SM-016 im Gesamtplan](planung.md).
Die Anforderungs- und Entscheidungsstände bleiben im Gesamtplan. Dieser Text dokumentiert
Quellen, Entwurf und Prüfbedarf der ursprünglichen Recherche. Inzwischen sind begrenzte IPv4-Unicast-Erkennung, optionales SSDP auf expliziten lokalen Interfaces, lesendes SOAP-Topologiepolling und [Discovery aus aktuellen Topologiehinweisen](umsetzung-backend.md#discovery-aus-topologiehinweisen-sm-002-sm-019) implementiert. Optionaler GENA-Empfang mit geprüftem Initial-Callback, Lease-Erneuerung und Polling-Fallback ist ebenfalls implementiert; die reale Netzabnahme bleibt offen. Gruppen werden je Reporter mit unabhängiger Quellenqualität ausgegeben; gültige Mitgliedsadressen können begrenzte HTTP-Identifikationen innerhalb der freigegebenen CIDRs auslösen. SSDP und Topologie erweitern keine Zielnetze und ersetzen keine Unicast-Vollsuche. Es wurde während der Implementierung kein reales Sonos-Netz gescannt. Aktueller Stand: [Backend-Umsetzung](planung.md#umsetzungsstand-backend), Konfiguration und Grenzen: [optionales SSDP](umsetzung-backend.md#optionale-ssdp-erkennung-sm-002-sm-016-sm-019).

## SSDP und GENA im Alltag (SM-002, SM-016, SM-019)

Ergänzung vom 30.09.2026: Die Einstellungen enthalten diese Erläuterungen und eine
aufklappbare Einrichtungshilfe auf Deutsch und Englisch. Die folgenden Beispiele
beschreiben eine mögliche Einrichtung; sie aktivieren nichts. Im lokalen Prüfstack
bleiben SSDP und GENA deaktiviert. Der aktuelle Nachweis für drei per HTTP erkannte
Geräte und ihre Detaildiagnosen steht im [Gesamtplan](planung.md).

### Was leisten die beiden Protokolle?

**SSDP – Simple Service Discovery Protocol – findet Geräte.** Der Collector sendet
eine UDP-Multicast-Suche (`M-SEARCH`), Player antworten per Unicast. SSDP liefert
Kandidatenadressen; es sammelt keine Messwerte. **GENA – General Event Notification
Architecture – meldet Änderungen.** Nach einem HTTP-Abonnement (`SUBSCRIBE`)
sendet der Player HTTP-`NOTIFY` an die angegebene Rückrufadresse. Discovery und
Eventing sind getrennte UPnP-Verfahren. [D15]

| Entscheidung | Nutzen in SonosMonitoring |
|---|---|
| Nur erlaubte IPv4-Netze konfigurieren | Vollständige, begrenzte HTTP-Suche und anschließende Geräte-/Topologieabfragen funktionieren auch ohne SSDP oder GENA. Dies ist der Einstieg für geroutete Netze. |
| SSDP ergänzen | Zusätzliche Kandidatenerkennung auf expliziten, direkt angeschlossenen Schnittstellen. Anschließend bleiben HTTP-Identifikation und die vollständige CIDR-Suche erforderlich. |
| GENA ergänzen | Gruppentopologieänderungen stoßen zusätzliche aktuelle SOAP-Abfragen an. Regelmäßige Abfragen bleiben zur Absicherung aktiv. |

Der aktuelle GENA-Adapter abonniert ausschließlich den angekündigten
`ZoneGroupTopology:1`-Dienst am geprüften Pfad `/ZoneGroupTopology/Event`.
`ZoneGroupState` ist ein Hinweis auf einen notwendigen Abgleich; der Event-Payload
wird nicht direkt als neuer Gruppenstand übernommen. **Wiedergabe, Lautstärke,
Mute und Interface-Metriken werden weiterhin abgefragt.** GENA bedeutet in dieser
Anwendung kein vollständiges Push-Monitoring und keine Sonos-Steuerung. SSDP muss
für GENA nicht eingeschaltet sein: Auch per HTTP gefundene Geräte können abonniert
werden. Details: [GENA-Implementierung](umsetzung-backend.md#topologie-ereignisempfang-sm-016).

### SSDP einrichten und prüfen

1. Unter **Einstellungen → Netzwerke** Erfassung und erlaubte IPv4-Suchnetze
   konfigurieren. SSDP darf den erlaubten Zielbereich nicht ausweiten.
2. Bei **Lokale IPv4-Schnittstellen für SSDP** die eigenen IPv4-Adressen der
   Collector-/API-Laufzeit eintragen, eine pro Zeile. Keine Lautsprecher-IP,
   kein CIDR und keine Wildcard-Adresse. Die Adresse muss tatsächlich lokal
   zugewiesen sein; erlaubt sind höchstens acht unterschiedliche Interfaces.
3. Die ausgewählte Schnittstelle braucht Multicast-Zugang zum Sonos-Segment.
   Erforderlich sind UDP ausgehend an `239.255.255.250:1900` und die Antworten
   zurück zum kurzlebigen UDP-Quellport der Suche. Dieser Quellport ist nicht
   fest 1900. Die Implementierung verwendet TTL 1 und erreicht damit keine
   entfernten gerouteten Netze. Eine mDNS-Regel ist keine SSDP-Freigabe.
4. SSDP aktivieren und speichern. Das Empfangsfenster ist 2–10 Sekunden und
   muss kürzer als das Durchlaufzeitlimit sein; 1–256 Datagramme pro Interface
   begrenzen den Empfang. Ungültige oder übergroße Antworten zählen mit.
5. Nach der nächsten Vollsuche auf **Erfassung → SSDP** letzten Abschluss,
   Kandidaten, Schnittstellenfehler und Begrenzungen prüfen. Kandidaten werden
   erst nach erfolgreicher HTTP-Identifikation auf TCP 1400 als Geräte übernommen.

In Docker ist die Host-IP nicht automatisch ein Interface im API-Container.
Ein gewöhnliches Bridge-Netz oder die Veröffentlichung von UDP 1900 allein
stellt keinen direkten Multicast-Zugang zum Lautsprechersegment her. Eine
passende Netzwerkanbindung muss beim Deployment eingerichtet und geprüft werden.
Auch Hostnetz löst fehlende Routerfreigaben nicht. Für entfernte VLANs bleibt die
vorhandene Unicast-Suche der verwendete Weg; ein SSDP-Relay wird hier nicht
vorausgesetzt. Keine SSDP-Antwort beweist keinen Geräteausfall.

### GENA einrichten und prüfen

GENA benötigt einen zusätzlichen **Rückweg vom Lautsprecher zum Collector**.
Dass der Collector erfolgreich ein Gerät per HTTP erreicht, beweist diesen
Rückweg nicht.

| Einstellung | Bedeutung und Beispiel |
|---|---|
| Bindeadresse | Lokales Listener-Interface der API. `0.0.0.0` bindet alle lokalen IPv4-Interfaces. Eine konkrete Adresse muss dort verfügbar sein. |
| Rückrufadresse | Vom Sonos-Gerät erreichbare IPv4 des Collectors, bei Docker häufig die LAN-IP des Hosts, z. B. `192.168.6.10`. Nur die Adresse, keine URL. Dieses Beispiel durch die eigene erreichbare Adresse ersetzen. |
| Rückrufport | Dedizierter HTTP-Port, standardmäßig `48081`; Listener und angekündigter Rückrufport sind in dieser Implementierung identisch. Der Web-/API-Port erfüllt diese Aufgabe nicht. |
| Abonnementdauer | Gewünschte Dauer in Sekunden. Der Player gewährt die tatsächliche Lease; der Collector erneuert sie vor Ablauf automatisch. |
| Initial-Zeitlimit | Budget für die erste Benachrichtigung als Nachweis des Rückwegs. |
| Mindestabstand | Untergrenze zwischen zusätzlichen, ereignisgesteuerten Topologieabfragen je Gerät. Kein Ersatz für das reguläre Abfrageintervall. |

1. Erfassung aktivieren und Listener-/Rückrufadresse festlegen. `0.0.0.0` ist
   nur als Bindeadresse sinnvoll. `localhost`, `127.0.0.1` oder eine aus dem
   Sonos-Netz unerreichbare interne Container-IP sind keine Rückrufadresse für
   reale Player; Loopback ist nur in isolierten Tests zulässig.
2. Routing und Firewall für **Collector → Sonos, TCP 1400** und **Sonos →
   Rückrufadresse, TCP Rückrufport** erlauben. Die Benachrichtigung ist eine
   neue, vom Player initiierte Verbindung; die erste Freigabe genügt nicht.
3. In Docker den Rückrufport am **API-Container** mit demselben externen und
   internen Port veröffentlichen. Ein Beispiel für die oben genannte Host-IP
   wäre `192.168.6.10:48081:48081/tcp`. Im Compose-Deployment prüfen, ob dies
   bereits eingerichtet ist. Der Listener muss dort binden können. Die
   Anwendung erkennt NAT nicht automatisch und erstellt keine Portfreigaben
   oder Firewallregeln beim Speichern.
4. GENA aktivieren und speichern. Aktuell erfolgreich identifizierte Geräte
   mit passendem Topologiedienst werden automatisch abonniert. Geräte ohne
   diesen Dienst melden `not_supported`.
5. In den **Gerätedetails → GENA** sowohl ein gültiges Abonnement als auch
   eine empfangene Initialmeldung prüfen. Eine erfolgreiche SUBSCRIBE-Antwort
   allein bestätigt keinen Ereignisempfang. Die erste Meldung und spätere
   Gruppenänderungen müssen über den tatsächlichen Rückweg eintreffen. [D15]

Der Callback-Listener prüft zusätzlich, dass die Quell-IP der Benachrichtigung
zur zuvor identifizierten Geräteadresse gehört. Ein Reverse Proxy oder SNAT,
das diese Quell-IP ersetzt, ist deshalb kein transparenter Ersatz für den
direkten Rückweg. Normales Portmapping muss einschließlich tatsächlich
sichtbarer Quell-IP auf dem Zielhost geprüft werden.

Bei `awaiting_initial` läuft das erste Zeitbudget noch; `callback_unconfirmed`
bedeutet, dass eine Initialmeldung nicht rechtzeitig nachgewiesen wurde.
Dann Rückrufadresse, Listenerbindung, Portmapping und Firewall prüfen.
`subscription_failed` betrifft die Anmeldung oder Erneuerung; `listener_unavailable`
den lokalen Listener. Ein Portkonflikt oder eine nicht lokal zugewiesene
Bindeadresse kann den Listener verhindern. `sequence_gap` stößt einen
Abgleich an. Ausfälle betreffen GENA getrennt von HTTP-Erreichbarkeit und
Quellenqualität; reguläres Polling bleibt aktiv. Nach einer bestätigten
Initialmeldung ist ein lange unverändertes System zulässig: Eine alte letzte
Benachrichtigung allein beweist keinen kaputten Rückweg.

Die Anleitung beschreibt die vorhandene Implementierung. Reale SSDP-/GENA-
Netzabnahme, Router-/Container-Verhalten und Firmwareunterstützung bleiben
gezielte Prüfungen auf der jeweiligen Installation; die Protokollreferenz
ist kein Sonos-Supportversprechen für beliebige VLAN- oder NAT-Konfigurationen.

## 1. Ergebnis der offiziellen Dokumentationsprüfung

Die öffentlich dokumentierte Connected-Home-Architektur verweist auf die Cloud Control API.
Die LAN Control API wird dort als nicht allgemein verfügbar bezeichnet. Die Seite „Discover“
beschreibt das Auffinden von Haushalten und Gruppen nach Kontoautorisierung über das
Cloud-Gateway, keinen Scan frei konfigurierter lokaler Netze. [D1, D2]

Es gibt zugleich offizielle Inhalte zu lokalen Mechanismen: Der Cloud-Queue-Ablauf nennt
SSDP, eine Unicast-Antwort und eine sichere WebSocket-Verbindung zum Player. Das belegt die
Existenz dieses Integrationswegs, ist aber kein allgemeines Supportversprechen für unsere
UPnP- oder Diagnoseimplementierung. Die allgemeine Cloud-Subscription-Dokumentation beschreibt
HTTPS-Callbacks über die Sonos-Plattform und darf nicht mit lokalen GENA-Abonnements vermischt
werden. [D3, D4]

Die Sonos-Systemvoraussetzungen verlangen für die Sonos-App und alle Sonos-Produkte dasselbe
Subnetz. Unsere Anwendung soll von einem anderen Netz aus beobachten können und in mehreren
freigegebenen Netzen Geräte finden. Das ist eine anwendungseigene Fähigkeit. Daraus folgt weder
eine Sonos-Zusage für diesen Betrieb noch funktionierende gruppierte Wiedergabe zwischen
beliebig getrennten Lautsprechern. [D5]

Es wurde in den geprüften offiziellen Quellen kein allgemein freigegebener Vertrag für die
bekannten `/status/*`-Diagnoseansichten oder eine universelle Multi-CIDR-Discovery gefunden.
Die bisherigen Gerätebefunde bleiben in [Metriken und Datenquellen](metriken.md) dokumentiert.

## 2. Untersuchte Projekte

Bewertet wurden Dokumentation und ausgewählte Implementierungen. Kein Projekt wurde hier
ausgeführt. Sterne, Suchmaschinen-Crawldatum oder sichtbare Commitanzahl sind kein Nachweis
für aktuelle Pflege, Sicherheit oder Kompatibilität. Vor einer Abhängigkeit einen konkreten
Release/Commit, Lizenz, Zielruntime und Tests prüfen.

| Projekt | Belegter Ansatz | Bedeutung für unser Projekt |
|---|---|---|
| [SoCo/SoCo](https://github.com/SoCo/SoCo) | Python; `scan_network(networks_to_scan=...)` akzeptiert IPv4-Netze, ergänzt Discovery bei Multicastproblemen und kann mehrere Haushalte suchen. Gefundene Geräte liefern weitere Zonen. [D6] | Stärkste konkrete Referenz für die geforderte CIDR-Suche. Suchgrenzen und Parallelität selbst festlegen; Ergebnisse können veraltete oder nicht direkt erreichbare Zonen enthalten. |
| [Home Assistant Sonos](https://github.com/home-assistant/core/tree/dev/homeassistant/components/sonos) | Dokumentiert feste Hosts bei getrennten Subnetzen, konfigurierbare veröffentlichte Callback-Adresse und Polling bei blockiertem Rückweg. [D7] | Gute Betriebsreferenz für Richtungen der Netzwerkverbindungen und verständliche Fehlerzustände. Eine feste Hostliste erfüllt allein unsere automatische CIDR-Suche nicht. |
| [svrooij/node-sonos-ts](https://github.com/svrooij/node-sonos-ts) | TypeScript; SonosManager mit SSDP oder Start über eine bekannte Geräte-IP, Gruppenverwaltung und Ereignisse. [D8] | Referenz für Einstieg über ein Gerät und Topologieaktualisierung. Kein belegter Ersatz für unsere Suche in allen konfigurierten Netzen. |
| [jishi/node-sonos-http-api](https://github.com/jishi/node-sonos-http-api) | Node-HTTP-Bridge; dokumentiert Zustands-/Topologieereignisse und getrennte Ports für Erkennung, Gerätezugriff und Rückmeldungen. [D9] | Anschauung für spätere Steuerung und Ereignisweitergabe. Zusätzliche Node-Laufzeit wäre eine bewusste Architekturentscheidung. |
| [steipete/sonoscli](https://github.com/steipete/sonoscli) | Go; SSDP, Topologie und Fallback über lokale IPv4-Probes auf Port 1400 samt Gerätebeschreibung. [D10] | Zweiter konkreter Referenzweg für Discovery ohne funktionierendes Multicast. Seine lokale Subnetzsuche ist keine Zusage für unsere Multi-CIDR-Anforderung. |
| [svrooij/sonos-net](https://github.com/svrooij/sonos-net) | C# samt experimenteller Blazor-Weboberfläche. README bezeichnet das Projekt als unvollständig und Discovery als wenig funktionsfähig; GPL-3.0 angegeben. [D11] | Interessant für .NET-Serviceclients. Keine ungeprüfte Grundlage für unsere Discovery-/Ereignisanforderungen; Featureliste und tatsächlichen Code vor Auswahl abgleichen. |
| [svrooij/sonos-api-docs](https://github.com/svrooij/sonos-api-docs) | Community-Dokumentation und Generator auf Basis lokaler UPnP-Servicebeschreibungen. [D12] | Nützlich zum Abgleich konkreter Services und Gerätebeschreibungen; kein offizieller Sonos-Vertrag. |
| [sonn-audio/node-sonos](https://github.com/sonn-audio/node-sonos) | README beschreibt lokalen WebSocket-Weg für S2 und SOAP/GENA für S1. [D13] | Separater Erkundungskandidat für S2. Authentifizierung, Zertifikatsvertrauen, Lizenz und Hardwaretests offen; nicht aus dem README auf belastbaren allgemeinen Support schließen. |

SoCo kann standardmäßig nach dem ersten gefundenen Haushalt aufhören; der entsprechende
Parameter muss für vollständige Suche bewusst gewählt werden. Auch unser Verfahren darf nach
einem frühen SSDP-/Topologiefund nicht ungeprüft weitere konfigurierte Netze überspringen. [D6]

Empfehlung: C# als Implementierungssprache beibehalten und lokale Protokolle in einem
abgegrenzten Infrastrukturadapter kapseln. Bestehende Bibliotheken gegen die erforderlichen
Fähigkeiten prüfen. Ein Python-/Node-Sidecar bleibt eine Option, falls er nachweislich Aufwand
spart; mehrere lokale Sonos-Stacks parallel würden Fehleranalyse und Betrieb erschweren.
UPnP/SOAP ist der zuerst zu prüfende Weg für Discovery/Topologie; WebSockets separat erkunden.

## 3. Empfohlenes Discovery-Verfahren

Aktueller Rahmen laut Nutzer: zunächst `192.168.6.0/24`, später beliebige ausdrücklich
konfigurierte Suchnetze. LXC voraussichtlich in einem anderen Subnetz, pfSense dazwischen;
Regeln sind anpassbar. Ein zusätzliches LXC-Interface wäre möglich, wird für den unten
beschriebenen gerouteten Unicast-Weg jedoch nicht als Voraussetzung eingeplant.

Die folgenden Punkte sind unser Entwurf, keine von den Quellen zugesicherte Komplettlösung.

1. **Suchbereiche konfigurieren.** Explizite IPv4-CIDRs, optionale Startadressen und bei Bedarf
   lokale Netzwerkschnittstellen. Keine Ableitung des gesamten Scanumfangs aus einer einzelnen
   Geräte-IP. IPv6-Unterstützung separat priorisieren.
2. **Bekannte Geräte früh prüfen.** Zuletzt bekannte Adressen und optionale Startgeräte über
   HTTP erneut identifizieren. Eine veraltete IP darf nicht still einem anderen Gerät zugeordnet werden.
3. **SSDP ergänzend verwenden.** Auf direkt angeschlossenen, freigegebenen Schnittstellen
   M-SEARCH für Sonos senden und Antworten auswerten. Die übliche Multicastadresse ist
   `239.255.255.250:1900`. Für entfernte Netze wird keine SSDP-Weiterleitung vorausgesetzt. [D6]
4. **Konfigurierte Netze per Unicast durchsuchen.** Begrenzte TCP-/HTTP-Probes auf Port 1400;
   Kandidaten über `/xml/device_description.xml` als Sonos-Geräte validieren. Ein offener Port
   alleine genügt nicht. Hersteller, Gerätetyp, UDN und angebotene Services gemeinsam prüfen. [D10]
5. **Topologie erweitern.** Gefundene Player nach Zonen und Gruppen befragen. Nur weitere Ziele
   innerhalb des erlaubten Such-/Zugriffsbereichs abrufen. Außerhalb liegende Referenzen dürfen
   als solche angezeigt werden, ohne einen automatischen Netzaufruf auszulösen.
6. **Identitäten zusammenführen.** Geräte anhand stabiler UDN/UUID und Haushaltszuordnung
   wiedererkennen; IP als veränderliche Adresse behandeln. Subnetz und Haushalt sind verschiedene
   Konzepte. Mehrere Interfaces, Suchmethoden oder überlappende CIDRs erzeugen keine Duplikate.
7. **Fortlaufend abgleichen.** Schnelle Prüfungen bekannter Geräte, seltenere vollständige
   Suchläufe und ein manueller Suchstart. Abwesende Geräte samt Historie erhalten und nach
   Erreichbarkeitsregeln markieren; sie nicht allein aufgrund eines fehlenden SSDP-Pakets löschen.

Ein CIDR-Feld mit automatischer Suche ist für den Nutzer ein Discovery-Verfahren, auch wenn der
technische Fallback aktive Unicast-Probes statt Multicast verwendet. Eine Liste aller einzelnen
Geräte-IPs ist nicht erforderlich; Startadressen beschleunigen lediglich den Einstieg.

Grenzen vor Umsetzung festlegen: maximale Adressanzahl pro Lauf, Parallelität, Verbindungs- und
HTTP-Timeouts, Wiederholungen mit Wartezeit und Abbruch. Vorschlag für einen Versuch: bis zu
16 gleichzeitige Probes und deutlich seltenere Vollsuche als Metrikabfragen. Das ist kein
Framework-Grenzwert und keine zugesicherte Performance. Große CIDRs vor Start auf Größe prüfen;
überlappende Bereiche zusammenführen und Netzwerk-/Broadcastadressen passend zur Maske behandeln.
Ohne erlaubte CIDRs beziehungsweise ausdrückliche lokale Discovery-Konfiguration keine Vollsuche.

SSDP-LOCATION, Gerätebeschreibungen, Topologiereferenzen und HTTP-Redirects dürfen die konfigurierten
Zielgrenzen nicht ausweiten. Antworten mit Größenlimit und sicherer XML-Verarbeitung lesen.
Gefunden, direkt erreichbar, zuletzt gesehen und für Ereignisse abonniert getrennt führen.

## 4. Netzwerkverbindungen und Container

| Richtung | Zweck | Planung |
|---|---|---|
| Collector → Sonos, TCP 1400 | Gerätebeschreibung, lokale SOAP-Aufrufe und Diagnosen | Routing und Firewall aus dem Collector-Netz in jedes freigegebene Sonos-Netz erforderlich |
| Sonos → Collector, konfigurierter TCP-Port | Lokale GENA-NOTIFY-Ereignisse | Erreichbare veröffentlichte Callback-URL und passendes Binding/Portmapping erforderlich |
| Collector → SSDP-Multicast, UDP 1900; Antworten zurück | Zusätzliche Discovery | Passende Schnittstelle und Rückverkehr beachten; entfernte Netze optional nur mit passender SSDP-Weiterleitung |
| API/Grafana → TSDB sowie TSDB → Metrikexport | Historie/Monitoring | Unabhängig von Sonos-Multicast, eigene HTTP-Verbindungen |

Home Assistant dokumentiert dieses Callback-Problem ausdrücklich und nutzt bei fehlendem
Rückweg Polling. Sein Standardport ist integrationsspezifisch; er ist kein Sonos-Protokollzwang
für unsere Anwendung. Listeneradresse, Listenerport und veröffentlichte Callback-Adresse
getrennt konfigurieren, bei mehreren erreichbaren Netzwegen nötigenfalls je Zielnetz. [D7]

Ein erfolgreicher HTTP-GET vom Collector zum Lautsprecher beweist den Ereignisrückweg nicht.
Auch eine erfolgreiche Subscription-Antwort reicht ohne empfangenes Initial-/Änderungsereignis
nicht für den Nachweis. Stille Geräte müssen nicht ständig Ereignisse senden: fehlende spontane
Meldungen alleine beweisen keinen kaputten Rückweg. Abonnements erneuern und Zustand abgleichen.

Normales Unicast-Routing transportiert SSDP-Multicast nicht automatisch. Eine mDNS-Freigabe
ist nicht dasselbe wie eine SSDP-Weiterleitung. Unser Unicast-Fallback vermeidet diese
Voraussetzung. Docker-Hostnetz teilt den Host-Netzwerkstack, löst aber keine fehlenden Routen
oder Router-Firewallregeln. Ein LXC im Servernetz kann mit erlaubtem Hin-/Rückweg genügen;
eine zusätzliche Schnittstelle in jedem Sonos-Netz ist damit nicht zwingend.

### Diagnose: Host erreicht das Gerät, Docker-Collector nicht

Ein abgeschlossener Suchlauf mit 254 geprüften Adressen bedeutet 254 Probeversuche,
nicht 254 erreichbare HTTP-Dienste. Auch bei vollständigem Verbindungs-Timeout kann
der Lauf regulär abgeschlossen sein. Zuerst dieselbe bekannte Geräteadresse vom
Host und aus dem tatsächlich laufenden API-Container prüfen, jeweils auf TCP 1400
und `/xml/device_description.xml`.

Alle Docker-Netze auf Überschneidung mit den erlaubten Sonos-CIDRs prüfen, auch
Netze anderer Projekte. Ein fremdes Bridge-Netz kann den Verkehr innerhalb des
Docker-Hosts zum falschen Zielnetz routen. Nur das Netz von SonosMonitoring zu
ändern beseitigt eine weiterhin vorhandene fremde Überschneidung nicht. Docker
dokumentiert diesen Konflikt und die explizite Subnetzvergabe beziehungsweise
angepasste Adresspools: [Docker Networking](https://docs.docker.com/engine/network/#subnet-allocation).

**Befund vom 30.09.2026 (SM-002/SM-019):** Das bekannte Gerät `192.168.6.55`
lieferte vom Windows-Host HTTP 200 für Status und Gerätebeschreibung. Hersteller,
ZonePlayer-Typ, UDN-Form und Topologieservice entsprechen bei Sichtprüfung den
Identifikationskriterien. Aus dem API-Container blieb schon der TCP-Verbindungsaufbau
innerhalb fünf Sekunden ohne Erfolg; der HTTP-Versuch lief nach acht Sekunden ab.
Das aktive fremde Netz `sharedshelf_default` verwendet `192.168.0.0/20` und
überschneidet sich nachweislich mit dem Suchnetz `192.168.6.0/24`. Das eigene
Sonos-Netz verwendet `192.168.128.0/20`. Die Überschneidung ist die wahrscheinliche
Routingursache; ihre Beseitigung und anschließende erfolgreiche Geräteerkennung
sind noch nicht verifiziert. Es wurden weder Docker-Netze noch Container geändert.

Für die Korrektur ein gegen Docker-Netze und Host-/VPN-Routen geprüftes Subnetz
dauerhaft in der betroffenen Compose-Konfiguration festlegen. Der aktuell geprüfte
Kandidat ist `10.253.240.0/24`. Die sechs angeschlossenen SharedShelf-Dienste gehören
zu einem anderen Projekt; ihre Netzumstellung benötigt eine gesonderte Freigabe
wegen kurzzeitiger Verbindungsunterbrechungen. Containerneustarts vermeiden:
SharedShelf verwendet einen flüchtigen Keycloak-Speicher. Netzwerk-Aliase und
Portfreigaben beim Umhängen erhalten und anschließend prüfen. Keine Datenvolumes
löschen. Danach den Geräteabruf aus dem API-Container und die vollständige Suche
erneut prüfen; drei vom Nutzer erwartete Geräte sind noch kein geprüfter Fund.

**Nachprüfung am 30.09.2026, 11:45 Uhr Europe/Berlin:** Der Nutzer hat SharedShelf
auf `172.16.42.0/24` umgestellt. Der Abruf von `/xml/device_description.xml` auf
`192.168.6.55:1400` direkt aus dem SonosMonitoring-API-Container liefert jetzt HTTP 200.
Die drei vorhandenen SonosMonitoring-Infrastrukturcontainer waren mit Exit 255 beendet;
die API startete wegen fehlender PostgreSQL-Namensauflösung wiederholt neu. Nach Start
derselben PostgreSQL-/RabbitMQ-/MongoDB-Container ohne Konfigurations- oder Volumeänderung
schloss die automatische CIDR-Suche um 11:45:06 Uhr erfolgreich ab: **254 Adressen
geprüft, drei Geräte gefunden, kein Bereichsfehler**. Alle drei liefern erfolgreiche,
aktuelle Gerätebeschreibungen sowie erfolgreiche Topologieantworten (zwei SYMFONISK
Bookshelf und eine SYMFONISK Table lamp). Damit ist die Erreichbarkeit dieser realen
Geräte aus dem Docker-Collector nach der Netzkorrektur nachgewiesen. Die frühere
Korrekturplanung oben wurde von uns nicht ausgeführt. Detaildiagnosen melden für alle
drei Geräte `monitoring_disabled`; die gerätebezogene Überwachung wurde nicht aktiviert.
GENA, zusätzliche Netze, weitere Modelle und eine Dauerabnahme bleiben offen.

**Erneute Überlappung am 30.09.2026 während der Grafana-Abnahme:** Nach dem Update
lieferten zunächst alle drei überwachten Geräte frische Detaildaten und lesbare
Prometheus-Namensmetadaten. Später meldeten alle Beschreibungsabrufe Timeouts.
Der Windows-Host erreicht weiterhin alle drei Geräte mit HTTP 200; TCP 1400 aus
dem API-Container scheitert. `sharedshelf_default` liegt weiterhin im korrigierten
`172.16.42.0/24`. Neu hinzugekommen ist jedoch `jacom-tenant-spike_default` mit
`192.168.0.0/20`, das den Sonos-Suchbereich einschließt. Eine lesende Prüfung der
Docker-Hostroute bestätigt `192.168.0.0/20` über `br-762eb9803c1e`; diese Bridge
gehört genau zum neuen Netz. Es gibt in der geprüften Tabelle keinen spezifischeren
LAN-Weg für den Sonos-Suchbereich. Der Verkehr wird damit in dieses Docker-Netz geleitet.

Die Grafik markiert die alten Quellen und unterdrückt aktuelle Werte; Name und
bereits gespeicherte Historie bleiben verfügbar. Das ist eine tatsächliche
Erfassungsunterbrechung, kein Beleg eines Sonos-Geräteausfalls oder eines
Grafana-Abfragefehlers. Das neue Netz gehört zu einem anderen Projekt und wurde
nicht verändert. Die dort maßgebliche Compose-Konfiguration muss ein gegen LAN-,
VPN- und Docker-Netze geprüftes, nicht überlappendes Subnetz erhalten. Bestehende
Container/Konten/Volumen und projektbezogene Regeln beim geplanten Wechsel beachten;
anschließend TCP/HTTP aus dem Sonos-Container und die Quellenfrische erneut prüfen.
Der genaue Dashboard-Prüfstand steht im [Gesamtplan](planung.md#protokollhilfe-und-detaillierte-grafana-ansichten-30092026).

### Auswertung der Datenlücke und App-Wakeup, 30.09.2026

Nachfrage des Nutzers zu einer ungefähr 30-minütigen Datenlücke, bezogen auf
SM-002/SM-005/SM-019. Alle folgenden Uhrzeiten sind Europe/Berlin. Der Nutzer
bestätigt, die Handy-App ungefähr um 17:54 Uhr geöffnet zu haben.

**Gemessener Verlauf:** Eine lesende Prometheus-Auswertung von 17:00 bis 18:04 Uhr
mit 15-Sekunden-Abfrageschritt zeigt für alle drei Geräte denselben Ausfallverlauf:

| Zeitpunkt | Nachweis |
|---|---|
| Ab 17:22:30 | Fehlgeschlagene Beschreibungsabrufe, dazwischen noch einzelne Erfolge |
| 17:25:18–19 | Letzte erfolgreiche Beschreibungen vor der zusammenhängenden Lücke |
| 17:25:45–17:54:00 | Letzter Beschreibungsabruf durchgehend fehlgeschlagen; die gespeicherten Erfolgszeitpunkte ändern sich nicht |
| Ab 17:26:15 | Auch die Interfacequelle durchgehend nicht frisch |
| 17:54:03 | Alle drei Geräte liefern wieder erfolgreiche Beschreibungen |
| 17:54:15 | Wiederkehr von Beschreibungs- und Interfacefrische erstmals im Abfrageraster sichtbar |
| Nachprüfung um 18:04 Uhr | Alle sieben Quellen aller drei Geräte erfolgreich und frisch; Windows-HTTP und Collector-TCP 1400 funktionieren |

Die zusammenhängende Lücke zwischen den tatsächlichen erfolgreichen Beschreibungen
beträgt damit knapp 29 Minuten. Das Prometheus-API-Scrape `up` bleibt im gesamten
geprüften Fenster 1; während der zusammenhängenden Lücke sind 106 verschiedene
Collector-Heartbeat-Zeitpunkte nachgewiesen, mit maximal 72 Sekunden Alter im
Abfrageraster. Der laufende
API-Container startete um 17:09:30 Uhr und meldet null Neustarts. Es handelt sich
um eine Geräteerfassungsunterbrechung bei weiter erreichbarer API. Der reduzierte
lokale Messbeleg liegt unter `artifacts/test-results/outage-investigation.json`;
Kennungen und Raumnamen wurden darin durch Aliasse ersetzt beziehungsweise weggelassen.
Das Abfrageraster begrenzt die Aussage über den sichtbaren Zustandswechsel; die
separat abgefragten Erfolgszeitpunkte stammen aus der Collector-Metrik selbst.

**Ursachenbewertung:** Während der Lücke wurde die oben dokumentierte Überlappung
durch `jacom-tenant-spike_default` einschließlich Docker-Hostroute nachgewiesen.
Gleichzeitig antworteten dieselben drei Geräte dem Windows-Host mit HTTP 200,
während Verbindungen aus dem API-Container scheiterten. Damit waren die Geräte
zu diesem Prüfzeitpunkt eingeschaltet und ihr HTTP-Dienst erreichbar; ein Schlaf
aller Lautsprecher erklärt die unterschiedliche Erreichbarkeit nicht.
Bei der Nachprüfung ist dieses Docker-Netz nicht mehr vorhanden und seine
`192.168.0.0/20`-Route verschwunden. Diese Arbeit hat das fremde Netz weder
entfernt noch umkonfiguriert. Die begrenzte Docker-Ereignisabfrage liefert für
das untersuchte Zeitfenster keine gespeicherten Netzwerkereignisse mehr.
Der genaue Zeitpunkt der Netzentfernung ist daher nicht belegt.

Die vom Nutzer bestätigte App-Öffnung und der Wiederbeginn fallen zeitlich zusammen.
Es fehlt jedoch eine Paketaufzeichnung der App-Kommunikation sowie ein
Routingnachweis genau zu diesem Zeitpunkt. Deshalb ist ein zusätzlicher Effekt
der App offen; die Korrelation beweist keinen Wakeup. Ein App-Wakeup würde den
nachgewiesenen, falschen Docker-Routingweg nicht reparieren.

**Dokumentierte Wakeups:** Sonos beschreibt für tragbare Akkuprodukte bei
deaktiviertem Battery Saver einen Schlaf nach 30 Minuten Inaktivität und
automatisches Aufwachen beim Öffnen der App. Bei aktiviertem Battery Saver
schalten diese Geräte stattdessen vollständig ab. [D16]
Der geprüfte Bestand besteht aus zwei netzbetriebenen SYMFONISK Bookshelf und
einer SYMFONISK Table lamp; er fällt nicht unter diese Akkuproduktbeschreibung.
IKEA beschreibt für den Bookshelf Gen 2 (Typ E1922) einen Netzwerk-Standby ohne
Audiowiedergabe. Das belegt keinen 30-Minuten-Schlaf oder garantierten
App-Wakeup für die hier vorhandenen Modelle und Firmwarestände. [D17]
In den geprüften Primärquellen wurde kein allgemeiner Wakeup-Befehl für diese
SYMFONISK gefunden; das ist kein Beweis, dass die App keinerlei interne
Aktivierungs- oder Aktualisierungseffekte haben kann.

**Erfassung und weiteres Vorgehen:** Der Collector besitzt keinen
30-Minuten-Ruhetimer und fragt bekannte Adressen unabhängig vom Browser ab.
Er liest zuerst die Gerätebeschreibung; schlägt sie fehl, werden die nachfolgenden
Topologie- und Diagnoseabrufe dieses Durchlaufs nicht gestartet. Deshalb werden
Netzwerk-, Wiedergabe- und Lautstärkedaten bei dieser Störung gemeinsam alt.
SSDP und GENA waren und sind deaktiviert; die gemessene Wiederkehr erfolgte im
Pollingbetrieb. Ihre Aktivierung ist keine Reparatur einer falschen Unicast-Route.

Beim nächsten Auftreten zuerst Uhrzeit, API-Quellenzustände und Collector-Heartbeat
festhalten und dieselbe Gerätebeschreibung vom Windows-Host sowie TCP/HTTP 1400
aus dem API-Container prüfen. Zeitgleich die Docker-Subnetze und Docker-Hostroute
für das Sonos-Netz sichern, bevor App oder Netz geändert werden. Scheitert nur
Docker, am Routing ansetzen. Scheitern beide Wege bei korrektem Routing, für eine
gesonderte Wakeup-Untersuchung den App-Öffnungszeitpunkt und gezielt gefilterten
LAN-Verkehr vorher/nachher erfassen. Eine solche Aufzeichnung oder aktive
Wakeup-Steuerung wurde hier nicht ausgeführt. Bei einem weiteren Start des
betroffenen Fremdprojekts dessen maßgebliche Netzkonfiguration auf ein dauerhaft
überschneidungsfreies Subnetz prüfen; die aktuell fehlende Bridge allein bestätigt
noch keine dauerhafte Konfigurationskorrektur.

### Vorgeschlagener pfSense-Netzplan

Die Collector-IP, Interface-Namen und der feste Callback-Port müssen vor Umsetzung bestimmt
werden. Vorläufige Regeln als Plan, keine ausgeführte Firewalländerung:

| Eingangsinterface in pfSense | Initiator → Ziel | Freigabe |
|---|---|---|
| Interface des LXC-Netzes | Collector-IP → konfigurierte Sonos-Suchnetze | TCP-Zielport 1400 für Discovery/Abfragen |
| Jeweiliges Sonos-Netzinterface | Sonos-Geräte → Collector-IP | TCP-Zielport des festgelegten GENA-Listeners |

pfSense filtert normale Interfaceregeln am eingehenden Interface und erlaubt Antworten über
den erzeugten Verbindungszustand. Sonos-NOTIFY ist aber eine neu vom Lautsprecher aufgebaute
Verbindung und benötigt daher die zweite Freigabe; sie ist nicht nur Antwortverkehr auf eine
Abfrage. Bestehende Floating-/Gruppenregeln und Routing beim späteren Test mitprüfen. [D14]

Zwischen direkt gerouteten internen Netzen ist für diesen Entwurf keine Adressübersetzung
geplant. Die Callback-URL muss auf die von den Lautsprechern erreichbare LXC-IP zeigen.
Für Docker gegebenenfalls auf erreichbare Host-IP plus veröffentlichten Listenerport abbilden.
WebSocket-/Audiofunktionen können zusätzliche Geräteports benötigen, gehören aber nicht
ungeprüft in die initiale Discovery-Freigabe. SSDP-Relay oder zweites Interface bleiben
optionale Ergänzungen; zuerst Unicast-Discovery plus tatsächliche Callback-Zustellung testen.

Falls eine Netzgrenze absichtlich keinen passenden Verkehr erlaubt, wäre später ein Collector
im jeweiligen Netz eine Alternative. Mehrere Collector-Instanzen mit Übernahme/Deduplizierung
sind zusätzlicher Umfang und noch nicht beschlossen. Die konkrete Routerkonfiguration bleibt
offen; in dieser Recherche wurden keine Netzregeln geändert.

## 5. Abnahme und ausstehende Prüfungen

Die Quelle belegt die Machbarkeit einzelner Verfahren; die eigene Umsetzung muss nachweisen:

- Zwei konfigurierte geroutete Testnetze mit blockiertem SSDP: erreichbare Sonos-Geräte in
  beiden finden; ein früher Fund darf die zweite Suche nicht beenden.
- Unterschiedliche Haushalte und ein Haushalt mit mehreren Zonen korrekt unterscheiden.
  Mehrere Haushalte erkennen bedeutet noch keine vollständige Mehrhaushalt-Verwaltungsoberfläche.
- Offener Port 1400 mit fremdem HTTP-Dienst wird nicht als Sonos übernommen.
- Überlappende CIDRs, doppelte SSDP-Antworten, IP-Wechsel und wiedervergebene IPs ohne Duplikate
  oder falsche Historienzuordnung behandeln.
- Gesperrtes Teilnetz verursacht einen sichtbaren Teilausfall, blockiert aber andere Netze nicht.
- Funktionierende HTTP-Abfragen bei blockiertem Callback ergeben eingeschränkten Livebetrieb;
  nach Freigabe/Neuverbindung Ereignisempfang und Rückkehr aus Polling prüfen.
- Geräte-/Topologieverweise außerhalb erlaubter Ziele lösen keine ausgehenden Abrufe aus.
- Auf dem realen LXC und Docker-Testhost Suche, Renewal, Reconnect, Ressourcen und Last messen.

Zum ursprünglichen Recherchestand verifiziert: öffentliche Dokumentations-/Quellcodebefunde
und der dokumentierte lesende Zugriff auf ein einzelnes Gerät vom aktuellen Rechner.
Am 30.09.2026 wurden nach der oben beschriebenen Docker-Netzkorrektur drei Geräte per
CIDR-Suche erkannt; die spätere Detaildiagnoseabnahme steht im [Gesamtplan](planung.md).
Weiterhin nicht real verifiziert: Discovery über mehrere Subnetze, VLAN-Routing vom
Zielhost, SSDP-/GENA-Rückweg, WebSockets oder Funktionsfähigkeit einer der aufgeführten
Bibliotheken im eigenen Netz. Die ausführbaren Protokollsimulationen sind getrennt
in der [Backend-Umsetzung](umsetzung-backend.md) dokumentiert.

## Quellen

Bewegliche Repository-Links vor Wiederverwendung auf eine geprüfte Revision festlegen.

- D1: [Sonos Connected Home Architecture](https://docs.sonos.com/docs/connected-home-architecture)
- D2: [Sonos Discover](https://docs.sonos.com/docs/discover)
- D3: [Sonos Cloud Queue: Play audio](https://docs.sonos.com/docs/cloud-queue-play-audio)
- D4: [Sonos Subscribe](https://docs.sonos.com/docs/subscribe)
- D5: [Sonos System requirements](https://support.sonos.com/en/article/sonos-system-requirements)
- D6: [SoCo discovery.py](https://github.com/SoCo/SoCo/blob/master/soco/discovery.py)
- D7: [Home Assistant Sonos: Network requirements / Advanced use](https://www.home-assistant.io/integrations/sonos/)
- D8: [node-sonos-ts README](https://github.com/svrooij/node-sonos-ts)
- D9: [node-sonos-http-api README](https://github.com/jishi/node-sonos-http-api)
- D10: [sonoscli Discovery-Dokumentation](https://sonoscli.sh/discovery.html)
- D11: [sonos-net README](https://github.com/svrooij/sonos-net)
- D12: [sonos-api-docs](https://github.com/svrooij/sonos-api-docs)
- D13: [sonn-audio/node-sonos README](https://github.com/sonn-audio/node-sonos)
- D14: [Netgate: pfSense Rule Methodology](https://docs.netgate.com/pfsense/en/latest/firewall/rule-methodology.html)
- D15: [OCF UPnP Device Architecture 2.0, Revision 17.04.2020](https://openconnectivity.org/upnp-specs/UPnP-arch-DeviceArchitecture-v2.0-20200417.pdf), Abschnitte 1 (Discovery) und 4 (Eventing).
- D16: [Sonos: Battery settings for portable Sonos products](https://support.sonos.com/en/article/battery-settings-for-portable-sonos-products?language=en), geprüft am 30.09.2026.
- D17: [IKEA: SYMFONISK Bookshelf Gen 2, Typ E1922, Anleitung](https://www.ikea.com/us/en/manuals/symfonisk-sonos-wifi-bookshelf-speaker-black-smart-gen-2__AA-2286628-2-100.pdf), Netzwerk-Standby auf Seite 5, geprüft am 30.09.2026.
