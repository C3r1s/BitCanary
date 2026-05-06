# BitCanary

Кроссплатформенный мессенджер с сервером на **ASP.NET Core** и десктоп-клиентом на **Avalonia**: чаты, сообщения, медиа, звонки, real-time через **SignalR**, аутентификация **JWT**, сквозное шифрование (ключевые пакеты / **NSec**). Сервер хранит данные в **PostgreSQL**, клиент использует локальный **SQLite** для кэша и офлайн-состояния.

## Возможности

- Регистрация и вход, работа с профилем и чатами
- Сообщения и вложения (файловое хранилище на стороне API)
- Real-time обновления чата (SignalR hub `/hubs/chat`)
- Голосовые/видео вызовы (интеграция на стороне приложения)
- OpenAPI / Swagger в режиме Development

## Стек

| Компонент | Технологии |
|-----------|------------|
| API | .NET 10, EF Core, Npgsql, JWT Bearer, SignalR, Swashbuckle |
| Клиент | Avalonia 11, CommunityToolkit.Mvvm, SignalR Client, SQLite |
| Контракты | `Messenger.Shared.Contracts` (общие DTO и real-time модели) |

Подробная раскладка проектов: [`src/README.md`](src/README.md).

## Требования

- [.NET SDK 10](https://dotnet.microsoft.com/download) (в решении используются превью-пакеты — версия должна соответствовать проекту)
- [PostgreSQL](https://www.postgresql.org/)
- Для клиента: Windows 10+ (таргет `net10.0-windows10.0.17763.0`)

## Быстрый старт

### 1. База данных

Создайте базу и укажите строку подключения в `src/Backend/Messenger.Api/appsettings.json` (или переопределите через конфигурацию / секреты):

```json
"ConnectionStrings": {
  "Postgres": "Host=localhost;Port=5432;Database=canary_avo;Username=postgres;Password=your_password"
}
```

При старте API миграции применяются автоматически (`MigrateAsync`).

### 2. Запуск API

```bash
cd src/Backend/Messenger.Api
dotnet run
```

По умолчанию HTTP-профиль слушает `http://localhost:5176` (см. `Properties/launchSettings.json`).

Для JWT и файлового хранилища проверьте секции `Jwt` и `Storage` в `appsettings.json` (в продакшене задайте надёжный `SigningKey` и путь `RootPath`).

### 3. Запуск клиента

Укажите URL API (должен совпадать с запущенным сервером):

```bash
set MESSENGER_API_BASE_URL=http://localhost:5176
cd src/Client/Messenger.Client.Avalonia
dotnet run
```

На Windows в PowerShell: `$env:MESSENGER_API_BASE_URL="http://localhost:5176"`.

### 4. Тесты

```bash
dotnet test
```

из корня репозитория (решение `BitCanary.sln`).

## Решение

- **Backend:** `Messenger.Domain`, `Messenger.Application`, `Messenger.Infrastructure`, `Messenger.Api`
- **Client:** `Messenger.Client.Avalonia`
- **Tests:** `tests/Messenger.*.Tests`
