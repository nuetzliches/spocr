Anweisung fuer codex:
Halte dich an diese Todos, arbeite sie der Reihe nach ab.

Setze - [ ] fuer neue Todos
Setze - [x] fuer erledigte Todos

- [ ] passe samples\mssql\init an. trenne schema und tables, fuege custom data type und custom table types ein
  - [ ] analysiere die bestehenden Skripte in samples\mssql\init und dokumentiere welche Teile Schema, Tabellen, Typen und Seed-Daten enthalten
  - [ ] splitte die Schema-Definition in ein eigenes Skript (z.B. samples\mssql\init\01-create-schema.sql)
  - [ ] verschiebe die Tabellen-Definition in ein separates Skript (z.B. samples\mssql\init\02-create-tables.sql)
  - [ ] ergaenze ein Skript fuer custom scalar data types (z.B. samples\mssql\init\03-create-custom-types.sql)
  - [ ] ergaenze ein Skript fuer custom table types und verknuepfe es mit dem Tabellen-Skript
- [ ] verwende custom data types in den Tabellen
  - [ ] pruefe jede Tabelle auf Spalten, die auf die neuen custom data types wechseln sollen
  - [ ] passe die CREATE TABLE Skripte so an, dass die custom data types und table types verwendet werden
  - [ ] aktualisiere Seed-Daten oder Defaults, damit sie mit den neuen Typen kompatibel sind
  - [ ] gleiche EF Core Modelle und DbContext-Mappings mit den geaenderten Typen ab
- [ ] weitere Test Prozeduren erstellen: Multiple Resultsets, nested Json Objekte, Inputs mit custom data type und custom table types.
  - [ ] entwerfe eine Stored Procedure mit mehreren Resultsets inklusive unterschiedlicher Schemata
  - [ ] entwerfe eine Stored Procedure, die verschachtelte JSON-Objekte zurueckgibt
  - [ ] entwerfe eine Stored Procedure mit Inputparametern basierend auf custom data types und custom table types
  - [ ] dokumentiere jede neue Stored Procedure samt erwarteter Resultsets fuer die Tests
- [ ] Die Models sollen auf C# Ebene nicht implizit deserialisiert werden (Vorteil der Performance, direkt das Ergebnis an den Client weiterzugeben geht verloren!?) - Erstelle ein Konzept, wie dieser Vorgang optional durchgefuehrt werden kann.
  - [ ] analysiere die aktuelle Deserialisierung in StoredProcedureContentModel und verwandten Klassen
  - [ ] definiere eine Konfigurationsoption oder Pipeline, die die optionale Deserialisierung steuert
  - [ ] erstelle einen Prototyp, der zwischen direkter JSON Weitergabe und Deserialisierung umschalten kann
  - [ ] ermittle Performance-Kennzahlen fuer beide Varianten und dokumentiere die Ergebnisse
- [ ] Beruecksichtige in den Output Models, dass mit den JSON Strukturen nun auch nested Objekte abgebildet werden muessen. Wie ist das am besten zu loesen? Eine neue Output Property?
  - [ ] inventarisiere alle Output Models unter src/Output auf vorhandene JSON Properties
  - [ ] entwerfe eine Strategie fuer verschachtelte JSON (z.B. separate Payload-Klasse oder dynamische Struktur)
  - [ ] passe die Codegenerierung an, damit nested JSON korrekt serialisiert/deserialisiert wird
  - [ ] ergaenze Dokumentation fuer Konsumenten, wie nested JSON Felder zu verwenden sind
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
