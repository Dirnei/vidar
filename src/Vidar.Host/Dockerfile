FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Host/Vidar.Host.csproj src/Vidar.Host/
RUN dotnet restore src/Vidar.Host/Vidar.Host.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Host/ src/Vidar.Host/
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
