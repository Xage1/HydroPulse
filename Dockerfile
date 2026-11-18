# --- Build Stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["HydroPulse.sln", "./"]
COPY ["src/HydroPulse.Api/HydroPulse.Api.csproj", "src/HydroPulse.Api/"]
COPY ["src/HydroPulse.Application/HydroPulse.Application.csproj", "src/HydroPulse.Application/"]
COPY ["src/HydroPulse.Domain/HydroPulse.Domain.csproj", "src/HydroPulse.Domain/"]
COPY ["src/HydroPulse.Infrastructure/HydroPulse.Infrastructure.csproj", "src/HydroPulse.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "src/HydroPulse.Api/HydroPulse.Api.csproj"

# Copy remaining source code
COPY . .
WORKDIR "/src/src/HydroPulse.Api"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime Stage ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HydroPulse.Api.dll"]