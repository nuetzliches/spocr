Anweisung fuer codex:
Halte dich an diese Todos, arbeite sie der Reihe nach ab und
setze - [ ] fuer neue Todos,
setze - [x] fuer erledigte Todos.

> Next Steps
> Generator & Output-Code für nested JSON anpassen (generateNestedModels/autoDeserialize Flags umsetzen).
> Danach README/Doku um Beispiel für verschachtelte Payloads ergänzen (offenes Sub-Task).

- [x] passe samples\mssql\init an. trenne schema und tables, fuege custom data type und custom table types ein
  - [x] analysiere die bestehenden Skripte in samples\mssql\init und dokumentiere welche Teile Schema, Tabellen, Typen und Seed-Daten enthalten
  - [x] splitte die Schema-Definition in ein eigenes Skript (z.B. samples\mssql\init\01-create-schema.sql)
  - [x] verschiebe die Tabellen-Definition in ein separates Skript (z.B. samples\mssql\init\02-create-tables.sql)
  - [x] ergaenze ein Skript fuer custom scalar data types (z.B. samples\mssql\init\03-create-custom-types.sql)
  - [x] ergaenze ein Skript fuer custom table types und verknuepfe es mit dem Tabellen-Skript
- [x] verwende custom data types in den Tabellen
  - [x] pruefe jede Tabelle auf Spalten, die auf die neuen custom data types wechseln sollen
  - [x] passe die CREATE TABLE Skripte so an, dass die custom data types und table types verwendet werden
  - [x] aktualisiere Seed-Daten oder Defaults, damit sie mit den neuen Typen kompatibel sind
  - [x] Fuege Tabellen-Spalten mit nullable Type hinzu
- [x] weitere Test Prozeduren erstellen: Multiple Resultsets, nested Json Objekte, Inputs mit custom data type und custom table types, passe die Namen der Prozeduren an, vermeide Nummerierung.
  - [x] entwerfe eine Stored Procedure mit mehreren Resultsets inklusive unterschiedlicher Schemata
  - [x] entwerfe eine Stored Procedure, die verschachtelte JSON-Objekte zurueckgibt
  - [x] entwerfe eine Stored Procedure mit Inputparametern basierend auf custom data types und custom table types
  - [x] dokumentiere jede neue Stored Procedure samt erwarteter Resultsets fuer die Tests
- [ ] Die Models sollen auf C# Ebene nicht implizit deserialisiert werden (Vorteil der Performance, direkt das Ergebnis an den Client weiterzugeben geht verloren!?) - Erstelle ein Konzept, wie dieser Vorgang optional durchgefuehrt werden kann.
  - [x] analysiere die aktuelle Deserialisierung in StoredProcedureContentModel und verwandten Klassen
  - [x] definiere eine Konfigurationsoption oder Pipeline, die die optionale Deserialisierung steuert
  - [ ] docs\optional-json-deserialization.md umsetzen
  - [ ] dokumentiere diese Anpassungen
- [ ] Beruecksichtige in den Output Models, dass mit den JSON Strukturen nun auch nested Objekte abgebildet werden muessen. Wie ist das am besten zu loesen? Eine neue Output Property?
  - [x] inventarisiere alle Output Models unter src/Output auf vorhandene JSON Properties
  - [x] entwerfe eine Strategie fuer verschachtelte JSON (z.B. separate Payload-Klasse oder dynamische Struktur)
  - [ ] passe die Codegenerierung an, damit nested JSON korrekt serialisiert/deserialisiert wird
  - [ ] ergaenze Dokumentation fuer Konsumenten, wie nested JSON Felder zu verwenden sind (nach Umsetzung)
- [ ] Den Output DataContext (C#) um einen weiteren Schalter erweitern "SET NO COUNT": "ON"|"OFF" Dies soll ueber die spocr.json und AppDbContextOptions konfigurierbar werden. Plane diesen Schritt zunaechst mit Unteraufgaben (Session Scope usw.)

Mit folgenden Todos noch warten bis wir weiter fortgeschritten sind:

- [ ] Tests implementieren: Auf Basis eines Docker Containers mit mssql DB die testbare StoredProcedures beinhaltet und zu einem soll-Output (spocr.json) fuehren soll? Daraus dann die Model-Generierung testen? Also mehrstufige Tests?
  - [ ] richte ein docker-compose Setup fuer eine MSSQL Testdatenbank inklusive Init-Skripten ein
  - [ ] automatisiere das Einspielen der Stored Procedures und der custom types im Container
  - [ ] generiere ein referenz spocr.json aus dem Docker Setup und versioniere es fuer Vergleiche
  - [ ] implementiere Integrationstests, die die Model-Generierung gegen die referenz pruefen
  - [ ] binde die neuen Tests in CI/Build Pipeline ein
- [ ] `SpocR.DataContext.Queries.StoredProcedureQueries` hier wird fuer jede Methode die ObjectId neu aufgeloest. Ist es nicht sinnvoller, diese gleich ueber die `StoredProcedureListAsync` mit abzufragen (gilt das auch fuer die `definition` - enthaelt diese auch `inputs` und `outputs` - reicht also der Abruf einer angereicherten/vollstaendigen Liste)?
  - [ ] analysiere die aktuelle Implementierung von StoredProcedureQueries und identifiziere redundante ObjectId-Abfragen
  - [ ] erweitere StoredProcedureListAsync um ObjectId, Definition, Inputs und Outputs
  - [ ] passe alle Call-Sites an, damit sie die erweiterten Ergebnisse verwenden
  - [ ] pruefe, ob die Definition saemtliche benoetigten Metadaten enthaelt oder weitere Joins erforderlich sind
- [ ] Naming Conventions aufweichen/entfernen oder konfigurierbar gestalten: Moeglichkeiten ausarbeiten.
  - [ ] dokumentiere bestehende Naming-Konventionen (z.B. in Config oder Hardcodings)
  - [ ] evaluiere, welche Konventionen optional sein sollen und welche Defaults bleiben
  - [ ] implementiere eine konfigurierbare Variante (z.B. ueber appsettings oder CLI Flags)
  - [ ] beschreibe im README, wie Anwender die Naming Konfiguration anpassen
- [ ] CRUD vs Result-Set Procedures: Wie unterscheiden wir diese? List vs Single? Bei JSON anhand WITHOUT_ARRAY_WRAPPER?
  - [ ] definiere Kriterien fuer CRUD, Single-Result und Multi-Result Stored Procedures
  - [ ] implementiere eine Klassifizierung, die auf Resultset-Metadaten und JSON Settings basiert
  - [ ] teste die Klassifizierung mit den neuen Beispielprozeduren aus samples\mssql\init
  - [ ] nutze die Klassifizierung, um passende Output Modelle oder Generierungslogik abzuleiten

Todos als Rohentwurf (müssen vervollständigt werden):

- [ ] Prüfen, ob der AppDbContext Anpassungen im Bereich Transaktions-Sicherheit, Sicherheit im Allgemeinen und verbesserter Konfigurations-Pipeline (aktuelles C# .NET Pattern) benötigt.
