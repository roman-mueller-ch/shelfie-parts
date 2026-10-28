# Shelfie capture API. Build from the repo root (needs vision/ for prompt and schema):
#   docker build -f capture-api/Dockerfile .
#
# Cross-compiles on the build machine's architecture for each target platform,
# so multi-arch builds (amd64 for the VM, arm64 for a Pi) need no emulation.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY capture-api/CaptureApi.csproj capture-api/
RUN dotnet restore capture-api/CaptureApi.csproj -a $TARGETARCH
COPY vision/prompt.md vision/schema.json vision/
COPY capture-api/ capture-api/
RUN dotnet publish capture-api/CaptureApi.csproj -c Release -a $TARGETARCH --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The base image runs as the non-root "app" user and listens on 8080.
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "CaptureApi.dll"]
