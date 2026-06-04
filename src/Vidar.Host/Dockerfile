FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Host/Vidar.Host.csproj src/Vidar.Host/
RUN dotnet restore src/Vidar.Host/Vidar.Host.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Host/ src/Vidar.Host/
RUN dotnet publish src/Vidar.Host/Vidar.Host.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080 4053
ENTRYPOINT ["dotnet", "Vidar.Host.dll"]
