# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

# Debugging: Struktur anzeigen
RUN find . -maxdepth 3 -type f

# Restore
RUN dotnet restore ./ApiApp/ApiApp.fsproj

# Publish mit voller Ausgabe
RUN dotnet publish ./ApiApp/ApiApp.fsproj \
    -c Release \
    -o /app/publish \
    -v normal

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ApiApp.dll"]
