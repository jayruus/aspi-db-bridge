Obiettivo
Sistemare il progetto DbBridge .NET 8 in modo che il container Docker parta senza FileLoadException.
Cause tipiche: mismatch tra publish e ENTRYPOINT, output copiato nel path sbagliato, Kestrel bind errato in container.

(A) Correzione STANDARD — Framework-dependent (consigliata)
1) DbBridge/DbBridge.csproj

Rimuovi <RuntimeIdentifier> se presente.

Imposta publish framework-dependent (niente RID, niente self-contained).

Sostituisci l’intero file con:

<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <SelfContained>false</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.0" />
  </ItemGroup>
</Project>

2) DbBridge/Program.cs

Rimuovi qualsiasi ListenLocalhost(...).

Lascia che Kestrel legga ASPNETCORE_URLS (lo settiamo nel Dockerfile).

Cose che devono rimanere: mapping endpoint, middleware locale/secret, ecc.
Cose da eliminare: blocchi che forzano ListenLocalhost(5001).

3) DbBridge/Dockerfile

Usa multi-stage; pubblica in /app; ENTRYPOINT su DbBridge.dll.

Copia anche SqlTemplates/.

Sostituisci l’intero file con:

# BUILD
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia solo csproj per cache
COPY DbBridge/DbBridge.csproj DbBridge/DbBridge.csproj
RUN dotnet restore DbBridge/DbBridge.csproj

# Copia resto sorgente
COPY . .

# Publish framework-dependent (no RID, no self-contained)
RUN dotnet publish DbBridge/DbBridge.csproj -c Release -o /app /p:UseAppHost=false

# RUNTIME
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=build /src/DbBridge/SqlTemplates ./SqlTemplates

# Ascolta su 0.0.0.0:5001 in container
ENV ASPNETCORE_URLS=http://0.0.0.0:5001
EXPOSE 5001

ENTRYPOINT ["dotnet", "DbBridge.dll"]

4) Comandi da eseguire (aggiungi alla risposta finale)
cd DbBridge

# build fresco (opzionale: pulisci cache)
# docker system prune -f

docker build -t db-bridge:local .

docker run --rm -it \
  -p 5001:5001 \
  -e SQL_CONN="Server=HOST,1433;Database=DB;User Id=USER;Password=PASS;TrustServerCertificate=True" \
  -e BRIDGE_SECRET="CHANGEME" \
  db-bridge:local

# test
curl -sS http://127.0.0.1:5001/health

(B) Alternativa — Self-contained (solo se vuoi un binario unico)
1) DbBridge/DbBridge.csproj (self-contained + RID)
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.0" />
  </ItemGroup>
</Project>

2) DbBridge/Dockerfile (ENTRYPOINT binario, non .dll)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY DbBridge/DbBridge.csproj DbBridge/DbBridge.csproj
RUN dotnet restore DbBridge/DbBridge.csproj
COPY . .
RUN dotnet publish DbBridge/DbBridge.csproj -c Release -o /app -r linux-x64 --self-contained true

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=build /src/DbBridge/SqlTemplates ./SqlTemplates
ENV ASPNETCORE_URLS=http://0.0.0.0:5001
EXPOSE 5001
ENTRYPOINT ["./DbBridge"]

3) Program.cs

Anche qui: niente ListenLocalhost. Usa ASPNETCORE_URLS.

(C) Sanity check che Copilot deve garantire

Nel container, esiste /app/DbBridge.dll (Opzione A) oppure /app/DbBridge (Opzione B).

L’ENTRYPOINT corrisponde al tipo di publish:

Framework-dependent → ["dotnet","DbBridge.dll"]

Self-contained → ["./DbBridge"]

ASPNETCORE_URLS = http://0.0.0.0:5001 e EXPOSE 5001.

Nessun ListenLocalhost in Program.cs.

curl http://127.0.0.1:5001/health risponde {"ok":true}.

(D) Spiega perché prima esplodeva (da includere nel commit message)

Publish/ENTRYPOINT incoerenti: pubblicavi un exe o in una cartella diversa, ma l’ENTRYPOINT cercava DbBridge.dll, causando FileLoadException.

Config Kestrel bloccata su localhost dentro container → anche se avesse avviato, non avrebbe esposto la porta correttamente.

Applica ora le modifiche, sovrascrivi i file indicati, ricostruisci l’immagine e avvia.