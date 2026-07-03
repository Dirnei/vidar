# --- Frontend build stage ---
# Builds the SPA so the image always ships the current UI instead of a stale committed
# wwwroot. Deps are installed first (from the lockfile) for layer caching.
FROM node:22-alpine AS frontend
WORKDIR /frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build -- --outDir dist --emptyOutDir

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Host/Vidar.Host.csproj src/Vidar.Host/
RUN dotnet restore src/Vidar.Host/Vidar.Host.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Host/ src/Vidar.Host/
# Replace the committed wwwroot with the freshly built frontend so publish ships current UI.
RUN rm -rf src/Vidar.Host/wwwroot
COPY --from=frontend /frontend/dist/ src/Vidar.Host/wwwroot/
RUN dotnet publish src/Vidar.Host/Vidar.Host.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080 4053
# Lets dependent workers wait for the host to be serving before they start (avoids a
# startup cluster race where a worker associates with a not-yet-ready host).
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=12 \
  CMD curl -fsS http://localhost:8080/ || exit 1
ENTRYPOINT ["dotnet", "Vidar.Host.dll"]
