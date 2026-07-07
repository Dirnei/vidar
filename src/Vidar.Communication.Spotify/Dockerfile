FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Spotify/Vidar.Communication.Spotify.csproj src/Vidar.Communication.Spotify/
RUN dotnet restore src/Vidar.Communication.Spotify/Vidar.Communication.Spotify.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Spotify/ src/Vidar.Communication.Spotify/
RUN dotnet publish src/Vidar.Communication.Spotify/Vidar.Communication.Spotify.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Spotify.dll"]
