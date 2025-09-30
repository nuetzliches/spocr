# Optional JSON Deserialization Concept

## Ausgangssituation

- `AppDbContextPipe.ReadJsonAsync<T>` deserialisiert Ergebnisse immer per `JsonSerializer.Deserialize<T>`.
- Generator ersetzt JSON-Prozeduren automatisch durch `ReadJsonAsync<T>`, wodurch Modelklassen fix entstehen und der Roh-JSON-Stream verloren geht.
- Aufrufer, die das JSON direkt an den Client weiterreichen wollen (z.B. HTTP Response Streaming), zahlen die Kosten der Deserialisierung und eines erneuten Serialisierens.

## Zielbild

- JSON-Ergebnisse sollen optional als `string` oder `JsonDocument` verfuegbar sein, ohne erzwungene Deserialisierung.
- Umschaltbar ueber Konfiguration (`spocr.json`) und zur Laufzeit (`AppDbContextOptions` bzw. Pipe).
- Generatoren bleiben deterministisch: Bei identischer Konfiguration identischer Output.

## Architekturvorschlag

### 1. Konfigurierbare Materialisierungs-Strategie

Neue Einstellung in `spocr.json` (Arbeitsname `jsonMaterialization`):

```jsonc
{
  "project": {
    "output": {
      "dataContext": {
        "jsonMaterialization": "Deserialize" // Optionen: "Deserialize", "Raw", "Hybrid"
      }
    }
  }
}
```

- `Deserialize` (Default): heutiges Verhalten bleibt erhalten.
- `Raw`: Generator erzeugt Methoden mit `Task<string>` (oder `Task<JsonDocument>`). Kein Model-Output; Konsument nutzt JSON direkt.
- `Hybrid`: Generator liefert beide Varianten (z.B. `Task<string> ExecuteFooRawAsync(...)` plus `Task<Foo> ExecuteFooAsync(...)`).

### 2. Runtime-Schalter im DataContext

`AppDbContextOptions` wird um `JsonMaterializationMode` erweitert (gleiches Enum wie Konfiguration). Pipe erhaelt entsprechende Property plus Fluent-API:

```csharp
public enum JsonMaterializationMode { Deserialize, Raw }

public class AppDbContextOptions
{
    public int CommandTimeout { get; set; } = 30;
    public JsonMaterializationMode JsonMaterializationMode { get; set; } = JsonMaterializationMode.Deserialize;
}

public interface IAppDbContextPipe
{
    JsonMaterializationMode? JsonMaterializationOverride { get; set; }
}

public static IAppDbContextPipe WithJsonMaterialization(this IAppDbContext context, JsonMaterializationMode mode)
    => context.CreatePipe().WithJsonMaterialization(mode);
```

`AppDbContextPipeExtensions.ReadJsonAsync` entscheidet anhand von `pipe.JsonMaterializationOverride ?? pipe.Context.Options.JsonMaterializationMode`, ob deserialisiert wird oder ein Roh-Ergebnis zurueckkommt.

### 3. API-Form der Rueckgabe

- Modus `Deserialize`: unveraendert `Task<T>`.
- Modus `Raw`: Generator ersetzt `ReadJsonAsync<T>` durch `ReadJsonRawAsync` und der Methodentyp wird `Task<string>`.
- Modus `Hybrid`: Generator erzeugt beide Methoden (typed und raw). Benennungsvorschlag `ExecuteFooAsync` (typed) und `ExecuteFooRawAsync` (raw).

`ReadJsonAsync<T>` prueft den Modus und wirft bei aktivem `Raw` eine `InvalidOperationException` mit Hinweis, stattdessen `ReadJsonRawAsync` zu verwenden. So werden Fehlkonfigurationen frueh erkannt.

### 4. Generator-Anpassungen

- `StoredProcedureGenerator` liest `jsonMaterialization` und setzt `returnType` plus `returnExpression` entsprechend.
- Bei `Hybrid` nutzt der Generator ein Template-Duplikat fuer die Roh-Variante, Parameter- und Pipe-Setup teile sich Code.
- Modelle werden nur generiert, wenn sie gebraucht werden (siehe Punkt 5).

### 5. Auswirkungen auf Output-Modelle

- Modus `Raw`: JSON-Modelle optional. Konfigschalter `generateJsonModels` kann mit `jsonMaterialization` verknuepft werden (Default `true`, aber bei `Raw` optional).
- Modus `Hybrid`: Modelle bleiben bestehen, typed Methode greift darauf zu.

### 6. Migration und Kompatibilitaet

- Default bleibt `Deserialize`, bestehende Projekte erhalten identischen Output.
- `AppDbContextOptions` haelt den Defaultwert, damit bestehende `IOptions`-Konfigurationen weiterhin laufen.
- Aufrufer koennen zur Laufzeit `context.WithJsonMaterialization(JsonMaterializationMode.Raw)` setzen (z.B. in einem Web-Endpunkt).

### 7. Prototyp-Schritte

1. Enum und Option in `AppDbContextOptions` sowie der Pipe implementieren.
2. Neue Methode `ReadJsonRawAsync` erstellen; `ReadJsonAsync<T>` erweitert um Modus-Check.
3. Generator so erweitern, dass Rueckgabetyp und Aufruf je nach Modus variieren.
4. Templates fuer StoredProcedure-Extensions um Raw-Derivate erweitern (nur fuer Modus `Hybrid`).
5. `spocr.json`-Schema (Validation) und CLI-Helptexte ergaenzen.

### 8. Test- & Performance-Strategie

- Unit Tests fuer `ReadJsonAsync<T>` und `ReadJsonRawAsync` mit allen Modus-Kombinationen.
- Integrationstest mit Sample-Prozeduren (`samples.OrderListAsJson`, `samples.UserOrderHierarchyJson`) fuer typed vs. raw.
- Generator-Snapshots (z.B. Verify) je Modus zur Sicherung der Ausgabe.
- Performance-Messung via BenchmarkDotNet: Vergleich `Deserialize` vs. `Raw` mit grossem JSON (z.B. 10k Elemente) auf CPU-/Allocation-Basis.

### 9. Offene Fragen / Entscheidungsbedarf

- Rohformat als `string` oder `JsonDocument`? Vorschlag: zunaechst `string`, spaeter optional `JsonDocument` via Parameter.
- Kombination mit `Hybrid`: Soll der Generator beide Methoden immer generieren oder nur per Flag? Aktuell Vorschlag: nur wenn Konfig explizit `Hybrid` setzt.

## Zusammenfassung

Mit `JsonMaterializationMode` entsteht eine konfigurierbare Pipeline: Standard bleibt die automatische Deserialisierung, optional kann JSON roh zurueckgegeben oder parallel angeboten werden. Generator und Runtime-API ziehen an einem Strang, Konsumenten koennen je Anwendungsfall entscheiden und vermeiden ueberfluessige Deserialisierung inklusive Performance-Overhead.
