# 1. 빌드 환경 (.NET 8.0 SDK)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 프로젝트 파일 복사 및 패키지 복원
COPY ["GameServer.csproj", "./"]
RUN dotnet restore "GameServer.csproj"

# 전체 소스 복사 및 릴리즈 빌드
COPY . .
RUN dotnet publish "GameServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. 실행 환경 (.NET 8.0 Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# .NET 8 기본 포트인 8080 개방
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# 서버 실행
ENTRYPOINT ["dotnet", "GameServer.dll"]