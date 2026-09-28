FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MediaRelay.sln ./
COPY src/MediaRelay/MediaRelay.csproj src/MediaRelay/
RUN dotnet restore src/MediaRelay/MediaRelay.csproj
COPY src/MediaRelay/ src/MediaRelay/
RUN dotnet publish src/MediaRelay/MediaRelay.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MediaRelay.dll"]
