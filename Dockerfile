# ========================================================
# Production Dockerfile for JY Career Agent (.NET 10 API)
# ========================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["backend/JYCareerAgent.Api.csproj", "backend/"]
RUN dotnet restore "backend/JYCareerAgent.Api.csproj"
COPY . .
WORKDIR "/src/backend"
RUN dotnet publish "JYCareerAgent.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .
COPY data ./data
COPY cv ./cv
COPY prompts ./prompts

ENTRYPOINT ["dotnet", "JYCareerAgent.Api.dll"]
