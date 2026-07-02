# ===== 빌드 단계 =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# csproj만 먼저 복사 → restore (레이어 캐시로 빌드 빨라짐)
COPY ["WordApp/WordApp.csproj", "WordApp/"]
RUN dotnet restore "WordApp/WordApp.csproj"

# 전체 소스 복사 후 publish
COPY . .
RUN dotnet publish "WordApp/WordApp.csproj" -c Release -o /app/publish

# ===== 실행 단계 =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# 컨테이너 안에서 0.0.0.0으로 바인딩 (아까 배운 그거)
ENV ASPNETCORE_URLS=http://0.0.0.0:5276
EXPOSE 5276

ENTRYPOINT ["dotnet", "WordApp.dll"]