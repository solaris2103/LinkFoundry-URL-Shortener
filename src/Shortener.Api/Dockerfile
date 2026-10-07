FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/Shortener.Core/Shortener.Core.csproj", "src/Shortener.Core/"]
COPY ["src/Shortener.Infrastructure/Shortener.Infrastructure.csproj", "src/Shortener.Infrastructure/"]
COPY ["src/Shortener.Api/Shortener.Api.csproj", "src/Shortener.Api/"]
RUN dotnet restore "src/Shortener.Api/Shortener.Api.csproj"

COPY . .
WORKDIR "/src/src/Shortener.Api"
RUN dotnet publish "Shortener.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 10000
ENV ASPNETCORE_URLS=http://+:10000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Shortener.Api.dll"]
