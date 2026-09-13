FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shortener.Core/Shortener.Core.csproj src/Shortener.Core/
COPY src/Shortener.Infrastructure/Shortener.Infrastructure.csproj src/Shortener.Infrastructure/
COPY src/Shortener.Api/Shortener.Api.csproj src/Shortener.Api/
RUN dotnet restore src/Shortener.Api/Shortener.Api.csproj
COPY src/ src/
RUN dotnet publish src/Shortener.Api/Shortener.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shortener.Api.dll"]
