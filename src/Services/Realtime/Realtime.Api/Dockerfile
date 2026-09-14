FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/Services/Realtime/Realtime.Api/Realtime.Api.csproj
RUN dotnet publish src/Services/Realtime/Realtime.Api/Realtime.Api.csproj \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Realtime.Api.dll"]
