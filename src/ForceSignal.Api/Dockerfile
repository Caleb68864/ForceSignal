FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ForceSignal.Api/ForceSignal.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
# The directory the match database lives on, created in the image and owned by the user the app runs
# as. It matters that this happens here rather than at runtime: docker seeds a fresh named volume
# from the image's directory, ownership included, so a volume mounted over a path that does not
# exist in the image is created owned by root - and the app, which is deliberately not root, then
# cannot create the file. That is exactly what happened: every compose stack ran on memory, said so
# only in a readiness warning nobody was reading, and lost every game at the next restart.
#
# An existing deployment whose volume was already created the old way needs a one-time
# `docker compose run --rm --user root api chown -R $APP_UID /var/lib/forcesignal`; a new one does not.
RUN mkdir -p /var/lib/forcesignal && chown -R $APP_UID:$APP_UID /var/lib/forcesignal
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD curl -fsS http://127.0.0.1:8080/ready || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "ForceSignal.Api.dll"]
