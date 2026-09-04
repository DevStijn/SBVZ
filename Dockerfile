FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /source

COPY global.json Directory.Build.props .editorconfig ./
COPY src/Sbvz.Api/Sbvz.Api.csproj src/Sbvz.Api/
RUN dotnet restore src/Sbvz.Api/Sbvz.Api.csproj

COPY src/Sbvz.Api/ src/Sbvz.Api/
RUN dotnet publish src/Sbvz.Api/Sbvz.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .

USER $APP_UID
ENTRYPOINT ["dotnet", "Sbvz.Api.dll"]
