FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY FrislEams.Web/FrislEams.Web.csproj FrislEams.Web/
RUN dotnet restore FrislEams.Web/FrislEams.Web.csproj

COPY FrislEams.Web/ FrislEams.Web/
RUN dotnet publish FrislEams.Web/FrislEams.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .
EXPOSE 10000

ENTRYPOINT ["dotnet", "FrislEams.Web.dll"]
