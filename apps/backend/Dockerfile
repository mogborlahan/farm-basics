FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY apps/backend/src/FarmBasics.BuildingBlocks/FarmBasics.BuildingBlocks.csproj apps/backend/src/FarmBasics.BuildingBlocks/
COPY apps/backend/src/FarmBasics.Api/FarmBasics.Api.csproj apps/backend/src/FarmBasics.Api/
RUN dotnet restore apps/backend/src/FarmBasics.Api/FarmBasics.Api.csproj

COPY apps/backend/src/ apps/backend/src/
RUN dotnet publish apps/backend/src/FarmBasics.Api/FarmBasics.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system appuser \
    && useradd --system --gid appuser --no-create-home appuser
COPY --from=build /app/publish .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=15s --retries=3 \
  CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "FarmBasics.Api.dll"]
