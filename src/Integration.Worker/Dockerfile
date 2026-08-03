# Dockerfile para Integration.Worker
# Phase 1: compila y publica el Worker Service
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Integration.Worker/Integration.Worker.csproj", "src/Integration.Worker/"]
COPY ["src/Integration.Shared/Integration.Shared.csproj", "src/Integration.Shared/"]
RUN dotnet restore "src/Integration.Worker/Integration.Worker.csproj"
COPY . .
WORKDIR "/src/src/Integration.Worker"
RUN dotnet build "Integration.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Integration.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Integration.Worker.dll"]
