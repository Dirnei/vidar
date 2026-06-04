FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Shelly/Vidar.Communication.Shelly.csproj src/Vidar.Communication.Shelly/
RUN dotnet restore src/Vidar.Communication.Shelly/Vidar.Communication.Shelly.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Shelly/ src/Vidar.Communication.Shelly/
RUN dotnet publish src/Vidar.Communication.Shelly/Vidar.Communication.Shelly.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Shelly.dll"]
