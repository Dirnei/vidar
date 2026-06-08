FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.UniFi/Vidar.Communication.UniFi.csproj src/Vidar.Communication.UniFi/
RUN dotnet restore src/Vidar.Communication.UniFi/Vidar.Communication.UniFi.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.UniFi/ src/Vidar.Communication.UniFi/
RUN dotnet publish src/Vidar.Communication.UniFi/Vidar.Communication.UniFi.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.UniFi.dll"]
