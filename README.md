# Nuclear_Hack_ARLU

Backend-сервис и веб-интерфейс для автоматизации задач T-FLEX DOCs по ТЗ хакатона:
- проверка подключения к серверу T-FLEX DOCs,
- импорт списка студентов (CSV/XML),
- построение плана provisioning (группы, пользователи, папки, права, задания),
- запуск в режиме `dry-run` и базовый `execute`-каркас.

## Что реализовано

- ASP.NET Core backend (`net8.0`) с API:
  - `POST /api/tflex/check-connection`
  - `POST /api/tflex/check-connection/custom`
  - `POST /api/provisioning/preview`
  - `POST /api/provisioning/execute?dryRun=true|false`
- Веб-интерфейс (MVP) по адресу `/`:
  - форма проверки подключения,
  - загрузка CSV/XML,
  - просмотр студентов и плана действий,
  - запуск `preview` / `dry-run` / `execute`.

## Структура

- `src/Backend/EngGraphLabAdminApp` — backend + UI (`wwwroot`)
- `src/Backend/libs` — подключенные T-FLEX DLL:
  - `TFlex.DOCs.Common.dll`
  - `TFlex.DOCs.Model.dll`

## Требования

1. .NET SDK 8.0+
2. Установленный клиент T-FLEX DOCs (той же ветки версии, что сервер)
3. Доступ к серверу T-FLEX DOCs (логин/пароль или токен)

## Откуда брать DLL для T-FLEX

Для внешнего приложения DLL берутся из установленного клиента T-FLEX DOCs, обычно:

`C:\Program Files (x86)\T-FLEX DOCs 17\Program`

В проекте уже есть минимальные ссылки на `TFlex.DOCs.Common.dll` и `TFlex.DOCs.Model.dll`,  
остальные зависимости подхватываются через `AssemblyResolver` из `ClientProgramDirectory`.

Подробно: `src/Backend/EngGraphLabAdminApp/TFLEX_SETUP.md`

## Конфигурация

Базовые настройки:
- `src/Backend/EngGraphLabAdminApp/appsettings.json`
- `src/Backend/EngGraphLabAdminApp/appsettings.Development.json`

Локальные (секретные) настройки:
- `src/Backend/EngGraphLabAdminApp/appsettings.Local.json` (в `.gitignore`)
- пример: `src/Backend/EngGraphLabAdminApp/appsettings.Local.example.json`

Пример блока `TFlex`:

```json
{
  "TFlex": {
    "Server": "31.29.180.7:21325",
    "UserName": "Администратор",
    "Password": "******",
    "UseAccessToken": false,
    "ClientProgramDirectory": "C:\\Program Files (x86)\\T-FLEX DOCs 17\\Program",
    "CommunicationMode": "GRPC",
    "DataSerializerAlgorithm": "Default",
    "CompressionAlgorithm": "None"
  }
}
```

## Запуск

```powershell
dotnet run --project src/Backend/EngGraphLabAdminApp/EngGraphLabAdminApp.csproj
```

По умолчанию профиль запуска использует `http://localhost:5101`.

## Проверка

1. Открыть UI: `http://localhost:5101/`
2. Проверить API:
   - `GET /api/health`
   - `POST /api/tflex/check-connection`
3. Для проверки provisioning загрузить CSV/XML в UI и запустить `Preview`.

## Важно

- `execute` сейчас реализован как foundation-каркас (точка расширения под реальные операции OpenAPI).
- Если в `check-connection` ошибка про отсутствующие DLL — проверьте `ClientProgramDirectory` и установленный клиент T-FLEX DOCs.
