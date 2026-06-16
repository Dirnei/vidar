FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.E3dc/Vidar.Communication.E3dc.csproj src/Vidar.Communication.E3dc/
RUN dotnet restore src/Vidar.Communication.E3dc/Vidar.Communication.E3dc.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.E3dc/ src/Vidar.Communication.E3dc/
RUN dotnet publish src/Vidar.Communication.E3dc/Vidar.Communication.E3dc.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.E3dc.dll"]
