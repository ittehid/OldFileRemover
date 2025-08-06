using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

// Класс конфигурации, данные загружаются из config.json
class Config
{
    public List<string> Folders { get; set; }                  // Папки, в которых нужно удалять файлы
    public string DiskLetter { get; set; }                     // Буква диска, на котором ведётся мониторинг
    public int MaxDiskUsagePercent { get; set; }              // Порог заполнения диска, при котором запускается очистка
    public long MinFreeSpaceAfterCleanupMB { get; set; }      // Минимальное свободное место, которого нужно достичь (в МБ)
}

class Program
{
    static Config config;           // Глобальная переменная с конфигурацией
    static string logDir;           // Путь к папке логов

    static void Main(string[] args)
    {
        // Создаём папку logs, если её ещё нет
        logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        try
        {
            // Загружаем или создаём файл конфигурации
            config = LoadOrCreateDefaultConfig("config.json");

            // Проверяем, существуют ли указанные в конфиге папки
            foreach (var folder in config.Folders)
            {
                if (!Directory.Exists(folder))
                {
                    string error = $"[{DateTime.Now}] [ERROR] Папка не найдена: {folder}";
                    Log(error);
                    Console.WriteLine(error);
                    Environment.Exit(1);
                }
            }

            // Удаляем старые логи (старше 10 дней)
            DeleteOldLogs(10);

            string logEntry = $"[{DateTime.Now}] [INFO] Запуск программы\n";

            // Получаем информацию о диске
            DriveInfo drive = new DriveInfo(config.DiskLetter);
            long totalBytes = drive.TotalSize;
            long freeBytes = drive.AvailableFreeSpace;

            // Считаем процент заполнения
            int usedPercent = (int)(((double)(totalBytes - freeBytes) / totalBytes) * 100);
            logEntry += $"[INFO] Диск {config.DiskLetter}: используется на {usedPercent}%\n";

            // Если диск заполнен менее чем на указанный процент — завершить
            if (usedPercent < config.MaxDiskUsagePercent)
            {
                logEntry += "[INFO] Порог не превышен. Очистка не требуется.\n";
                Log(logEntry);
                return;
            }

            // Если диск заполнен — начинаем очистку
            long targetFreeSpace = config.MinFreeSpaceAfterCleanupMB * 1024 * 1024; // Переводим МБ в байты
            long freedSpace = 0;

            // Получаем список всех файлов, отсортированных по дате создания (от самых старых)
            var files = GetAllFilesSortedByCreation(config.Folders);

            foreach (var file in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(file);
                    long size = fi.Length;

                    fi.Delete();                  // Удаляем файл
                    freedSpace += size;          // Учитываем освобождённое место

                    logEntry += $"[INFO] Удалён файл: {file} | Размер: {size / (1024 * 1024)} МБ\n";

                    // Проверка: достигнуто ли нужное свободное место
                    if (drive.AvailableFreeSpace + freedSpace >= targetFreeSpace)
                        break;
                }
                catch (Exception ex)
                {
                    // Логируем ошибку, если файл не удалось удалить
                    logEntry += $"[ERROR] Ошибка удаления файла {file}: {ex.Message}\n";
                }
            }

            long finalFree = drive.AvailableFreeSpace;
            logEntry += $"[INFO] Освобождено всего: {freedSpace / (1024 * 1024)} МБ\n";
            logEntry += $"[INFO] Свободное место после очистки: {finalFree / (1024 * 1024)} МБ\n";

            // Записываем всё в лог
            Log(logEntry);
        }
        catch (Exception ex)
        {
            // Общий блок отлова ошибок, чтобы ничего не сломалось "в тишину"
            try
            {
                Log($"[{DateTime.Now}] [ERROR] Общая ошибка: {ex.Message}\n");
            }
            catch
            {
                Console.WriteLine($" [ERROR] Ошибка при логировании: {ex.Message}");
            }
        }
    }

    // Загружает конфигурацию или создаёт конфигурационный файл по умолчанию
    static Config LoadOrCreateDefaultConfig(string path)
    {
        if (!File.Exists(path))
        {
            // Создаём конфигурацию по умолчанию
            var defaultConfig = new Config
            {
                Folders = new List<string> { @"D:\Videos" }, // Папка по умолчанию
                DiskLetter = "D",                             // Буква диска
                MaxDiskUsagePercent = 90,                     // Порог заполнения в %
                MinFreeSpaceAfterCleanupMB = 800              // Минимум свободного места после очистки
            };

            // Сохраняем конфигурацию в JSON
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            // Сообщаем пользователю
            string msg = $"[{DateTime.Now}] [INFO] Файл конфигурации не найден. Создан файл по умолчанию: {path}\n" +
                         $"[{DateTime.Now}] [INFO] Отредактируйте файл конфигурации и перезапустите программу.";
            Log(msg);
            Environment.Exit(0); // Завершаем выполнение, чтобы пользователь мог настроить путь
        }

        // Загружаем конфигурацию из JSON
        var configJson = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(configJson);
    }

    // Получает список всех файлов из указанных папок, отсортированных по дате создания (по возрастанию)
    static List<string> GetAllFilesSortedByCreation(List<string> folders)
    {
        var files = new List<string>();

        foreach (var folder in folders)
        {
            if (Directory.Exists(folder))
            {
                var folderFiles = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.CreationTime)
                    .Select(f => f.FullName);

                files.AddRange(folderFiles);
            }
        }

        return files;
    }

    // Метод логирования: записывает в файл логов и выводит в консоль
    static void Log(string text)
    {
        string logPath = Path.Combine(logDir, $"log_{DateTime.Now:dd-MM-yyyy}.txt");
        File.AppendAllText(logPath, text + "\n");
        Console.WriteLine(text);
    }

    // Удаление логов, старше указанного количества дней
    static void DeleteOldLogs(int daysToKeep)
    {
        try
        {
            var logFiles = Directory.GetFiles(logDir, "log_*.txt");

            foreach (var file in logFiles)
            {
                var fi = new FileInfo(file);
                if (fi.CreationTime < DateTime.Now.AddDays(-daysToKeep))
                {
                    fi.Delete();
                    Console.WriteLine($"[INFO] Удалён старый лог: {fi.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[{DateTime.Now}] [ERROR] Ошибка при удалении старых логов: {ex.Message}");
        }
    }
}
