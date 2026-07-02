FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Ecowitt/Vidar.Communication.Ecowitt.csproj src/Vidar.Communication.Ecowitt/
RUN dotnet restore src/Vidar.Communication.Ecowitt/Vidar.Communication.Ecowitt.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Ecowitt/ src/Vidar.Communication.Ecowitt/
RUN dotnet publish src/Vidar.Communication.Ecowitt/Vidar.Communication.Ecowitt.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Ecowitt.dll"]
