# ─── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore dependencies (layer-cache friendly)
COPY ["src/AgentHost/AgentHost.csproj",               "src/AgentHost/"]
COPY ["src/AgentHost.Core/AgentHost.Core.csproj",     "src/AgentHost.Core/"]
COPY ["src/AgentHost.Llm/AgentHost.Llm.csproj",       "src/AgentHost.Llm/"]
COPY ["src/AgentHost.Indexing/AgentHost.Indexing.csproj",     "src/AgentHost.Indexing/"]
COPY ["src/AgentHost.Repositories/AgentHost.Repositories.csproj", "src/AgentHost.Repositories/"]
RUN dotnet restore "src/AgentHost/AgentHost.csproj"

# Copy all source and publish
COPY . .
WORKDIR "/src/src/AgentHost"
RUN dotnet publish "AgentHost.csproj" -c Release -o /app/publish --no-restore

# ─── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

EXPOSE 8080

# Run as non-root user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AgentHost.dll"]