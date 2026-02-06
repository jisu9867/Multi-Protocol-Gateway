FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files for better layer caching
COPY ["Directory.Build.props", "./"]
COPY ["src/Gateway.Core/Gateway.Core.csproj", "src/Gateway.Core/"]
COPY ["src/Gateway.Infrastructure/Gateway.Infrastructure.csproj", "src/Gateway.Infrastructure/"]
COPY ["src/Gateway.Adapters/Gateway.Adapters.csproj", "src/Gateway.Adapters/"]
COPY ["src/Gateway.Api/Gateway.Api.csproj", "src/Gateway.Api/"]

# Restore dependencies for Gateway.Api project (which will restore all dependencies transitively)
RUN dotnet restore "src/Gateway.Api/Gateway.Api.csproj" --verbosity quiet

# Copy remaining source files
COPY . .

# Build the project
WORKDIR "/src/src/Gateway.Api"
RUN dotnet build "Gateway.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore /p:UseAppHost=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/src/Gateway.Api"
RUN dotnet publish "Gateway.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Api.dll"]

