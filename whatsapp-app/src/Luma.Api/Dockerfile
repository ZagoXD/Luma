FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Luma.Api/Luma.Api.csproj", "src/Luma.Api/"]
RUN dotnet restore "src/Luma.Api/Luma.Api.csproj"
COPY . .
RUN dotnet publish "src/Luma.Api/Luma.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Luma.Api.dll"]
