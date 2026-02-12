FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy NuGet.config first to disable Windows fallback folders
COPY ["NuGet.config", "./"]

# Copy solution and project files for better layer caching
COPY ["Directory.Build.props", "./"]
COPY ["src/Gateway.Core/Gateway.Core.csproj", "src/Gateway.Core/"]
COPY ["src/Gateway.Ui/Gateway.Ui.csproj", "src/Gateway.Ui/"]

# Restore dependencies for Gateway.Ui project (which depends on Gateway.Core)
# Clear NuGet cache and disable fallback folders to avoid Windows path issues
RUN dotnet nuget locals all --clear && \
    dotnet restore "src/Gateway.Ui/Gateway.Ui.csproj" --verbosity quiet

# Copy remaining source files (excluding obj and bin folders to avoid Windows path issues)
COPY . .
# Remove any obj/bin folders that might have been copied from Windows
RUN find /src -type d -name "obj" -o -name "bin" | grep -v "/app/" | xargs rm -rf || true

# Build the project
WORKDIR "/src/src/Gateway.Ui"
# Remove obj folder to clear any cached Windows paths, then build (which will restore if needed)
RUN rm -rf obj bin && \
    dotnet build "Gateway.Ui.csproj" -c $BUILD_CONFIGURATION -o /app/build /p:UseAppHost=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/src/Gateway.Ui"
# Remove obj folder to clear any cached Windows paths, then publish
RUN rm -rf obj bin && \
    dotnet publish "Gateway.Ui.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Ui.dll"]

