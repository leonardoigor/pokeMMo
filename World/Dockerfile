FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY World/src/World.Server/World.Server.csproj World/src/World.Server/
COPY Observability/src/Observability/Observability.csproj Observability/src/Observability/
RUN dotnet restore World/src/World.Server/World.Server.csproj
COPY World/src/ World/src/
COPY Observability/src/ Observability/src/
RUN dotnet publish World/src/World.Server/World.Server.csproj -c ${BUILD_CONFIGURATION} -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
COPY World/data/ /app/data/
ENV ASPNETCORE_URLS=http://+:8082
EXPOSE 8082
EXPOSE 9090
ENTRYPOINT ["dotnet","World.Server.dll"]
