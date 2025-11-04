# DbBridge ASP.NET Core

Bridge per eseguire query SQL da PHP a MSSQL via HTTP.

## Setup

1. Imposta variabili ambiente:

```cmd
setx SQL_CONN "Server=HOST;Database=DB;User Id=USER;Password=PASS;TrustServerCertificate=True"
setx BRIDGE_SECRET "CHANGE_ME"   # opzionale, se si testa da fuori localhost
```

2. Publish:

```cmd
dotnet publish ./DbBridge -c Release -o ./DbBridge/publish
```

3. Run:

```cmd
./DbBridge/publish/DbBridge.exe
```

Ascolta su `http://127.0.0.1:5001`

## Endpoints

- GET /health → { "ok": true }
- POST /db/exec → esegui singola query
- POST /db/execBatch → esegui batch di query in transazione

## Sicurezza

- Solo localhost senza secret
- Header X-Bridge-Secret per accesso remoto
