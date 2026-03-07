# Nuclear_Hack_ARLU

Тестовый стенд: 31.29.180.7:789453.

Вход в T-FLEX:
- Адрес: 31.29.180.7:21325
- Login: Администратор
- Password: 323_75

## 1. Как развернуть это у себя
1. Установите:
   - Windows
   - .NET SDK 8
   - .NET Framework 4.8 Developer Pack
   - доступ к серверу T-FLEX DOCs
2. Создайте файл `src/Backend/EngGraphLabAdminApp/appsettings.Local.json`:
```json
{
  "TFlex": {
    "Server": "31.29.180.7:21321",
    "UserName": "Администратор",
    "Password": "PUT_REAL_PASSWORD_HERE",
    "UseAccessToken": false,
    "AccessToken": "",
    "ConfigurationGuid": null,
    "CommunicationMode": "GRPC",
    "DataSerializerAlgorithm": "Default",
    "CompressionAlgorithm": "None"
  },
  "TFlexAdapter": {
    "ExecutablePath": "",
    "RequestTimeoutSeconds": 60
  }
}
```
3. Соберите адаптер:
```powershell
dotnet build src/Backend/TFlexDocsAdapter/TFlexDocsAdapter.csproj -c Release
```
4. Запустите backend:
```powershell
dotnet run --project src/Backend/EngGraphLabAdminApp/EngGraphLabAdminApp.csproj -c Release
```
5. Откройте UI: `http://localhost:5101/`.

## 2. Как поменять адреса и установить версию DLL
1. Адреса и доступы меняются в `src/Backend/EngGraphLabAdminApp/appsettings.Local.json`:
   - `TFlex.Server`
   - `TFlex.UserName`
   - `TFlex.Password` (или `UseAccessToken` + `AccessToken`)
2. Скопируйте DLL версии `17.5.4.187` в папку `src/Backend/libs/17.5.4.187`.
3. Переключите `HintPath` например с `17.5.4.0` на `17.5.4.187` в файлах:
   - `src/Backend/TFlexDocsAdapter/TFlexDocsAdapter.csproj`
   - `src/Backend/TFlexDocsMacro/TFlexDocsMacro.csproj`
   - `src/Backend/TFlexUserFoldersMacro/TFlexUserFoldersMacro.csproj`
4. Пересоберите проекты backend/adapter.

## 3. Как сейчас работает система
- Сейчас стабильно работает создание/обновление аккаунтов пользователей.
- Создание папок и файлов в текущей реализации не работает стабильно.
- Основная причина: ограничения и нестабильность API T-FLEX DOCs, а также старый стек .NET Framework на стороне T-FLEX.


