# BattleCraft Remake. Custom Launcher

## Версии
- **Minecraft**: 1.20.1
- **Forge**: 47.4.20 (с автоматической очисткой старых версий)

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
- CmlLib.Core — запуск Minecraft
- CmlLib.Core.Installer.Forge — установка Forge
- Newtonsoft.Json — конфигурация

## Структура

```
├── App.xaml / App.xaml.cs        — точка входа
├── MainWindow.xaml               — UI
├── MainWindow.xaml.cs            — логика
└── Core/
    ├── AppSettings.cs            — конфигурация
    ├── FileDownloader.cs         — загрузка файлов
    └── ParticleBackground.cs     — анимированный фон
```

## Конфигурация

Настройки хранятся в `Документы/CustomLauncher/launcher_config.json`.

## Лицензия

MIT
