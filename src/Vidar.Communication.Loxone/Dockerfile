FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Loxone/Vidar.Communication.Loxone.csproj src/Vidar.Communication.Loxone/
RUN dotnet restore src/Vidar.Communication.Loxone/Vidar.Communication.Loxone.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Loxone/ src/Vidar.Communication.Loxone/
RUN dotnet publish src/Vidar.Communication.Loxone/Vidar.Communication.Loxone.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Loxone.dll"]
