# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY World/src/World.Server/World.Server.csproj World/src/World.Server/
RUN dotnet restore World/src/World.Server/World.Server.csproj
COPY World/src/ World/src/
RUN dotnet publish World/src/World.Server/World.Server.csproj -c ${BUILD_CONFIGURATION} -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8082
EXPOSE 8082
EXPOSE 9090
ENTRYPOINT ["dotnet","World.Server.dll"]

