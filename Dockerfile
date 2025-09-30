# Render용 Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# 🔥 한국 시간대 설정 추가
ENV TZ=Asia/Seoul
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# FFmpeg 설치
RUN apt-get update && apt-get install -y \
    ffmpeg \
    tzdata \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["YouTubeShortsWebApp.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet build "YouTubeShortsWebApp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "YouTubeShortsWebApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 음악 폴더 생성 및 권한 설정
RUN mkdir -p /app/music
RUN chmod 755 /app/music

# 환경 변수 설정
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV TZ=Asia/Seoul

ENTRYPOINT ["dotnet", "YouTubeShortsWebApp.dll"]
