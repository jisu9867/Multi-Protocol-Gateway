FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["src/Gateway.Core/Gateway.Core.csproj", "src/Gateway.Core/"]
COPY ["src/Gateway.Ui/Gateway.Ui.csproj", "src/Gateway.Ui/"]
RUN dotnet restore "src/Gateway.Ui/Gateway.Ui.csproj"
COPY . .
WORKDIR "/src/src/Gateway.Ui"
RUN dotnet build "Gateway.Ui.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Gateway.Ui.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Ui.dll"]

