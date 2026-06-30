FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Roborock/Vidar.Communication.Roborock.csproj src/Vidar.Communication.Roborock/
RUN dotnet restore src/Vidar.Communication.Roborock/Vidar.Communication.Roborock.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Roborock/ src/Vidar.Communication.Roborock/
RUN dotnet publish src/Vidar.Communication.Roborock/Vidar.Communication.Roborock.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Roborock.dll"]
