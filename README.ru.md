# OmniHID

<div align="center">

[English](README.md) | **Русский**

[![Платформа](https://img.shields.io/badge/платформа-Windows%207%E2%80%9311-0078D6.svg?style=flat-square&logo=windows)](https://github.com/)
[![Среда выполнения](https://img.shields.io/badge/.NET-%3E%3D%204.8%20%7C%206.0%2B-512BD4.svg?style=flat-square&logo=dotnet)](https://github.com/)
[![Зависимости](https://img.shields.io/badge/зависимости-Ноль%20(Native%20Win32)-brightgreen.svg?style=flat-square)](https://github.com/)
[![Протоколы](https://img.shields.io/badge/протоколы-14%20Встроенных-orange.svg?style=flat-square)](docs/protocol-development.md)
[![Лицензия](https://img.shields.io/badge/лицензия-MIT-blue.svg?style=flat-square)](LICENSE)

*Легковесный C# движок телеметрии и диагностическая CLI-утилита для опроса заряда батареи беспроводной игровой периферии через нативные Win32 HID API — без вендорного софта.*

</div>

---

## Обзор

Современная игровая периферия (мыши, клавиатуры, гарнитуры и геймпады) поддерживает высокоскоростное беспроводное подключение и передачу телеметрии. Однако для просмотра банального уровня заряда батареи вендоры требуют установки массивных пакетов ПО (Logitech G HUB, Razer Synapse, Corsair iCUE, SteelSeries GG), которые потребляют сотни мегабайт оперативной памяти, запускают по 5–8 фоновых служб и могут вызывать задержки ввода (input lag).

**OmniHID** предлагает чистое, независимое решение:
- **Для пользователей и геймеров:** Автономная диагностическая утилита с интерактивным дашбордом, моментальным сканированием заряда батареи, сниффером пакетов в реальном времени с подсветкой изменений (diff) и автоматическим A-B калибратором.
- **Для разработчиков:** Модульная C# библиотека (`OmniHid.Core.dll`) с нулевыми сторонними зависимостями, реактивной событийно-ориентированной моделью, потокобезопасными снимками состояния без блокировок, поддержкой горячей перезагрузки (Hot Reload) декларативных JSON-профилей и расширяемой архитектурой драйверов протоколов.

---

## Зачем OmniHID?

| Критерий | Вендорное ПО (G HUB, Synapse, iCUE) | OmniHID |
| :--- | :--- | :--- |
| **Потребление памяти** | 350 МБ – 800 МБ (Chromium / Electron) | **< 10 МБ RAM** |
| **Внешние зависимости** | Несколько гигабайт, пакеты VC++, веб-сервисы | **Ноль** (Чистый Win32 P/Invoke) |
| **Фоновые службы** | От 4 до 8 активных служб Windows | **0 служб** (Работает по требованию или in-process) |
| **Сетевая телеметрия** | Сбор статистики, обязательная аналитика | **100% Offline** (Никаких отправок в сеть) |
| **Влияние на Input Lag** | Низкоуровневые хуки клавиатуры и мыши (`WH_*_LL`)| **0% влияния** (Неблокирующие контрольные HID-пайпы) |
| **Умный Dual-Mode** | Путаница и дубликаты записей «провод / донгл» | **Автоматическая дедупликация** (Приоритет кабеля) |
| **Расширяемость** | Закрытая вендорная экосистема | **JSON-профили** с горячей перезагрузкой на лету |

---

## Архитектура проекта

```
┌────────────────────────────────────────────────────────┐
│                  Приложение пользователя               │
│      (WPF / WinForms / Service / CLI / Оверлей в играх)│
└───────────────────────────▲────────────────────────────┘
                            │ События / Снимки телеметрии
┌───────────────────────────┴────────────────────────────┐
│                       OmniManager                      │
│  - Агрегация множественных интерфейсов устройства      │
│  - Умная дедупликация Dual-Mode (провод / радиоканал)  │
│  - Поток обработки USB PnP событий подключения/снятия  │
└─────────────┬───────────────────────────┬──────────────┘
              │                           │
┌─────────────▼──────────────┐ ┌──────────▼──────────────┐
│       DeviceRegistry       │ │   14+ Драйверов железа  │
│  - Вшитые JSON-профили     │ │  - logitech-hidpp / cent │
│  - Внешние JSON-профили    │ │  - areson / royuan / ...│
│  - Hot-reload отслеживание │ │  - razer / steelseries  │
└────────────────────────────┘ └──────────┬──────────────┘
                                          │
┌─────────────────────────────────────────▼──────────────┐
│                    Win32HidTransport                   │
│  - SetupAPI.dll: перечисление GUID_DEVINTERFACE_HID    │
│  - Hid.dll: HidD_GetFeature, Overlapped I/O, Exchange  │
│  - XInput: опрос батареи геймпада (слоты 0..3)         │
│  - Windows 10/11 PnP: DEVPKEY_Device_BatteryLevel      │
└────────────────────────────────────────────────────────┘
```

---

## Быстрый старт за 60 секунд

### 1. Сборка из исходников

OmniHID не требует сторонних инструментов сборки или пакетных менеджеров. Компиляция занимает ~1 секунду через встроенный в Windows компилятор C# (`csc.exe`):

```cmd
build.bat
```

**Результат компиляции:**
- `bin\OmniHid.Core.dll` — Библиотека ядра телеметрии (совместима с .NET Framework 4.8 и .NET 6+).
- `bin\omni-hid.exe` — Автономная CLI-утилита.

---

### 2. Для пользователей: Запуск CLI-утилиты

Запуск интерактивного консольного меню:
```cmd
omni-hid
```

Или прямой опрос подключённой периферии:
```cmd
omni-hid scan
```

```text
Category     Device Name                      VID:PID      Battery        Status         Voltage    Protocol           Endpoints  Hints
────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
🖱 Mouse     ARDOR GAMING Prime X             25A7:FA7B    100%           Full (Wired)   4190 mV    areson             3 EPs      [⚡ Direct Cable]
⌨ Keyboard  ROYUAN Wireless Mechanical       3151:3008    82%            Discharging    --         royuan             4 EPs      [~48h remaining]
🎧 Headset   Logitech G PRO X 2 Lightspeed    046D:0AF7    94%            Discharging    3940 mV    logitech-centurion 2 EPs      [~42h remaining]
🎮 Gamepad   Xbox Wireless Controller         045E:0B12    80%            Discharging    --         xbox-controller    1 EPs      [XInput Slot 0]
```

> **Умный приоритет Dual-Mode (Провод / Радиоканал)**: Если устройство поддерживает подключение как по проводу, так и через 2.4 GHz ресивер (например, мышь ARDOR Gaming Prime X), подключение кабеля автоматически переводит статус в проводной режим (`Full (Wired)` / `Charging`) и скрывает дублирующую запись беспроводного донгла. Отключение кабеля мгновенно возвращает ресивер в список активных устройств. Флаг `--all` (или `-a`) позволяет увидеть оба физических интерфейса одновременно с пометкой `⏸ Standby`.

---

### 3. Для разработчиков: Интеграция C# библиотеки

Подключите ссылку на `bin\OmniHid.Core.dll` в ваш проект и начните отслеживать телеметрию всего в 10 строках кода:

```csharp
using System;
using OmniHid.Core;
using OmniHid.Core.Abstractions;

class Program
{
    static void Main()
    {
        using (var manager = new OmniManager())
        {
            manager.DeviceConnected += dev => 
                Console.WriteLine($"[+] Подключено: {dev.Name} ({dev.Category})");

            manager.TelemetryUpdated += (dev, tel) => 
                Console.WriteLine($"[*] {dev.Name}: {tel.LevelPercent}% [{tel.StateDescription}]");

            // Запуск периодического опроса (раз в 15 сек) + отслеживание PnP событий USB
            manager.StartMonitoring(pollIntervalMs: 15000);

            Console.WriteLine("Мониторинг запущен. Нажмите Enter для выхода...");
            Console.ReadLine();
        }
    }
}
```

---

## Диагностический инструментарий CLI

Все команды можно запускать как по ключевому слову, так и по номеру в интерактивном меню (`0`..`8`):

```cmd
omni-hid [команда|номер] [фильтр] [опции]
```

| # | Команда | Синтаксис | Описание |
| :-: | :--- | :--- | :--- |
| `[1]` | `scan` | `omni-hid scan [фильтр] [--all]` | Опрос поддерживаемых устройств: процент заряда, статус зарядки, напряжение и эндпоинты. |
| `[2]` | `list` | `omni-hid list [фильтр] [--flat]` | Дерево всех зарегистрированных HID-интерфейсов Windows, сгруппированных по физическим устройствам (`VID:PID`). |
| `[3]` | `debug` | `omni-hid debug [фильтр]` | Глубокий аппаратный аудит: XInput слоты 0–3, распознавание микроконтроллеров (IC Fingerprinting), анализ дескрипторов. |
| `[4]` | `hunt` | `omni-hid hunt [фильтр]` | Автоматический свип Feature-отчётов (`0x00`..`0xFF`), эвристический скоринг и вывод Top-5 кандидатов на байт батареи. |
| `[5]` | `sniff` | `omni-hid sniff [фильтр] [--timeout <сек>]`| Перехват входящих отчётов в реальном времени с подсветкой изменившихся байтов (diff) и дампом в файл. |
| `[6]` | `monitor` | `omni-hid monitor` | Монитор подключения и отключения USB-устройств в реальном времени через `WM_DEVICECHANGE`. |
| `[7]` | `calibrate` | `omni-hid calibrate [фильтр]` | Пошаговый A-B калибратор: сравнение срезов на батарее и на проводе для точной изоляции байтов заряда. |
| `[8]` | `export` | `omni-hid export [фильтр]` | Генерация `device_spec_<VID>_<PID>.md` с картой эндпоинтов и готовым промптом для LLM для создания C# драйвера. |
| `[0]` | `help` | `omni-hid help` | Вызов справочной информации и примеров. |

---

## Поддерживаемые протоколы

Ядро OmniHID включает 14 встроенных протокольных драйверов для популярных платформ игровых контроллеров и вендорных стандартов:

| ID протокола | Аппаратная платформа / Устройства | Метод получения телеметрии |
| :--- | :--- | :--- |
| `logitech-hidpp` | Logitech HID++ 2.0 / 1.0 (Nordic / TI) | 20-байтные Long Reports (Feature `0x1000` / `0x1004`) |
| `logitech-centurion` | Logitech G PRO X 2 Lightspeed Audio | 64-байтный отчет управления аудио (Report ID `0x51`) |
| `areson` | Areson Wireless MCU (например, ARDOR Prime X) | Feature Report `0x05` с контрольной суммой `XOR 0x55` |
| `royuan` | Клавиатуры ROYUAN / YiChip (Akko, Epomaker) | Output Report `0x83` / `0x80` Overlapped Exchange |
| `compx` | CompX Gaming Wireless Microcontroller (CX52850) | Вендорные Feature Reports |
| `sinowealth` | Игровые мыши на микроконтроллерах SinoWealth 8051 | Вендорные Feature Reports |
| `steelseries` | Мыши и гарнитуры SteelSeries Aerox / Rival / Arctis | Проприетарный HID Control Pipe SteelSeries |
| `razer` | Устройства Razer HyperSpeed / Chroma | 90-байтный кадровый пакет Razer Unified HID Report |
| `corsair-headset` | Беспроводные гарнитуры Corsair VOID / HS / Virtuoso | Протокол Corsair Wireless Audio |
| `hyperx-headset` | Гарнитуры HyperX Cloud / Flight / Stinger Wireless | Отчеты управления HyperX Audio HID |
| `sony-dualsense` | Геймпады Sony DualSense и DualShock 4 | Прямой опрос Input Report 0x01 / 0x31 |
| `xbox-controller` | Геймпады Microsoft Xbox Wireless Controller | Опрос XInput слотов и Bluetooth GATT батареи (`DEVPKEY`)|
| `generic-keyboard` | Стандартные fallback-клавиатуры HID | Стандартные Input Reports |
| `generic-peripheral` | Стандартная служба Windows HID Battery Service | HID Battery Service (`UsagePage 0x0085` / `0x0084`) |

---

## Добавление новых устройств

### 1. Декларативные JSON-профили (без написания кода)

Если чипсет устройства уже поддерживается одним из встроенных драйверов, достаточно положить файл профиля в папку `devices/` или `%APPDATA%\OmniHid\devices\`. Формат JSONC поддерживает комментарии (`//`):

```jsonc
{
  "model_name": "ARDOR GAMING Prime X",
  "vendor_id": "0x25A7",
  "product_ids": [
    "0xFA7B", // Проводной режим по USB-кабелю
    "0xFA7C"  // Беспроводной 2.4GHz ресивер (донгл)
  ],
  "wired_product_ids": [
    "0xFA7B"  // Указывает проводной PID для умной дедупликации
  ],
  "category": "Mouse",
  "protocol": "areson",
  "target_usage_page": "0xFF02",
  "target_usage": "0x0002",
  "battery_life_hours": 60,
  "capabilities": [
    "BatteryLevel",
    "ChargingStatus",
    "VoltageReading"
  ]
}
```

> **Горячая перезагрузка (Hot Reload)**: Файлы в `%APPDATA%\OmniHid\devices\` и `./devices/` отслеживаются через `FileSystemWatcher`. При сохранении файла профиль перезагружается «на лету» без перезапуска программы. В таблице CLI такие устройства помечаются иконкой `📄`.

### 2. Неизвестные протоколы — реверс-инжиниринг за 3 шага

1. **Классификация:** Запустите `omni-hid debug <устройство>`, чтобы изучить дескрипторы эндпоинтов и определить семейство чипа через IC Fingerprinting.
2. **Изоляция:** Запустите `omni-hid calibrate <устройство>` (A-B калибровка провод/батарея) или `omni-hid hunt <устройство>` (поиск Feature-отчетов), чтобы локализовать байты заряда и флага зарядки.
3. **Генерация:** Запустите `omni-hid export <устройство>` для создания файла `device_spec_<VID>_<PID>.md`. Скопируйте готовый промпт в ChatGPT, Claude или Gemini для автоматического написания C# драйвера!

---

## Центр документации

Подробные технические руководства доступны в каталоге [`docs/`](docs/):

- 📖 [**Начало работы**](docs/getting-started.md) — Системные требования, варианты сборки, первый запуск.
- 💻 [**Руководство разработчика**](docs/developer-guide.md) — Интеграция C# библиотеки, жизненный цикл, диспетчеризация в UI-поток (WPF/WinForms), пример трей-монитора.
- 📚 [**Справочник API**](docs/api-reference.md) — Описание типов: `IOmniManager`, `IOmniDevice`, `BatteryTelemetry`, `IHidTransport` и др.
- ⚙️ [**Справочник по CLI**](docs/cli-reference.md) — Полное руководство по всем 8 командам и диагностическим утилитам `omni-hid`.
- 📄 [**Профили устройств и Hot Reload**](docs/device-profiles.md) — Схема JSON-профилей, папки автозагрузки и настройка Dual-Mode.
- 🔬 [**Разработка протоколов**](docs/protocol-development.md) — Реверс-инжиниринг протоколов и реализация интерфейса `IProtocolHandler`.
- 🏛️ [**Архитектура и внутреннее устройство**](docs/architecture.md) — Win32 P/Invoke подсистема, агрегация интерфейсов и снимки без аллокаций.

---

## Структура проекта

```
omni-hid/
├── devices/                 # Декларативные JSON-профили (вшиваются при сборке)
│   ├── gamepads/            # Профили геймпадов (Xbox и др.)
│   ├── headsets/            # Профили гарнитур (Logitech, Corsair и др.)
│   ├── keyboards/           # Профили клавиатур (ROYUAN, Akko и др.)
│   └── mice/                # Профили мышей (Areson, CompX и др.)
├── docs/                    # Полный комплект документации и руководств
│   ├── api-reference.md     # Документация C# API ядра
│   ├── architecture.md      # Архитектура, Win32 P/Invoke и агрегация
│   ├── cli-reference.md     # Полное руководство по CLI-утилите
│   ├── developer-guide.md   # Руководство по интеграции в .NET проекты
│   ├── device-profiles.md   # Формат JSON-профилей и горячая перезагрузка
│   ├── getting-started.md   # Быстрый старт и варианты компиляции
│   └── protocol-development.md # Реверс-инжиниринг и создание IProtocolHandler
├── installer/               # Скрипт сборщика установщика Windows (Inno Setup)
├── reference/               # Спецификации и референсные проекты с открытым кодом
├── src/
│   ├── OmniHid.Core/        # Ядро: P/Invoke, транспорт, реестр, протоколы
│   └── OmniHid.Cli/         # Диагностическая CLI-утилита
├── build.bat                # Сборка за 1 секунду через системный csc.exe
└── OmniHid.sln              # Решение Visual Studio / MSBuild
```

---

## Лицензия

OmniHID распространяется под открытой лицензией [MIT](LICENSE).
