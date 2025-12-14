# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Directory/src/Directory.Api/Directory.Api.csproj Directory/src/Directory.Api/
COPY Observability/src/Observability/Observability.csproj Observability/src/Observability/
RUN dotnet restore Directory/src/Directory.Api/Directory.Api.csproj
COPY Directory/src/ Directory/src/
COPY Observability/src/ Observability/src/
RUN dotnet publish Directory/src/Directory.Api/Directory.Api.csproj -c ${BUILD_CONFIGURATION} -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8085
EXPOSE 8085
ENTRYPOINT ["dotnet","Directory.Api.dll"]
