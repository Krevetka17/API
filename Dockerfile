# Используем .NET SDK для сборки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Копируем файлы и собираем приложение
COPY . . 
RUN dotnet restore 
RUN dotnet publish -c Release -o /publish

# Используем ASP.NET Core Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /publish .

# Открываем порт
EXPOSE 8090
ENV ASPNETCORE_URLS=http://+:8090

# Запуск API
ENTRYPOINT ["dotnet", "ToDoListAPI.dll"]
