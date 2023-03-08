using FolderBackup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Timers;

namespace FileBackup;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ILogger<FileWatcher> _fileWatcherLogger;
    private readonly IConfiguration _configuration;
    public Worker(ILogger<Worker> logger, ILogger<FileWatcher> fileWatcherLogger, IConfiguration configuration)
    {
        _logger = logger;
        _fileWatcherLogger = fileWatcherLogger;
        _configuration = configuration;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watcher = new FileWatcher(_fileWatcherLogger, _configuration);
        while (!stoppingToken.IsCancellationRequested)
        {
            watcher.Watch();
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(10000, stoppingToken);
        }
    }
}

public class FileWatcher
{
    private static ILogger<FileWatcher> _logger;
    private static IConfiguration _configuration;
    private static FileSystemWatcher _watcher;
    private static System.Timers.Timer _timer;
    private static Dictionary<string, DateTime> _prevFileTimestamps;
    private static Dictionary<string, DateTime> _changedFileTimestamps = new Dictionary<string, DateTime>();
    // private static string _sourcePath = @"C:\Users\XiaohanLiu\OneDrive - Kerridge & Partners (1)\Original";
    private static string _sourcePath;
    // private static string _backupPath = @"C:\Users\XiaohanLiu\Documents\Backup";
    private static string _backupPath;
    private static string _timestampFile = @"../../../fileTimestamps.json";
    private const int CPU_THRESHOLD = 70;
    private const int DISK_THRESHOLD = 70;
    public FileWatcher(ILogger<FileWatcher> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _sourcePath = _configuration.GetSection("SourcePath").Value;
        _backupPath = _configuration.GetSection("BackupPath").Value;

        _watcher = new FileSystemWatcher(_sourcePath);

        if (!Directory.Exists(_backupPath))
        {
            Directory.CreateDirectory(_backupPath);
        }
        if (File.Exists(_timestampFile))
        {
            string json = File.ReadAllText(_timestampFile);
            _prevFileTimestamps = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
        }
        else
        {
            _prevFileTimestamps = GetAllFilesAndDirectories(_sourcePath);
        }
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        CopyFiles(_prevFileTimestamps.Keys.Select(x => x.ToString()).ToList());
        stopWatch.Stop();
        TimeSpan timeTaken = stopWatch.Elapsed;
        Console.WriteLine($"Synced in {timeTaken.ToString(@"m\:ss\.fff")}");

        _timer = new System.Timers.Timer();
        _timer.Interval = 30 * 1000;
        _timer.AutoReset = true;
        _timer.Enabled = true;
        _timer.Elapsed += OnTimerElapsed;

    }

    public void Watch()
    {
        _watcher.NotifyFilter = NotifyFilters.Attributes
                            | NotifyFilters.CreationTime
                            | NotifyFilters.DirectoryName
                            | NotifyFilters.FileName
                            | NotifyFilters.LastAccess
                            | NotifyFilters.LastWrite
                            | NotifyFilters.Security
                            | NotifyFilters.Size;
        _watcher.Changed += OnChanged;
        _watcher.Created += OnCreated;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
        _watcher.EnableRaisingEvents = true;
        _watcher.IncludeSubdirectories = true;
    }

    private static async void OnChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation(@$"{e.Name} changed");

        if (e.ChangeType != WatcherChangeTypes.Changed)
        {
            return;
        }

        string file = e.FullPath;
        _changedFileTimestamps[file] = DateTime.Now;
        await SaveFileTimestampsToJsonFile();
    }

    private static async void OnCreated(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation(@$"{e.Name} created");
        string file = e.FullPath;
        _changedFileTimestamps[file] = File.GetLastWriteTime(file);
        await SaveFileTimestampsToJsonFile();
    }

    private static async void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation($"Deleted: {e.FullPath}");
        foreach (var (file, lastModified) in _changedFileTimestamps)
        {
            if (file.Contains(e.FullPath))
            {
                _changedFileTimestamps.Remove(file);
            }
        }
        await SaveFileTimestampsToJsonFile();
    }

    private static async void OnRenamed(object sender, RenamedEventArgs e)
    {
        _logger.LogInformation($"Renamed:");
        _logger.LogInformation($"    Old: {e.OldFullPath}");
        _logger.LogInformation($"    New: {e.FullPath}");
        if (!_changedFileTimestamps.ContainsKey(e.OldFullPath))
        {
            _changedFileTimestamps[e.FullPath] = File.GetLastWriteTime(e.FullPath);
        }
        else
        {
            DateTime oldModified = _changedFileTimestamps[e.OldFullPath];
            _changedFileTimestamps.Remove(e.OldFullPath);
            _changedFileTimestamps[e.FullPath] = oldModified;
        }
        await SaveFileTimestampsToJsonFile();
    }

    private static void OnError(object sender, ErrorEventArgs e) =>
        PrintException(e.GetException());

    private static void PrintException(Exception? ex)
    {
        if (ex != null)
        {
            _logger.LogInformation($"Message: {ex.Message}");
            _logger.LogInformation("Stacktrace:");
            _logger.LogInformation(ex.StackTrace);
            Console.WriteLine();
            PrintException(ex.InnerException);
        }
    }

    private static async void OnTimerElapsed(object sender, ElapsedEventArgs e)
    {
        Console.WriteLine("Checking for changed files...");
        int cpuUsage = ComputerUsageMonitor.GetCpuUsage();
        int diskUsage = ComputerUsageMonitor.GetPhysicalDisk();
        Console.WriteLine($"CPU: {cpuUsage}, Disk: {diskUsage}");

        while (cpuUsage >= CPU_THRESHOLD || diskUsage >= DISK_THRESHOLD)
        {
            Thread.Sleep(10000);
            cpuUsage = ComputerUsageMonitor.GetCpuUsage();
            diskUsage = ComputerUsageMonitor.GetPhysicalDisk();
        }

        var stopWatch = new Stopwatch();
        stopWatch.Start();
        Console.WriteLine($"CPU: {cpuUsage}, Disk: {diskUsage}");
        List<string> updatedFiles = new List<string>();

        /*foreach (var (file, lastModified) in _prevFileTimestamps)
        {
            Console.WriteLine("_prev_ " + file + lastModified.ToString());
        }
        foreach (var (file, lastModified) in _changedFileTimestamps)
        {
            Console.WriteLine("_changed_ " + file + lastModified.ToString());
        }*/

        foreach (var (file, lastModified) in _changedFileTimestamps)
        {
            if (!_prevFileTimestamps.ContainsKey(file))
            {
                updatedFiles.Add(file);
            }
            else if (_prevFileTimestamps.ContainsKey(file) && _prevFileTimestamps[file] < lastModified)
            {
                updatedFiles.Add(file);
            }
        }

        CopyFiles(updatedFiles);

        List<string> changedFileRelativePathList = new List<string>();
        foreach (string file in _changedFileTimestamps.Keys)
        {
            changedFileRelativePathList.Add(file.Substring(_sourcePath.Length + 1));
        }

        DeleteFiles(changedFileRelativePathList, GetAllFilesAndDirectories(_backupPath).Keys.ToList());

        if (updatedFiles.Count > 0)
        {
            _prevFileTimestamps = _changedFileTimestamps.ToDictionary(e => e.Key, e => e.Value);
        }
        await SaveFileTimestampsToJsonFile();

        stopWatch.Stop();
        TimeSpan timeTaken = stopWatch.Elapsed;
        Console.WriteLine($"Synced in {timeTaken.ToString(@"m\:ss\.fff")}");
    }

    private static async void CopyFiles(List<string> files)
    {
        foreach (string file in files)
        {
            string relativePath = file.Substring(_sourcePath.Length + 1);
            string destinationFilePath = Path.Combine(_backupPath, relativePath);
            string destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath);
            if (!Directory.Exists(destinationDirectoryPath))
            {
                Directory.CreateDirectory(destinationDirectoryPath);
            }
            if (File.Exists(file) && !File.GetAttributes(file).HasFlag(FileAttributes.Directory))
            {
                File.Copy(file, destinationFilePath, true);
            } else if (File.Exists(file) && File.GetAttributes(file).HasFlag(FileAttributes.Directory) && Directory.Exists(file) && !Directory.Exists(destinationFilePath))
            {
                Directory.CreateDirectory(destinationFilePath);
            } else
            {
                if (File.Exists(destinationFilePath)) {
                    DeleteFiles(new List<string>(), new List<string> { destinationFilePath });
                }
            }
            _changedFileTimestamps[file] = File.GetLastWriteTime(file);
        }
        await SaveFileTimestampsToJsonFile();
    }

    private static void DeleteFiles(List<string> sourceFileRelativePaths, List<string> destinationFileNames)
    {
        foreach (string file in destinationFileNames)
        {
            string backedUpFileRelativePath = file.Substring(_backupPath.Length + 1);
            if (!sourceFileRelativePaths.Contains(backedUpFileRelativePath))
            {
                if (!File.GetAttributes(file).HasFlag(FileAttributes.Directory))
                {
                    File.Delete(file);
                }
                else
                {
                    Directory.Delete(file, true);
                }
            }
        }
    }

    private static Dictionary<string, DateTime> GetAllFilesAndDirectories(string rootPath)
    {
        Dictionary<string, DateTime> resultDict = new Dictionary<string, DateTime>();
        foreach (string file in Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            resultDict[file] = File.GetLastWriteTime(file);
        }
        foreach (string directory in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories))
        {
            resultDict[directory] = File.GetLastWriteTime(directory);
        }
        return resultDict;
    }

    private static async Task SaveFileTimestampsToJsonFile()
    {
        string jsonOutput = JsonSerializer.Serialize(_changedFileTimestamps);
        byte[] jsonBuffer = Encoding.UTF8.GetBytes(jsonOutput);
        try
        {
            // await File.WriteAllTextAsync(_timestampFile, jsonOutput);
            using (FileStream fs = new FileStream(_timestampFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                fs.SetLength(0);
                await fs.WriteAsync(jsonBuffer, 0, jsonBuffer.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Message: {ex.Message}");
        }
    }
}