FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY nuget.config .
COPY *.sln .
COPY HomeAutio.Mqtt.UnifiVideo/*.csproj ./HomeAutio.Mqtt.UnifiVideo/
RUN dotnet restore

# copy and build everything else
COPY HomeAutio.Mqtt.UnifiVideo/. ./HomeAutio.Mqtt.UnifiVideo/
RUN dotnet build -c Release

# publish
FROM build AS publish
WORKDIR /app/HomeAutio.Mqtt.UnifiVideo
RUN dotnet publish -o out

# build runtime image
FROM mcr.microsoft.com/dotnet/core/runtime:3.1-alpine AS runtime
WORKDIR /app
COPY --from=publish /app/HomeAutio.Mqtt.UnifiVideo/out ./

ENV ENVIRONMENT Production

VOLUME ["/app/appsettings.Production.json", "/app/logs"]
ENTRYPOINT ["dotnet", "HomeAutio.Mqtt.UnifiVideo.dll"]