# Dockerfile para Integration.Api
# Phase 1: compila y publica la aplicación ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Integration.Api/Integration.Api.csproj", "src/Integration.Api/"]
COPY ["src/Integration.Shared/Integration.Shared.csproj", "src/Integration.Shared/"]
RUN dotnet restore "src/Integration.Api/Integration.Api.csproj"
COPY . .
WORKDIR "/src/src/Integration.Api"
RUN dotnet build "Integration.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Integration.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "Integration.Api.dll"]
