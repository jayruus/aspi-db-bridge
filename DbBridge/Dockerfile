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
