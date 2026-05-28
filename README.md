# BattleCraft Remake. Custom Launcher

## Версии
- **Minecraft**: 1.20.1
- **Forge**: 47.4.20 (с автоматической очисткой старых версий)

## Возможности
- Установка и запуск Minecraft с Forge и модпаком
- Управление серверами: создание, запуск, остановка, перезапуск
- Встроенная консоль сервера (без внешнего CMD)
- Конфигурация server.properties из лаунчера
- Управление вайтлистом
- Восстановление мира из бэкапа
- Поддержка нескольких серверов
- Кастомизация темы (цвета, свечение, иконка)
- Автообновление лаунчера и модпака

## Сборка

```bash
dotnet build
```

## Публикация

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Стек

- .NET 8 / WPF
- CmlLib.Core - запуск Minecraft
- CmlLib.Core.Installer.Forge - установка Forge
- Newtonsoft.Json - конфигурация

## Структура

```
App.xaml / App.xaml.cs        - точка входа
MainWindow.xaml               - UI (сайдбар, Play, Server)
MainWindow.xaml.cs            - логика
Core/
    AppSettings.cs            - конфигурация лаунчера
    FileDownloader.cs         - загрузка файлов
    ServerConfig.cs           - модель конфигурации сервера
    ServerManager.cs          - управление процессом сервера
    ServerInstaller.cs        - установка и бэкап сервера
```

## Конфигурация

Настройки хранятся в `Документы/CustomLauncher/launcher_config.json`.

## Лицензия

MIT
