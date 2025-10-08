> Aktuelles samples/web-api/spocr.json:1 zeigt noch "TargetFramework": "net8.0". Du hattest erwähnt, auf net10.0 gestellt zu haben – ggf. ist das noch nicht gespeichert.

- [ ] In der Datei C:\Projekte\GitHub\spocr\samples\web-api\spocr.json steht: "TargetFramework": "net10.0" in Zeile 3, bitte verifizieren. Oder prüfen, wo das Problem liegt.

> Sollen wir spocr.json auf net10.0 umstellen und die Generation sofort auf den „modern mode“ migrieren?

- [ ] Das TargetFramework habe ich auf net10.0 gestellt. Bitte spocr im web-api Verzeichnis ausführen: dotnet run --project src/SpocR.csproj -- rebuild -p samples\web-api\spocr.json --no-auto-update --noc-cache

> Bevorzugter Namespace für den generierten Code? Sollen die Artefakte unter SpocR.Samples.WebApi.DataContext liegen?

- [ ] Ja, der Output-Namespace soll SpocR.Samples.WebApi.DataContext sein, wobei DataContext mit dem Order DataContext/ zusammentrifft.

> Darf der manuelle Kontext im Zuge der Umstellung vollständig entfernt werden, oder wünscht ihr eine Übergangsphase mit beiden Pfaden?

- [ ] Welcher manuelle Kontext?

> Sollen wir ein global.json (SDK 10.x) im Repo verankern?

- [ ] Erkläre bitte erst noch mal die Funktionsweise der global.json. Welche zusätzlichen Properties / Vorteile bringt diese mit sich?

> Dürfen wir das OpenAPI‑Paket auf eine stabile 10.x‑Version aktualisieren, oder sollen wir vorerst bei der RC bleiben?

- [ ] Ja, bitte auf die stabile Version (>=10.0) aktualisieren.

> Soll ich ein paar Beispiel‑Endpoints einbauen, die die generierten SP‑Wrapper nutzen, um die Integration zu demonstrieren?

- [ ] Ja, bitte ein paar Beispiel-Endpoints einbauen.
- [ ] Weitere Stored Procedures erstellen, gerne eine Story aus Tabellen, UDT und UDTT und Stored Procedures bauen. Mit unterschiedlichen Use-Cases, Kombinationen von Parametern, Rückgabetypen, Multiple Result Sets, etc.

> Für net10 wäre der Template-Ordner „Output-modern“ vorgesehen; er ist nicht vorhanden. Für net8/net9 existieren „Output-v9-0“/„Output-v5-0“. Der Fehler trat schon mit net8‑Konfig auf, also eher ein Konfig-/Nullproblem als ein Templateproblem.

- [ ] Die Templates seit v10 sollen von hier aus entstehen: C:\Projekte\GitHub\spocr\src\CodeGenerators\Templates\ITemplateEngine.cs also kein Output Ordner mehr (Bitte Kommentare/Dokumentation anpassen, wenn das nicht klar erkenntlich war)

- [ ] Existiert in docs/content ein Abschnitt zu Empfehlungen der .gitignore?
