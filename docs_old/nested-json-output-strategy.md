# Nested JSON Output Strategy

## Bestandsaufnahme
- Aktuell erzeugen Output-Modelle flache Klassen, die `FOR JSON`-Resultate nicht explizit modellieren.
- Nested JSON (z.B. `Orders` innerhalb von `Users`) landet als `string` ohne strukturierte Unterstützung.
- Generatoren unterscheiden nicht zwischen JSON-Strings und relationalen Resultsets.

## Zielsetzung
- Nested JSON soll optional als stark typisiertes Objekt (`NestedOrder`, `OrderItem` etc.) generiert werden.
- Gleichzeitig muss eine Variante bestehen bleiben, die das Original-JSON als `string` beibehält (für Streaming/Raw-Szenarien).
- Konfiguration muss steuern, ob nested Objekte als Modelle („Flatten“) oder separate `JsonPayload`-Eigenschaft verfügbar sind.

## Vorgehensvorschlag

### 1. Inventory & Klassifizierung
- `spocr.json`: neue Sektion `output.jsonModels`.
- Erfassung pro Stored Procedure, ob `ReturnsJson` und ob `JsonColumns` (via `StoredProcedureContentModel.JsonColumns`).
- Neue Flags in Definition.Model (`HasJsonPayload`, `JsonShape`).

### 2. Model-Generierung
- Für JSON-Spalten: generiere `public string OrdersJson { get; set; }` (bestehendes Verhalten).
- Falls `generateNestedJsonModels = true`:
  - Erzeuge zusätzliche Klassen (`OrdersPayload`, `OrderItemPayload`).
  - Output-Model erhält zwei Properties: `public string OrdersJson { get; set; }` und `public OrdersPayload Orders { get; set; }`.
  - Führe `JsonSerializable`-Attribute optional ein.
- Template-Erweiterung im Model-Generator: JSON-Spalten-Liste iterieren, `JsonSchemaService` nutzen (siehe optional-json-deserialization.md für Deserialisierungstum).

### 3. Deserialisierungs-Hook
- Neue Helper in Output-Layer: `JsonPayloadFactory.Parse<T>(string json)`.
- Option `autoDeserializeNestedJson`: bool.
  - Wenn true: `SqlDataReaderExtensions` ruft `JsonPayloadFactory` auf und füllt `Orders` bei `ConvertToObject<T>`.
  - Wenn false: Property bleibt null, Konsument kann `Factory.Parse` manuell nutzen.

### 4. Generator/Configuration Änderungen
- `spocr.json`: Beispiel

```jsonc
{
  "project": {
    "output": {
      "jsonModels": {
        "generateNestedModels": true,
        "autoDeserialize": false
      }
    }
  }
}
```

- Engine liest Flags und beeinflusst Template-Bearbeitung.
- CLI-Doku anpassen, Default `generateNestedModels=false` (keine BC-Breaks).

### 5. Testplan
- Integrationstest mit Beispielprozedur `UserOrderHierarchyJson`.
- Snapshot-Test für generierte Modelle mit/ohne Flag.
- Unit-Test: `SqlDataReaderExtensions.ConvertToObject` mit JSON-Spalte -> bei aktivem `autoDeserialize` wird verschachteltes Objekt gefüllt.
- Performance-Test: Vergleich `autoDeserialize=true` vs. `false` (Benchmark).

## Empfehlungen
1. Schrittweise Umsetzung: erst Modell-Generierung (optional), dann Auto-Deserialisierung.
2. Spielfeld-Schnittstelle: `optional-json-deserialization.md` aufgreifen, da optionales Materialisieren dort beschrieben ist.
3. Dokumentation: README-Erweiterung + Beispielcode für neue Payload-Objekte.
