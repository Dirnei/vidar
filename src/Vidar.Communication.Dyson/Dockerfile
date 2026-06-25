FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Dyson/Vidar.Communication.Dyson.csproj src/Vidar.Communication.Dyson/
RUN dotnet restore src/Vidar.Communication.Dyson/Vidar.Communication.Dyson.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Dyson/ src/Vidar.Communication.Dyson/
RUN dotnet publish src/Vidar.Communication.Dyson/Vidar.Communication.Dyson.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Dyson.dll"]
