# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

RUN find . -maxdepth 3 -type f

RUN dotnet restore ./WorkerApp/WorkerApp.fsproj

RUN dotnet publish ./WorkerApp/WorkerApp.fsproj \
    -c Release \
    -o /app/publish \
    -v normal

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "WorkerApp.dll"]
