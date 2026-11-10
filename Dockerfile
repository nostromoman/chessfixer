# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ src/
# Publishing the server also publishes the Blazor WebAssembly client into its wwwroot.
RUN dotnet publish src/ChessFixer.Server/ChessFixer.Server.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
# SQLite does not create missing folders; the volume mount normally covers this, but not without one.
RUN mkdir -p /app/App_Data

ARG GIT_SHA=dev
ENV APP_VERSION=$GIT_SHA
ENV ASPNETCORE_HTTP_PORTS=8080
# Everything that must survive a container update lives in App_Data: mount it as a volume.
ENV ConnectionStrings__ChessFixer="Data Source=/app/App_Data/chessfixer.db"
ENV DataProtection__KeysPath=/app/App_Data/keys
EXPOSE 8080

ENTRYPOINT ["dotnet", "ChessFixer.Server.dll"]
