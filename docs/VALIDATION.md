# Проверено 5 октября 2026

Окружение: Linux, локальный .NET SDK 8.0.425, runtime 8.0.31. Revit отсутствует. PowerShell установщик, WPF окна и API внутри Revit не запускались.

| Проверка | Результат |
|---|---|
| Domain xUnit | 55 passed, 0 failed |
| Presentation xUnit без WPF | 13 passed, 0 failed |
| API 2021 / net48 | Сборка без ошибок/предупреждений |
| API 2022 / net48 | Сборка без ошибок/предупреждений |
| API 2023 / net48 | Сборка без ошибок/предупреждений |
| API 2024 / net48 | Сборка без ошибок/предупреждений |
| API 2025 / net8.0-windows | Сборка без ошибок/предупреждений |
| HTML, Chromium 154 | Все browser checks прошли; desktop 1440 px, mobile 390 px |
| Синтетическое демо | 7 устройств: 3 OK, 2 limit-exceeded с путями, 1 disconnected, 1 outside-network |
| ModelGuard демо | 6 элементов, 8 замечаний, одна предложенная writable марка |

В тестах Дейкстра сравнивается с Floyd–Warshall на 30 случайных графах по 14 узлов (5 880 пар); BVH проверяется 300 запросами на 150 случайных 3D-сегментах против независимого exhaustive алгоритма. Дополнительно: interior attachments, вертикальные трассы, геометрическое пересечение без связи, несколько taps, параллельные рёбра, stale write guard, v1/v2/v3, JSON/HTML escaping, профили параметров, regex timeout, нумерация, ввод/команды/локализация ViewModel.

Браузер: выбор маршрута, фильтр, смена XY/XZ, empty state, mobile overflow, отсутствие JS errors и сетевых запросов. [Первичный протокол](evidence/browser-check.json); снимки в images. Проверка выполнена Playwright 1.63.0 с установленным Chromium 154 через executablePath; CI настроен на bundled browser, запуск CI в GitHub не подтверждён.

Benchmark: 3 600 узлов, 7 080 сегментов, 1 500 запросов. Построение индекса 51.62 мс; запросы BVH 4.85 мс, exhaustive 68.41 мс, наблюдаемое ускорение 14.1×. Максимальная разница расстояний 0. Полный расчёт 500 устройств — 115 мс, 500 восстановленных путей. [Исходный JSON](evidence/benchmark.json). Warmup + median of 3 batches; это elapsed-time microbenchmark на данной машине, без Autodesk API. Ускорение отдельных запросов не означает такое же ускорение всего плагина.

Повторить: `./scripts/demo.sh`. Benchmark: `dotnet run --project tools/BimPortfolio.Demo -c Release -- --benchmark artifacts/benchmark.json`. На этой машине вместо системного dotnet используется `.tools/dotnet/dotnet`, не включённый в Git/ZIP. Браузерные команды — в README.

Следующая обязательная стадия — [приёмка в Revit](REVIT-ACCEPTANCE.md). Компиляция API не подтверждает host-runtime, worksharing, Undo, drafting view generation и корректность конкретных семейств/шаблонов.
