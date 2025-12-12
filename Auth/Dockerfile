# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Auth/src/Auth.Domain/Auth.Domain.csproj Auth/src/Auth.Domain/
COPY Auth/src/Auth.Application/Auth.Application.csproj Auth/src/Auth.Application/
COPY Auth/src/Auth.Infrastructure/Auth.Infrastructure.csproj Auth/src/Auth.Infrastructure/
COPY Auth/src/Auth.Api/Auth.Api.csproj Auth/src/Auth.Api/
COPY Observability/src/Observability/Observability.csproj Observability/src/Observability/
RUN dotnet restore Auth/src/Auth.Api/Auth.Api.csproj
COPY Auth/src/ Auth/src/
COPY Observability/src/ Observability/src/
RUN dotnet publish Auth/src/Auth.Api/Auth.Api.csproj -c ${BUILD_CONFIGURATION} -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","Auth.Api.dll"]
