using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

class Config
{
    public List<string> Folders { get; set; }
    public string DiskLetter { get; set; }
    public int MaxDiskUsagePercent { get; set; }
    public long MinFreeSpaceAfterCleanupMB { get; set; }
}

class Program
{
    static Config config;
    static string logDir;

    static void Main(string[] args)
    {
        logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        try
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");            
            config = LoadOrCreateDefaultConfig(configPath);

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

            DeleteOldLogs(10);

            string logEntry = $"[{DateTime.Now}] [INFO] Запуск программы\n";

            DriveInfo drive = new DriveInfo(config.DiskLetter);
            long totalBytes = drive.TotalSize;
            long freeBytes = drive.AvailableFreeSpace;

            int usedPercent = (int)(((double)(totalBytes - freeBytes) / totalBytes) * 100);
            logEntry += $"[{DateTime.Now}] [INFO] Диск {config.DiskLetter}: используется на {usedPercent}%\n";

            if (usedPercent < config.MaxDiskUsagePercent)
            {
                logEntry += $"[{DateTime.Now}] [INFO] Порог не превышен. Очистка не требуется.\n";
                Log(logEntry);
                return;
            }

            long targetFreeSpace = config.MinFreeSpaceAfterCleanupMB * 1024 * 1024;
            long freedSpace = 0;

            var files = GetAllFilesSortedByCreation(config.Folders);

            foreach (var file in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(file);
                    long size = fi.Length;

                    fi.Delete();
                    freedSpace += size;

                    logEntry += $"[{DateTime.Now}] [INFO] Удалён файл: {file} | Размер: {size / (1024 * 1024)} МБ\n";

                    if (drive.AvailableFreeSpace + freedSpace >= targetFreeSpace)
                        break;
                }
                catch (Exception ex)
                {
                    logEntry += $"[{DateTime.Now}] [ERROR] Ошибка удаления файла {file}: {ex.Message}\n";
                }
            }

            long finalFree = drive.AvailableFreeSpace;
            logEntry += $"[{DateTime.Now}] [INFO] Освобождено всего: {freedSpace / (1024 * 1024)} МБ\n";
            logEntry += $"[{DateTime.Now}] [INFO] Свободное место после очистки: {finalFree / (1024 * 1024)} МБ\n";

            Log(logEntry);
        }
        catch (Exception ex)
        {
            try
            {
                Log($"[{DateTime.Now}] [ERROR] Общая ошибка: {ex.Message}\n");
            }
            catch
            {
                Console.WriteLine($"[{DateTime.Now}] [ERROR] Ошибка при логировании: {ex.Message}");
            }
        }
    }

    static Config LoadOrCreateDefaultConfig(string path)
    {
        if (!File.Exists(path))
        {
            Log($"[{DateTime.Now}] [ERROR] Файл конфигурации не найден: {path}");
            Log($"[{DateTime.Now}] [INFO] Создание конфигурации по умолчанию...");

            var defaultConfig = new Config
            {
                Folders = new List<string> { @"D:\Videos" },
                DiskLetter = "D",
                MaxDiskUsagePercent = 90,
                MinFreeSpaceAfterCleanupMB = 800
            };

            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            Log($"[{DateTime.Now}] [INFO] Конфигурация по умолчанию создана. Отредактируйте файл и перезапустите программу.");
            Environment.Exit(0);
        }

        var configJson = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(configJson);
    }

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

    static void Log(string text)
    {
        string logPath = Path.Combine(logDir, $"log_{DateTime.Now:dd-MM-yyyy}.txt");
        File.AppendAllText(logPath, text + "\n");
        Console.WriteLine(text);
    }

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
                    Console.WriteLine($"[{DateTime.Now}] [INFO] Удалён старый лог: {fi.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[{DateTime.Now}] [ERROR] Ошибка при удалении старых логов: {ex.Message}");
        }
    }
}