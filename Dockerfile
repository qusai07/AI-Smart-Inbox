# 1) Build the Vue app
FROM node:22-alpine AS web
WORKDIR /web
COPY src/smart-inbox-web/package.json src/smart-inbox-web/package-lock.json ./
RUN npm ci
COPY src/smart-inbox-web/ ./
RUN npm run build

# 2) Build the API
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY src/SmartInbox.Domain/SmartInbox.Domain.csproj src/SmartInbox.Domain/
COPY src/SmartInbox.Application/SmartInbox.Application.csproj src/SmartInbox.Application/
COPY src/SmartInbox.Infrastructure/SmartInbox.Infrastructure.csproj src/SmartInbox.Infrastructure/
COPY src/SmartInbox.Api/SmartInbox.Api.csproj src/SmartInbox.Api/
RUN dotnet restore src/SmartInbox.Api/SmartInbox.Api.csproj
COPY src/ src/
RUN dotnet publish src/SmartInbox.Api/SmartInbox.Api.csproj -c Release -o /app --no-restore

# 3) Runtime: one container serves the API, SignalR hub and the SPA
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api /app .
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__Root=/data/uploads     DataProtection__KeysPath=/data/keys
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartInbox.Api.dll"]
