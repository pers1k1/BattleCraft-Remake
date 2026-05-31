# BattleCraft Remake. Custom Launcher

## Версии
- **Minecraft**: 1.20.1
- **Forge**: 47.4.20
- **Launcher Version**: 6.5

## Особенности клиента
- Загрузка и запуск Minecraft и Forge в один клик
- Установка кастомного дизайна: иконки, фон, цвета
- Интеграция Discord Rich Presence с кнопкой GitHub
- Авторизация Microsoft без WebView2 (легковесная)
- Поддержка одиночной игры и мультиплеера
- Автообновление лаунчера и модпака
- Автоматическая очистка кэша Distant Horizons (удаление Distant_Horizons_server_data при запуске)

## Серверная часть
- Создание и управление несколькими Forge-серверами
- Установка Forge сервера и скачивание данных в один клик
- Настройка server.properties через GUI (MOTD, порт, дистанция, ОЗУ)
- Вайтлист с офлайн-UUID генерацией
- Встроенная консоль с вводом команд
- Восстановление мира из локального бэкапа
- Обновление серверных модов

## Сборка
```bash
dotnet build
```

## Публикация
```bash
dotnet publish -c Release -p:PublishSingleFile=true -o publish
```

## Стек
- .NET 8 / WPF
- CmlLib.Core - Ядро Minecraft
- DiscordRichPresence - Интеграция RPC

## Лицензия
MIT