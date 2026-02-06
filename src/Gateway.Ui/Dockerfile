FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files for better layer caching
COPY ["Directory.Build.props", "./"]
COPY ["Gateway.sln", "./"]
COPY ["src/Gateway.Core/Gateway.Core.csproj", "src/Gateway.Core/"]
COPY ["src/Gateway.Ui/Gateway.Ui.csproj", "src/Gateway.Ui/"]

# Restore dependencies using solution file
RUN dotnet restore "Gateway.sln" --verbosity quiet

# Copy remaining source files
COPY . .

# Build the project
WORKDIR "/src/src/Gateway.Ui"
RUN dotnet build "Gateway.Ui.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore /p:UseAppHost=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/src/Gateway.Ui"
RUN dotnet publish "Gateway.Ui.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Ui.dll"]

