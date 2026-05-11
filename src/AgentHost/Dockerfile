FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/AgentHost/AgentHost.csproj", "src/AgentHost/"]
RUN dotnet restore "src/AgentHost/AgentHost.csproj"
COPY . .
WORKDIR "/src/src/AgentHost"
RUN dotnet build "AgentHost.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AgentHost.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AgentHost.dll"]
