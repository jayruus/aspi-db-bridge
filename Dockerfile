# BUILD
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
WORKDIR /src/DbBridge
RUN dotnet publish -c Release -o /app

# RUNTIME
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
# Kestrel deve ascoltare su 0.0.0.0 in container
ENV ASPNETCORE_URLS=http://0.0.0.0:5001
# (Opzionale) variabili a runtime: SQL_CONN e BRIDGE_SECRET le passi con -e
EXPOSE 5001
ENTRYPOINT ["dotnet", "DbBridge.dll"]
