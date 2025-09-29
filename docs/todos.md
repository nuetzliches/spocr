Anweisung für codex:
Halte dich an diese Todos, arbeite sie der Reihe nach ab.

Setze - [ ] für neue Todos
Setze - [x] für erledigte Todos

- [ ] passe amples\mssql\init an. trenne schema und tables, füge custom data type und custom table types ein
- [ ] verwende custom data types in den Tabellen
- [ ] weitere Test Prozeduren erstellen: Multiple Resultsets, nested Json Objekte, Inputs mit custom data type und custom table types.
- [ ] Tests implementieren: Auf Basis eines Docker Containers mit mssql DB die testbare StoredProcedures beinhaltet und zu einem soll-Output (sporc.json) führen soll? Daraus dann die Model-Generierung testen? Also mehrstufige Tests?
- [ ] `SpocR.DataContext.Queries.StoredProcedureQueries` hier wird für jede Methode die ObjectId neu aufgelöst. Ist es nicht sinnvoller, diese gleich über die `StoredProcedureListAsync` mit abzufragen (gilt das auch für die `definition` - enthält diese auch `inputs` und `outputs` - reicht also der Abruf einer angereicherten/vollständigen Liste)?
- [ ] Naming Conventions aufweichen/entfernen oder konfigurierbar gestalten: Möglichkeiten ausarbeiten.
- [ ] CRUD vs Result-Set Procedures: Wie unterscheiden wir diese? List vs Single? Bei JSON anhand WITHOUT_ARRAY_WRAPPER?
