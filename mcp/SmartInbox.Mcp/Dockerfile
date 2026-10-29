# Build from the repository root:  docker build -f mcp/SmartInbox.Mcp/Dockerfile -t smart-inbox-mcp .
# Runs the shared HTTP mode; each client sends its own "Authorization: Bearer sik_...".
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY sdk/dotnet/ sdk/dotnet/
COPY mcp/SmartInbox.Mcp/ mcp/SmartInbox.Mcp/
RUN dotnet publish mcp/SmartInbox.Mcp/SmartInbox.Mcp.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV SMARTINBOX_MCP_PORT=8090
EXPOSE 8090
ENTRYPOINT ["dotnet", "SmartInbox.Mcp.dll", "--http"]
