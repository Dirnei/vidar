FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Zigbee2Mqtt/Vidar.Communication.Zigbee2Mqtt.csproj src/Vidar.Communication.Zigbee2Mqtt/
RUN dotnet restore src/Vidar.Communication.Zigbee2Mqtt/Vidar.Communication.Zigbee2Mqtt.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Zigbee2Mqtt/ src/Vidar.Communication.Zigbee2Mqtt/
RUN dotnet publish src/Vidar.Communication.Zigbee2Mqtt/Vidar.Communication.Zigbee2Mqtt.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Zigbee2Mqtt.dll"]
