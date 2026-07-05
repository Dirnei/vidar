FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Vidar.Core/Vidar.Core.csproj src/Vidar.Core/
COPY src/Vidar.Communication.Bambu/Vidar.Communication.Bambu.csproj src/Vidar.Communication.Bambu/
RUN dotnet restore src/Vidar.Communication.Bambu/Vidar.Communication.Bambu.csproj
COPY src/Vidar.Core/ src/Vidar.Core/
COPY src/Vidar.Communication.Bambu/ src/Vidar.Communication.Bambu/
RUN dotnet publish src/Vidar.Communication.Bambu/Vidar.Communication.Bambu.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Vidar.Communication.Bambu.dll"]
