# Restapi Development

```bash
dotnet run --project src/SpocR.csproj -- rebuild  -p samples/restapi/spocr.json --no-auto-update
```

---

## Nullable Reference Types – Stepwise Escalation (Phase 1)

Global `<Nullable>enable</Nullable>` ist aktiv. Eskalation erfolgt in kontrollierten Phasen, um Warnungsrauschen gering zu halten.

Phasenplan:

1. Baseline (aktuell): Alle Nullable-Warnungen bleiben Warnungen. Opportunistisches Fixing.
2. Fokus-Warnungen (lokal oder CI opt-in):
   - CS8602 Dereference of a possibly null reference
   - CS8603 Possible null reference return
3. Erweiterung: Weitere relevante Warnungen (z.B. CS8618) nach Reduktion der Hotspots.
4. CI Hard Gate: `SPOCR_STRICT_NULLABLE=1` dauerhaft setzen.

Lokaler Test (temporär, nicht committen):

```ini
# .editorconfig (lokal)
[*.cs]
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8603.severity = error
```

CI Eskalation Beispiel:

```powershell
# Windows lokal (optional)
setx SPOCR_STRICT_NULLABLE 1
```

```yaml
# GitHub Actions Beispiel
env:
	SPOCR_STRICT_NULLABLE: 1
```

Rationale:

- Minimiert Big-Bang Refactor.
- Frühzeitige Absicherung vor echten Null-Deref Bugs.
- Klare, dokumentierte Eskalationsleiter.

Aufräumhinweis: Vor Commits lokale experimentelle .editorconfig Anpassungen entfernen.
