FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/AnalyticsService/AnalyticsService.csproj src/AnalyticsService/
RUN dotnet restore src/AnalyticsService/AnalyticsService.csproj
COPY src/AnalyticsService/ src/AnalyticsService/
RUN dotnet publish src/AnalyticsService/AnalyticsService.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AnalyticsService.dll"]
