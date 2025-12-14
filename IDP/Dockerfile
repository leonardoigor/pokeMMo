# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY IDP/src/IDP.Domain/IDP.Domain.csproj IDP/src/IDP.Domain/
COPY IDP/src/IDP.Application/IDP.Application.csproj IDP/src/IDP.Application/
COPY IDP/src/IDP.Infrastructure/IDP.Infrastructure.csproj IDP/src/IDP.Infrastructure/
COPY IDP/src/IDP.Api/IDP.Api.csproj IDP/src/IDP.Api/
COPY Observability/src/Observability/Observability.csproj Observability/src/Observability/
RUN dotnet restore IDP/src/IDP.Api/IDP.Api.csproj
COPY IDP/src/ IDP/src/
COPY Observability/src/ Observability/src/
RUN dotnet publish IDP/src/IDP.Api/IDP.Api.csproj -c ${BUILD_CONFIGURATION} -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
ENTRYPOINT ["dotnet","IDP.Api.dll"]
