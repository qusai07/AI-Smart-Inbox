# Build from the repository root:  docker build -f sdk/samples/WebhookReceiver/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY sdk/dotnet/ sdk/dotnet/
COPY sdk/samples/WebhookReceiver/ sdk/samples/WebhookReceiver/
RUN dotnet publish sdk/samples/WebhookReceiver/WebhookReceiver.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 5090
ENTRYPOINT ["dotnet", "WebhookReceiver.dll"]
