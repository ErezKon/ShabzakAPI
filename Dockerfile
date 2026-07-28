#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["04.ShabzakAPI/04.ShabzakAPI.csproj", "04.ShabzakAPI/"]
COPY ["BL/03.BL.csproj", "BL/"]
COPY ["DataLayer/01.DataLayer.csproj", "DataLayer/"]
COPY ["Translators/02.Translators.csproj", "Translators/"]
RUN dotnet restore "./04.ShabzakAPI/04.ShabzakAPI.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "/src/04.ShabzakAPI/04.ShabzakAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "/src/04.ShabzakAPI/04.ShabzakAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER app
WORKDIR /app
COPY --from=publish /app/publish .
# Copy the entrypoint script that auto-generates Jwt__Key if not provided.
COPY entrypoint.sh /app/entrypoint.sh
USER root
RUN chmod +x /app/entrypoint.sh
USER app
ENTRYPOINT ["/app/entrypoint.sh"]