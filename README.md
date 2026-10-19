# MySQL Backup Utility — MVP (C# / WPF / .NET 8)

Графическая утилита резервного копирования MySQL по ТЗ: профили подключений, бэкап через
`mysqldump` + gzip, восстановление через `mysql`, встроенный планировщик, ротация,
SQLite-хранилище, DPAPI-шифрование паролей, трей, логи.

## Требования
- Windows 10/11 x64, .NET 8 SDK (сборка проверена и на SDK 9/10, `TargetFramework=net8.0-windows`)
- MySQL Client (`mysqldump.exe`, `mysql.exe`) в `PATH` — https://dev.mysql.com/downloads/mysql/
  (проверка: `where mysqldump`, `where mysql`)

## Сборка и запуск
```powershell
dotnet restore src/MySqlBackupUtility
dotnet build src/MySqlBackupUtility -c Release
dotnet run --project src/MySqlBackupUtility -c Release
```

## Публикация (один .exe)
```powershell
dotnet publish src/MySqlBackupUtility -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish
```

## Установщик MSI (для ПК пользователя)
```powershell
powershell -ExecutionPolicy Bypass -File installer/build.ps1
# → dist/MySqlBackupUtility-0.1.0-win-x64.msi          (для всех пользователей)
# → dist/MySqlBackupUtility-0.1.0-win-x64-peruser.msi  (без прав администратора)
```
Готовые пакеты (собираются в `installer/build.ps1`, WiX v5 —
`dotnet tool install --global wix --version 5.0.2`):

| Файл | Куда ставится | Права |
|------|---------------|-------|
| `...win-x64.msi` | `C:\Program Files\MySqlBackupUtility`, ярлыки у всех пользователей | **нужен администратор** (подтвердить UAC); при отказе — ошибка 1925 |
| `...win-x64-peruser.msi` | `%LOCALAPPDATA%\Programs\MySqlBackupUtility`, ярлыки текущего пользователя | права администратора **не нужны** |

**Если установка «не работает»:** запустите `.msi` правым кликом →
«Запуск от имени администратора» и подтвердите запрос UAC. Если у учётной записи нет прав
администратора — используйте вариант `-peruser.msi` (двойной клик, без UAC).
После установки программа запускается автоматически; при ошибке запуска показывается окно
с текстом и пишется лог `%AppData%\MySqlBackupUtility\logs\crash-*.log`.

Если MSI поставить нельзя (нет прав, блокирует антивирус) — есть портативный вариант:
```powershell
dotnet publish src/MySqlBackupUtility -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false -o dist/portable
Compress-Archive -Path dist/portable/* -DestinationPath dist/MySqlBackupUtility-0.1.0-win-x64-portable.zip -Force
# распаковать и запустить MySqlBackupUtility.exe (установка не нужна)
```
Описание пакетов: `installer/Package.wxs`, `installer/PackagePerUser.wxs`.

## Использование
1. Вкладка «Подключения» → Новое → заполнить → «Проверить соединение».
   Нажмите **«Обновить список БД»** — сервер вернёт базы, отметьте галочками нужные
   («Выбрать все» = `*`, т.е. все базы). Выбор сохраняется в профиль. Сохранить.
2. Вкладка «Главная» → Новая задача → выбрать подключение.
   Поле «База данных»: можно оставить пустым — подставится выбор из подключения;
   либо вписать имя, несколько через запятую, или `*` = все БД.
   периодичность (Daily/Weekly/Monthly/Interval), время, папку, политику хранения →
   Сохранить задачу. «Создать бэкап сейчас» — ручной запуск.
3. Имя файла: `[ИмяБД]_[ГГГГММДД]_[ЧЧММСС].sql.gz`.
4. Вкладка «Архивы» → Восстановить (с подтверждением перезаписи) / из файла / удалить.
5. Вкладка «Логи» — история; файловый лог: `%AppData%\MySqlBackupUtility\logs\app-YYYYMMDD.log`.
6. Настройки: папка по умолчанию, статус утилит, сворачивание в трей.
   Контекстное меню трея: Открыть / Пауза / Выход.

## Планировщик
Встроенный (фоновый таймер 30 с, работает пока программа запущена, в т.ч. в трее).
Для запуска без входа пользователя создайте задачу в Windows Task Scheduler,
запускающую `publish\MySqlBackupUtility.exe` (см. ТЗ п.2.3 — штатный `taskschd.msc`).

## Безопасность
Пароли хранятся в `%AppData%\MySqlBackupUtility\app.db` только в DPAPI-шифрованном виде
(`ProtectedData.CurrentUser`), plain-text/base64-only запрещены. Пароль `mysqldump`/`mysql`
передаётся через env `MYSQL_PWD`, а не через видимую командную строку.

## Структура
```
src/MySqlBackupUtility/
  Models/      ConnectionProfile, BackupJob, BackupArchive, LogEntry
  Data/        Database.cs (SQLite: connections, jobs, archives, logs)
  Security/    CryptoService.cs (DPAPI)
  MySql/       MySqlService.cs, ToolLocator.cs
  Backup/      BackupService.cs, RestoreService.cs, RotationService.cs
  Scheduling/  SchedulerService.cs
  Services/    AppState.cs
  MainWindow.xaml(.cs)  — 5 вкладок + StatusBar + Tray
```

## Ограничения MVP
- Нет E-mail/Telegram-уведомлений (п.2.5 опционально).
- Восстановление из `*` (все БД) требует выбора целевой БД вручную.
- Для БД 5+ ГБ проверьте свободное место и таймауты; прогресс — текстовый (стриминг, без OOM).
