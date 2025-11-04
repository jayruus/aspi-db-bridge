Cartella progetto ASP: aspi-asp
Cartella progetto PHP: aspi-api-php

Scenario

Le uniche query verso MSSQL sono in User.php e Evento.php (PHP).

Obiettivo: replicare fedelmente le query esistenti in un bridge ASP.NET Core .NET 8 che accetta queryKey + params e le esegue come CommandType.Text usando SqlParameter.

Niente stored procedure. Niente cambi al runtime PHP in questa fase. Tutto testabile via Postman su localhost.

Output richiesto (file e contenuti completi)
0) Albero finale (target)
bridge/
  manifest.md
  queries.manifest.json
  scan_report.md
DbBridge/
  Program.cs
  SqlTemplateStore.cs
  Models.cs
  LocalOnlyOrSecretMiddleware.cs
  appsettings.json
  SqlTemplates/
    <uno-per-ogni-query>.sql
  postman_collection.json
  README.md
  web.config

1) Estrazione query da User.php e Evento.php (ANALISI DIRETTA DEI FILE)

Cosa fare

Apri e leggi integralmente User.php e Evento.php.

Individua tutti i punti in cui esegui SQL tramite:

PDO: prepare, query, exec, bindParam/bindValue, execute.

mysqli: query, prepare, bind_param, execute, get_result.

sqlsrv_*: sqlsrv_query, sqlsrv_prepare, sqlsrv_execute (+ array $params).

Ricomponi SQL se è distribuito in più concatenazioni. Se ambiguo, fai best effort e logga WARNING.

Per ogni query trovata

Determina:

Tipo: SELECT | INSERT | UPDATE | DELETE.

Driver: pdo | mysqli | sqlsrv.

Stile parametri: named (:email) o positional (?).

Ordine parametri: dall’uso in bindParam/bindValue/bind_param/execute([...]).

Tabella principale (FROM / INSERT INTO / UPDATE / DELETE).

Multi-statement: se presente, lascialo invariato (eseguito come testo).

Genera una key stabile: Area_Action_suffix_hash6, dove:

Area = tabella principale (es. User, Evento).

Action = Get|List|Insert|Update|Delete + eventuale suffisso (ByEmail, Paged, ecc.).

suffix opzionale corto per distinguere metodi simili (es. ActiveOnly).

hash6 = substr(sha1(sql_normalized),0,6) dove sql_normalized sostituisce :name/? con @name/@pN.

Normalizza i placeholder:

Named :name → @name.

Posizionali ? → @p1,@p2,... nell’ordine di bind/execute.

Tipi parametri (heuristic, override editabile):
INT per interi, DECIMAL(18,4) per numerici decimali, NVARCHAR(MAX) per stringhe, DATETIME2 per date, BIT per booleani.

Warning rules (per scan_report.md):

SELECT senza TOP/OFFSET → “no limit”.

IN (?) con array → “array placeholder: riformulare in fase 2”.

Concatenazioni con variabili → “concat detected”.

Uso di lastInsertId()/equivalenti nel sorgente → segnala.

Manifest

bridge/queries.manifest.json:

{
  "generatedAt": "ISO-8601",
  "sourceFiles": ["User.php","Evento.php"],
  "total": <num>,
  "queries": [
    {
      "class": "User",
      "method": "getByEmail",
      "key": "User_GetByEmail_a13f2b",
      "driver": "pdo",
      "style": "named",
      "params": ["email"],
      "paramTypes": {"email":"NVARCHAR(256)"},
      "source": "User.php:123",
      "sql_original": "SELECT ... WHERE email = :email",
      "sql_normalized": "SELECT ... WHERE email = @email",
      "warnings": []
    }
  ]
}


bridge/manifest.md: tabella leggibile con colonne:

Classe::Metodo | key | tipo | driver | stile | params (con tipo) | note/warnings

bridge/scan_report.md: elenco file/linea + WARNING specifici.

Template SQL

Crea uno .sql per ogni query in DbBridge/SqlTemplates/<key>.sql:

Righe iniziali commentate:

-- Source: User.php:123  User::getByEmail
-- PARAMS: Email NVARCHAR(256)


Corpo SQL con @param.

Se l’originale usa lastInsertId() (o equivalente), appendi:

; SELECT SCOPE_IDENTITY() AS LastId;

2) Bridge ASP.NET Core (.NET 8) — localhost only, senza SP

Vincoli non negoziabili

Nessun SQL dal client. Solo queryKey + params.

Binding Kestrel solo su http://127.0.0.1:5000.

Middleware LocalOnlyOrSecretMiddleware:

consenti 127.0.0.1/::1;

altrimenti richiedi header X-Bridge-Secret == BRIDGE_SECRET (env);

in caso contrario, 401 JSON { "ok": false, "error": "AUTH_FAILED" }.

Endpoint

GET /health → { "ok": true }

POST /db/exec

Body:

{ "queryKey": "User_GetByEmail_a13f2b", "params": { "email": "foo@bar.com" } }


Passi:

SqlTemplateStore.TryGet(queryKey) → ottieni SQL.

SqlConnection(Environment.GetEnvironmentVariable("SQL_CONN"))

SqlCommand(sql, conn) con CommandType.Text, CommandTimeout=15.

Per ogni params, cmd.Parameters.Add(new SqlParameter("@"+name, value ?? DBNull.Value)).

Esegui:

Se c’è resultset → leggi solo il primo in rows: List<Dictionary<string,object?>>.

affected = cmd.ExecuteNonQuery() quando appropriato (se non leggi).

Rispondi:

{ "ok": true, "rows": [...], "affected": <int>, "durationMs": <int> }


POST /db/execBatch

Body:

{ "ops": [ { "queryKey": "...", "params": {...} }, ... ] }


Singola connessione + BeginTransaction(), esecuzione in ordine; rollback su errore; restituisci rowsByOp (array) o totalAffected.

File da generare

Program.cs (completo, niente TODO)

SqlTemplateStore.cs:

Carica tutti *.sql da appsettings.json:TemplatesPath (default SqlTemplates) in Dictionary<string,string>.

Models.cs:

public record ExecReq(string QueryKey, Dictionary<string, object?>? Params);
public record ExecBatchReq(List<ExecReq> Ops);


LocalOnlyOrSecretMiddleware.cs (come descritto)

appsettings.json:

{ "TemplatesPath": "SqlTemplates" }


web.config (IIS, framework-dependent, out-of-process).

README.md con comandi:

setx SQL_CONN "Server=HOST;Database=DB;User Id=USER;Password=PASS;TrustServerCertificate=True"
setx BRIDGE_SECRET "CHANGE_ME"   # opzionale, se si testa da fuori localhost
dotnet publish ./DbBridge -c Release -o ./DbBridge/publish
./DbBridge/publish/DbBridge.exe  # ascolta su http://127.0.0.1:5000

3) Postman collection — 1 request per query

DbBridge/postman_collection.json (v2.1):

Variables:
baseUrl = http://127.0.0.1:5000
bridgeSecret = "" (opzionale)

Requests:

Health → GET {{baseUrl}}/health

Una request per ogni query del manifest:

POST {{baseUrl}}/db/exec

Headers: Content-Type: application/json, opzionale X-Bridge-Secret: {{bridgeSecret}}

Body con queryKey esatto e un esempio coerente dei params (dummy ma del tipo giusto).

Batch Example con due op reali dal manifest.

4) Requisiti di qualità (accettazione)

Copertura: tutte le query presenti in User.php e Evento.php devono apparire nel manifest e avere un corrispondente file .sql.

Param mapping: i nomi e l’ordine dei parametri nei template devono corrispondere a quelli usati nel PHP (bind/execute).

Nessuna sostituzione testuale dei parametri nel SQL: solo SqlParameter.

Fedeltà semantica: non alterare la logica delle query; se c’è TOP, JOIN, ORDER BY, OUTPUT, deve rimanere identico.

Multi-statement: supportati così come sono; ritorna il primo resultset.

INSERT id: se il codice PHP usa lastInsertId()/equivalenti, aggiungi ; SELECT SCOPE_IDENTITY() AS LastId; nel template.

Log (server): logga queryKey, durata, rows/affected, errori (senza esporre stacktrace al client).

Sicurezza base: binding localhost + header segreto opzionale. Nessuna esposizione pubblica pre-hardening.

5) Edge case da gestire (e documentare in scan_report.md)

Query costruite via concatenazioni con variabili: ricomponi dove possibile; se ambiguo, WARNING + template separato.

Clause IN (?) con liste: WARNING “array/variadic non gestito in Fase 1”; lascia @Csv come placeholder testuale nel template e nota che in Fase 2 si farà espansione server-side sicura.

Resultset “vuoti”/only affected: gestisci entrambi (rows=[], affected>=0).

DateTime formati: usa DATETIME2 e lascia l’onere del formato al client durante il test Postman.

Unicode: usa tipi NVARCHAR.

6) Output atteso (mostra contenuto completo)

Alla fine stampa:

bridge/manifest.md completo (tabella).

bridge/queries.manifest.json completo.

Tutti i file DbBridge/SqlTemplates/*.sql.

DbBridge/Program.cs, SqlTemplateStore.cs, LocalOnlyOrSecretMiddleware.cs, Models.cs, appsettings.json, README.md, web.config.

DbBridge/postman_collection.json.

7) Esempio di risposta /db/exec (per Postman)

Request:

POST http://127.0.0.1:5000/db/exec
Content-Type: application/json

{
  "queryKey": "User_GetByEmail_a13f2b",
  "params": { "email": "foo@bar.com" }
}


Response:

{ "ok": true, "rows": [ { "UserId": 123, "Email": "foo@bar.com" } ], "affected": 1, "durationMs": 7 }

8) Non fare

Non accettare testo SQL dal client.

Non esporre il servizio oltre localhost.

Non trasformare query in stored procedure (non richieste).

Esegui ora questo piano: genera tutti i file sopra con contenuto completo, basandoti ESCLUSIVAMENTE sulle query effettive trovate in User.php e Evento.php.