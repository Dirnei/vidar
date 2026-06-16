FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.HomeConnect/Vidar.Communication.HomeConnect.csproj src/Vidar.Communication.HomeConnect/
RUN dotnet restore src/Vidar.Communication.HomeConnect/Vidar.Communication.HomeConnect.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.HomeConnect/ src/Vidar.Communication.HomeConnect/
RUN dotnet publish src/Vidar.Communication.HomeConnect/Vidar.Communication.HomeConnect.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.HomeConnect.dll"]
