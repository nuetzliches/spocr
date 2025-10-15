---
title: ResultSet Naming
position: 320
version: 4.5
status: draft
---

# ResultSet Naming (vNext)

Der vNext Generator versucht generische Platzhalter-Namen (`ResultSet1`, `ResultSet2`, …) durch aussagekräftige Tabellen-basierte Namen zu ersetzen.

## Wann wird umbenannt?

Eine Umbenennung findet nur statt, wenn ALLE folgenden Bedingungen erfüllt sind:

1. Ursprünglicher Name startet mit `ResultSet` (generisch)
2. Im gespeicherten SQL-Text (`Sql` Feld im Snapshot) wird für das entsprechende ResultSet eine eindeutig erkennbare Basistabelle gefunden (erste SELECT Query / Haupt FROM Quelle)
3. Der vorgeschlagene Name kollidiert nicht mit bereits vergebenen ResultSet Namen derselben Prozedur

Trifft eine Bedingung nicht zu, bleibt der generische Name stabil (Determinismus > Aggressive Heuristik).

## Beispiele

| SQL Ausschnitt | Vorher | Nachher | Begründung |
| -------------- | ------ | ------- | ---------- |
| `SELECT * FROM dbo.Users` | ResultSet1 | Users | Erste Basistabelle `Users` erkannt |
| `SELECT u.Id, r.Name FROM dbo.Users u JOIN dbo.Roles r ...` | ResultSet1 | Users | Erste FROM Tabelle gewinnt (nicht Roles) |
| `SELECT 1 AS X` | ResultSet1 | ResultSet1 | Keine Tabelle → keine Umbenennung |
| `SELECT * FROM #Temp` | ResultSet1 | ResultSet1 | Temporäre / nicht qualifizierte Tabelle ignoriert |
| `WITH C AS (SELECT * FROM dbo.Orders) SELECT * FROM C` | ResultSet1 | Orders | Fallback: erste Basisquelle in CTE (geplant) – aktuell noch ResultSet1 bis CTE Support implementiert |

(CTE / komplexe Fälle sind noch in Arbeit; Roadmap siehe unten.)

## Kollisionsvermeidung & Duplikate

Früher wurde bei einer Namenskollision (zweites ResultSet gleiche Basistabelle) kein Rename vorgenommen. Aktuell (vNext Erweiterung) gilt:

1. Erstes Auftreten einer Basistabelle erhält den berechneten Namen (`Users`).
2. Weitere ResultSets mit derselben erkannten Tabelle erhalten numerische Suffixe: `Users1`, `Users2`, ...

Dies garantiert Stabilität und dennoch höhere Aussagekraft als generische `ResultSetX` Namen.

## Mehrere ResultSets

Jedes ResultSet wird unabhängig betrachtet. Szenarien:

- Unterschiedliche Tabellen: Jede generische Instanz wird zu ihrem Tabellennamen umbenannt (sofern eindeutig & gültig)
- Gleiche Tabelle mehrfach: Suffix Schema wie oben (`Users`, `Users1`, `Users2`)
- Nicht ermittelbar / unparsable: Generischer Name (`ResultSetN`) bleibt bestehen

## Nicht-Ziele / Ausnahmen

- Dynamisches SQL (`EXEC(@sql)`) → wird ignoriert (Parser erkennt Basistabelle nicht sicher)
- Komplexe UNION / CTE Kaskaden → bleiben vorerst generisch
- JSON-Ausgaben (FOR JSON) ändern den Satz nicht; Fokus liegt auf tabellarischer Struktur

## Roadmap Verbesserungen

Geplante Erweiterungen (siehe CHECKLIST):
- CTE Support (Ableitung aus letzter SELECT Quelle)
- Alias-Auswertung bei `FOR JSON PATH` (Root Alias als Name)
- Performance Tuning (Parser Caching)
- Optionales Abschalten via Flag (noch nicht implementiert; aktuell always-on)
- Negative Tests für ungültiges SQL (bereits Fallback stillschweigend)

## Testabdeckung

Abgedeckt (Stand Erweiterung):
- Einfaches SELECT von Basistabelle (Rename)
- Duplicate Basistabellen: Suffixe (`Users`, `Users1`)
- Multi-Result: Nur resolvbare Sets umbenannt
- Unparsable SQL → Fallback generisch
- Mixed Case Tabelle → normalisiert (Case-insensitive Erkennung)

Geplant / Offen:
- CTE Struktur (erste echte Basistabelle in finalem SELECT)
- Dynamic SQL (`EXEC(@sql)`) Skip-Verifikation explizit
- FOR JSON PATH Alias-Nutzung

## FAQ

**Warum nicht jede JOIN Tabelle berücksichtigen?**  
Determinismus und Eindeutigkeit – mehrere Kandidaten würden zu instabilen Namen führen; daher nur erste FROM Quelle.

**Kann ich den Namen erzwingen?**  
Zurzeit nein; spätere Option: Überschreibungs-Metadaten oder Disabled-Flag.

**Beeinflusst das Hash/Determinismus?**  
Nur wenn Heuristik greift. Da identische SQL unverändert bleibt, ist Verhalten deterministisch.

---
Stand: Draft – wird mit Implementierung der nächsten Resolver Schritte aktualisiert.
