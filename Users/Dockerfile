# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Users/src/Users.Domain/Users.Domain.csproj Users/src/Users.Domain/
COPY Users/src/Users.Application/Users.Application.csproj Users/src/Users.Application/
COPY Users/src/Users.Infrastructure/Users.Infrastructure.csproj Users/src/Users.Infrastructure/
COPY Users/src/Users.Api/Users.Api.csproj Users/src/Users.Api/
COPY Observability/src/Observability/Observability.csproj Observability/src/Observability/
RUN dotnet restore Users/src/Users.Api/Users.Api.csproj
COPY Users/src/ Users/src/
COPY Observability/src/ Observability/src/
RUN dotnet publish Users/src/Users.Api/Users.Api.csproj -c ${BUILD_CONFIGURATION} -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
ENTRYPOINT ["dotnet","Users.Api.dll"]
