FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/AuthService/AuthService.csproj src/AuthService/
RUN dotnet restore src/AuthService/AuthService.csproj
COPY src/AuthService/ src/AuthService/
RUN dotnet publish src/AuthService/AuthService.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AuthService.dll"]
