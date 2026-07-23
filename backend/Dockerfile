# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/PlayerFeedback.Core/PlayerFeedback.Core.csproj src/PlayerFeedback.Core/
COPY src/PlayerFeedback.Api/PlayerFeedback.Api.csproj src/PlayerFeedback.Api/
RUN dotnet restore src/PlayerFeedback.Api/PlayerFeedback.Api.csproj

COPY . .
RUN dotnet publish src/PlayerFeedback.Api/PlayerFeedback.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
USER $APP_UID
ENTRYPOINT ["dotnet", "PlayerFeedback.Api.dll"]
