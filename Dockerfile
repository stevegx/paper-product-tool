FROM node:24-bookworm-slim AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-fund --no-audit
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NuGet.Config Directory.Build.props ./
COPY backend/Paper.Api/Paper.Api.csproj backend/Paper.Api/
RUN dotnet restore backend/Paper.Api/Paper.Api.csproj --configfile NuGet.Config
COPY backend/Paper.Api/ backend/Paper.Api/
COPY --from=frontend /src/backend/Paper.Api/wwwroot/ backend/Paper.Api/wwwroot/
RUN dotnet publish backend/Paper.Api/Paper.Api.csproj --no-restore -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
RUN mkdir -p /app/App_Data/keys && chown -R "$APP_UID" /app/App_Data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000 \
    AllowedHosts="*.onrender.com;localhost;127.0.0.1"
USER $APP_UID
EXPOSE 10000
CMD ["sh", "-c", "exec dotnet Paper.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
