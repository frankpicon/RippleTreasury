FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/Services/Notifications/Notifications.Worker/Notifications.Worker.csproj
RUN dotnet publish src/Services/Notifications/Notifications.Worker/Notifications.Worker.csproj \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Notifications.Worker.dll"]
