FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LinkService/LinkService.csproj src/LinkService/
RUN dotnet restore src/LinkService/LinkService.csproj
COPY src/LinkService/ src/LinkService/
RUN dotnet publish src/LinkService/LinkService.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LinkService.dll"]
