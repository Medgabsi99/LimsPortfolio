# =============================================================================
# Lims.RestApi - multi-stage build (Linux)
#   docker build -t lims-api -f src/Lims.RestApi/Dockerfile .
#   docker run -p 8080:8080 \
#     -e ConnectionStrings__LimsDb="Server=host.docker.internal;Database=LimsDb;..." \
#     -e Jwt__SigningKey="<32+ chars secret>" \
#     lims-api
# The SQL Server database stays external (LocalDB / SQL Server container);
# provision it with database/run-all.ps1 or sqlcmd before starting the API.
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore first for layer caching
COPY ["LimsPortfolio.sln", "./"]
COPY ["src/Lims.Core/Lims.Core.csproj", "src/Lims.Core/"]
COPY ["src/Lims.Infrastructure/Lims.Infrastructure.csproj", "src/Lims.Infrastructure/"]
COPY ["src/Lims.RestApi/Lims.RestApi.csproj", "src/Lims.RestApi/"]
COPY ["src/Lims.SoapService/Lims.SoapService.csproj", "src/Lims.SoapService/"]
COPY ["src/Lims.WindowsService/Lims.WindowsService.csproj", "src/Lims.WindowsService/"]
RUN dotnet restore "src/Lims.RestApi/Lims.RestApi.csproj"

COPY . .
RUN dotnet publish "src/Lims.RestApi/Lims.RestApi.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# ASPNETCORE_HTTP_PORTS replaces the old ASPNETCORE_URLS mechanism
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Lims.RestApi.dll"]
