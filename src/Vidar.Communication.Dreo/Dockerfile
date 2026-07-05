FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Dreo/Vidar.Communication.Dreo.csproj src/Vidar.Communication.Dreo/
RUN dotnet restore src/Vidar.Communication.Dreo/Vidar.Communication.Dreo.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Dreo/ src/Vidar.Communication.Dreo/
RUN dotnet publish src/Vidar.Communication.Dreo/Vidar.Communication.Dreo.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Dreo.dll"]
