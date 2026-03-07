# T-FLEX setup

## Актуальная схема

`EngGraphLabAdminApp` (`net8`) не вызывает T-FLEX OpenAPI напрямую.

Вызовы идут через `src/Backend/TFlexDocsAdapter` (`net48`) по локальному IPC:

- backend запускает `TFlexDocsAdapter.exe <command>`
- JSON передаётся в `stdin`
- JSON-ответ читается из `stdout`

Команды adapter:

- `check-connection`
- `execute-foundation`

## Где должны лежать DLL

Adapter ищет зависимости в следующих местах:

1. Папка рядом с `TFlexDocsAdapter.exe`
2. Подпапка `libs` рядом с `TFlexDocsAdapter.exe`
3. `src/Backend/libs`
4. `TFLEX_DOCS_HOME` и `TFLEX_DOCS_HOME/Program` (если переменная задана)
5. Автопоиск установок `T-FLEX DOCs*` в `Program Files` / `Program Files (x86)`

`ClientProgramDirectory` не нужен.

## Диагностика ошибок подключения

Если `check-connection` возвращает ошибку, смотрите `missingDependencies`:

- цепочку исключений,
- список недостающих DLL,
- информацию о попытках подключения по режимам `GRPC/WCF`.

### Частые случаи

1. Нет нужной версии `TFlex.DOCs.*`
- Положите совместимый набор DLL из одной и той же поставки T-FLEX DOCs в `src/Backend/libs`.

2. Ошибки formatter/serializer
- Adapter принудительно использует `Protobuf`, даже если в конфиге указан `Default`/`ZeroFormatter`.

3. Ошибка только на `GRPC`
- Adapter автоматически делает fallback-попытку через `WCF`.

## Секреты

Логины/пароли храните только в:

`src/Backend/EngGraphLabAdminApp/appsettings.Local.json`

Файл должен оставаться вне git.

## Update 2026-03

- Adapter no longer forces `Default/ZeroFormatter` to `Protobuf`.
- `execute-foundation` applies ACL rules for group/student folders and deny rule for root `Задания`.
- Assignment flow supports CAD export to `TIFF/PDF` with fallback to source extension if export is unavailable.
- Password export is available via backend endpoint: `GET /api/provisioning/passwords/{token}?format=csv|xlsx`.
