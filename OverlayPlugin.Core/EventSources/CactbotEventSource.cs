using Advanced_Combat_Tracker;
using Newtonsoft.Json.Linq;
using RainbowMage.OverlayPlugin.MemoryProcessors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Dalamud.Interface.ImGuiFileDialog;

namespace RainbowMage.OverlayPlugin.EventSources;

public class CactbotEventSource : EventSourceBase
{
    public CactbotEventSourceConfig Config { get; private set; }

    public event EventHandler RadarOptionsChanged;

    private const int KFastTimerMilli = 16;
    private const int KSlowTimerMilli = 300;
    private const int KUberSlowTimerMilli = 3000;

    private readonly SemaphoreSlim logLinesSemaphore = new(1);
    private readonly CactbotTtsDuplicateGuard ttsDuplicateGuard = new(
        TimeSpan.FromMilliseconds(500));

    // Not thread-safe, as OnLogLineRead may happen at any time. Use |log_lines_semaphore_| to access it.
    private List<string> logLines = new(40);

    private List<string> importLogLines = new(40);

    // Used on the fast timer to avoid allocating List every time.
    private List<string> lastLogLines = new(40);
    private List<string> lastImportLogLines = new(40);

    // When true, the update function should reset notify state back to defaults.
    private bool resetNotifyState;

    private System.Timers.Timer fastUpdateTimer;

    // Held while the |fast_update_timer_| is running.
    private FFXIVProcess ffxiv;
    private IDalamudGameStateProvider dalamudGameState;

    private string language;
    private string pcLocale;
    private List<FileSystemWatcher> watchers;

    public const string ForceReloadEvent = "onForceReload";
    public const string GameExistsEvent = "onGameExistsEvent";
    public const string GameActiveChangedEvent = "onGameActiveChangedEvent";
    public const string LogEvent = "onLogEvent";
    public const string ImportLogEvent = "onImportLogEvent";
    public const string InCombatChangedEvent = "onInCombatChangedEvent";
    public const string ZoneChangedEvent = "onZoneChangedEvent";
    public const string PlayerDiedEvent = "onPlayerDied";
    public const string PartyWipeEvent = "onPartyWipe";
    public const string PlayerChangedEvent = "onPlayerChangedEvent";
    public const string SendSaveDataEvent = "onSendSaveData";
    public const string DataFilesReadEvent = "onDataFilesRead";
    public const string InitializeOverlayEvent = "onInitializeOverlay";

    public void Wipe()
    {
        DispatchToJs(new JSEvents.PartyWipeEvent());
    }

    public CactbotEventSource(TinyIoCContainer container)
        : base(container)
    {
        Name = "Cactbot";

        RegisterEventTypes(new List<string>()
        {
            "onForceReload",
            "onGameExistsEvent",
            "onGameActiveChangedEvent",
            "onLogEvent",
            "onImportLogEvent",
            "onInCombatChangedEvent",
            "onZoneChangedEvent",
            "onPlayerDied",
            "onPartyWipe",
            "onPlayerChangedEvent",
            "onUserFileChanged",
        });

        // Broadcast onConfigChanged when a cactbotNotifyConfigChanged message occurs.
        RegisterEventHandler("cactbotReloadOverlays", (_) =>
        {
            DispatchToJs(new JSEvents.ForceReloadEvent());
            return null;
        });
        RegisterEventHandler("cactbotLoadUser", FetchUserFiles);
        RegisterEventHandler("cactbotReadDataFiles", FetchDataFiles);
        RegisterEventHandler("cactbotRequestPlayerUpdate", (_) =>
        {
            notifyState.Player = null;
            return null;
        });
        RegisterEventHandler("cactbotRequestState", (_) =>
        {
            resetNotifyState = true;
            return null;
        });
        RegisterEventHandler("cactbotSay", (msg) =>
        {
            var text = msg["text"]?.ToString() ?? string.Empty;
            LogInfo("cactbotSay: {0}", text);
            if (!ttsDuplicateGuard.TryAccept(text, Stopwatch.GetTimestamp()))
            {
                LogInfo("Suppressed duplicate cactbotSay from another overlay window: {0}", text);
                return null;
            }
            ActGlobals.oFormActMain.TTS(text);
            return null;
        });
        RegisterEventHandler("cactbotSaveData", (msg) =>
        {
            var previousDirectory = Config.UserConfigFile;
            var overlayName = msg["overlay"]?.ToString() ?? string.Empty;
            var data = msg["data"] ?? JValue.CreateNull();
            Config.OverlayData.TryGetValue(overlayName, out var previousData);
            var radarOptionsChanged = HaveRadarOptionsChanged(
                overlayName,
                previousData,
                data);
            Config.OverlayData[overlayName] = data;
            Config.OnUpdateConfig();
            if (Config.WatchFileChanges && previousDirectory != Config.UserConfigFile)
            {
                StopFileWatcher();
                StartFileWatcher();
            }
            if (radarOptionsChanged)
                RadarOptionsChanged?.Invoke(this, EventArgs.Empty);
            return null;
        });
        RegisterEventHandler("cactbotLoadData", (msg) =>
        {
            if (Config.OverlayData.ContainsKey(msg["overlay"].ToString()))
            {
                var ret = new JObject
                {
                    ["data"] = Config.OverlayData[msg["overlay"].ToString()]
                };
                return ret;
            }
            else
            {
                return null;
            }
        });
        RegisterEventHandler("cactbotChooseDirectory", (_) =>
        {
            var ret = new JObject();
            var data = ChooseDirectory();
            if (data != null)
                ret["data"] = data;
            return ret;
        });
    }

    internal static bool HaveRadarOptionsChanged(
        string overlayName,
        JToken? previousData,
        JToken nextData)
    {
        if (!string.Equals(overlayName, "options", StringComparison.Ordinal))
            return false;

        // The settings page saves the complete options document. Only Radar owns a
        // startup-only TTS snapshot, so unrelated option edits must not reload it.
        return !JToken.DeepEquals(previousData?["radar"], nextData?["radar"]);
    }

    private void Log(LogLevel level, string msg)
    {
        logger.Log(level, "Cactbot: " + msg);
    }

    private string ChooseDirectory()
    {
        if (container.TryResolve<ICactbotDirectoryPicker>(out var picker))
            return picker.ChooseDirectory(Config.UserConfigFile ?? string.Empty);

        var fileDialogManager = container.Resolve<FileDialogManager>();
        var semaphore = new SemaphoreSlim(0);
        string chosenFolder = null;
        fileDialogManager.OpenFolderDialog("Pick a cactbot user folder", (success, path) =>
        {
            if (!success)
            {
                semaphore.Release();
                return;
            }

            chosenFolder = path;
            semaphore.Release();
        });
        semaphore.Wait();
        return chosenFolder;
    }

    public override void LoadConfig(IPluginConfig config)
    {
        Config = CactbotEventSourceConfig.LoadConfig(config, logger);
        Config.OverlayData ??= new Dictionary<string, JToken>();
        if (EnsureDefaultAlertOutput(Config.OverlayData))
        {
            Config.SaveConfig(config);
            config.Save();
            LogInfo("Normalized Cactbot's default raidboss output and player label.");
        }
    }

    internal static bool EnsureDefaultAlertOutput(Dictionary<string, JToken> overlayData)
    {
        var changed = false;
        if (!overlayData.TryGetValue("options", out var optionsToken) ||
            optionsToken is not JObject options)
        {
            options = new JObject();
            overlayData["options"] = options;
            changed = true;
        }

        if (options["raidboss"] is not JObject raidboss)
        {
            raidboss = new JObject();
            options["raidboss"] = raidboss;
            changed = true;
        }

        if (raidboss["DefaultAlertOutput"] is null)
        {
            // SpokenAlertsEnabled is a derived runtime field, not a persisted cactbot option.
            // v0.2.23-v0.2.24 wrote it directly, so migrate that value to the option consumed
            // by raidboss_config.ts while preserving an explicit false value.
            var legacySpokenAlerts = raidboss["SpokenAlertsEnabled"]?.Type == JTokenType.Boolean
                ? raidboss["SpokenAlertsEnabled"]!.Value<bool>()
                : (bool?)null;
            raidboss["DefaultAlertOutput"] = legacySpokenAlerts == false
                ? "textAndSound"
                : "ttsAndText";
            changed = true;
        }

        if (raidboss["DefaultPlayerLabel"] is null)
        {
            raidboss["DefaultPlayerLabel"] = "jobFull";
            changed = true;
        }

        changed |= raidboss.Remove("SpokenAlertsEnabled");
        return changed;
    }

    public override void SaveConfig(IPluginConfig config)
    {
        Config.SaveConfig(config);
    }

    public override void Start()
    {
        var ffxivRepository = container.Resolve<FFXIVRepository>();
        if (!ffxivRepository.IsFFXIVPluginPresent())
        {
            Log(LogLevel.Error, "FFXIV plugin not found. Not initializing.");
            return;
        }

        // Our own timer with a higher frequency than OverlayPlugin since we want to see
        // the effect of log messages quickly.
        // TODO: Cleanup; Log messages are distributed through events and skip the update
        //   loop. Memory scanning needs a high frequency but that's handled by the
        //   MemoryProcessor classes which raise events.
        //   Everything else should be handled through events to avoid unnecessary polling.
        //   -- ngld
        fastUpdateTimer = new System.Timers.Timer();
        fastUpdateTimer.Elapsed += (_, _) =>
        {
            var timerInterval = KSlowTimerMilli;
            try
            {
                timerInterval = SendFastRateEvents();
            }
            catch (Exception e)
            {
                // SendFastRateEvents holds this semaphore until it exits.
                LogError("Exception in SendFastRateEvents: " + e.Message);
                LogError("Stack: " + e.StackTrace);
                LogError("Source: " + e.Source);
            }

            fastUpdateTimer.Interval = timerInterval;
        };
        fastUpdateTimer.AutoReset = false;

        language = ffxivRepository.GetLocaleString();
        pcLocale = System.Globalization.CultureInfo.CurrentUICulture.Name;
        
        var actVersion = typeof(ActGlobals).Assembly.GetName().Version!;

        // Print out version strings and locations to help users debug.
        LogInfo("OverlayPlugin Version: {0}", ffxivRepository.GetOverlayPluginVersion().ToString());
        LogInfo("FFXIV Plugin Version: {0}", ffxivRepository.GetPluginVersion().ToString());
        LogInfo("ACT Version: {0}", actVersion.ToString());

        LogInfo("Parsing Plugin Language: {0}", language ?? "(unknown)");

        LogInfo("System Locale: {0}", pcLocale ?? "(unknown)");

        container.TryResolve(out dalamudGameState);
        if (dalamudGameState != null)
        {
            LogInfo("Using Dalamud player, party, and combat state.");
        }
        else switch (language)
        {
            case "cn":
                this.ffxiv = new FFXIVProcessCn(container);
                LogInfo("Version: cn");
                break;
            case "ko":
                this.ffxiv = new FFXIVProcessKo(container);
                LogInfo("Version: ko");
                break;
            default:
                this.ffxiv = new FFXIVProcessIntl(container);
                LogInfo("Version: intl");
                break;
        }

        // Incoming events.
        ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;

        fastUpdateTimer.Interval = KFastTimerMilli;
        fastUpdateTimer.Start();

        // Start watching files after the update check.
        Config.WatchFileChangesChanged += (_, _) =>
        {
            if (Config.WatchFileChanges)
            {
                StartFileWatcher();
            }
            else
            {
                StopFileWatcher();
            }
        };

        if (Config.WatchFileChanges)
        {
            StartFileWatcher();
        }
    }

    public override void Stop()
    {
        fastUpdateTimer?.Stop();

        var formActMain = ActGlobals.oFormActMain;
        
        if (formActMain is not null)
            formActMain.OnLogLineRead -= OnLogLineRead;
    }

    public override void Dispose()
    {
        fastUpdateTimer?.Dispose();
        base.Dispose();
    }

    protected override void Update()
    {
        // Nothing to do since this is handled in SendFastRateEvents.
    }

    private void OnLogLineRead(bool isImport, LogLineEventArgs args)
    {
        logLinesSemaphore.Wait();
        if (isImport)
            importLogLines.Add(args.logLine);
        else
            logLines.Add(args.logLine);
        logLinesSemaphore.Release();
    }

    // Sends an event called |event_name| to javascript, with an event.detail that contains
    // the fields and values of the |detail| structure.
    public void DispatchToJs(JSEvent detail)
    {
        var ev = new JObject
        {
            ["type"] = detail.EventName(),
            ["detail"] = JObject.FromObject(detail)
        };
        DispatchEvent(ev);
    }

    // Events that we want to update as soon as possible.  Return next time this should be called.
    private int SendFastRateEvents()
    {
        if (resetNotifyState)
            notifyState = new NotifyState();
        resetNotifyState = false;

        var dalamudSnapshot = dalamudGameState?.Snapshot;
        var gameExists = dalamudSnapshot?.GameExists ?? ffxiv.FindProcess();
        if (gameExists != notifyState.GameExists)
        {
            notifyState.GameExists = gameExists;
            DispatchToJs(new JSEvents.GameExistsEvent(gameExists));
        }

        var gameActive = dalamudSnapshot?.GameActive ?? ffxiv.IsActive();
        if (gameActive != notifyState.GameActive)
        {
            notifyState.GameActive = gameActive;
            DispatchToJs(new JSEvents.GameActiveChangedEvent(gameActive));
        }

        // Silently stop sending other messages if the ffxiv process isn't around.
        if (!gameExists)
        {
            return KUberSlowTimerMilli;
        }

        // onInCombatChangedEvent: Fires when entering or leaving combat.
        var inActCombat = ActGlobals.oFormActMain.InCombat;
        var inGameCombat = dalamudSnapshot?.InGameCombat ?? ffxiv.GetInGameCombat();
        if (!notifyState.InActCombat.HasValue || inActCombat != notifyState.InActCombat ||
            !notifyState.InGameCombat.HasValue || inGameCombat != notifyState.InGameCombat)
        {
            notifyState.InActCombat = inActCombat;
            notifyState.InGameCombat = inGameCombat;
            DispatchToJs(new JSEvents.InCombatChangedEvent(inActCombat, inGameCombat));
        }

        // onZoneChangedEvent: Fires when the player changes their current zone.
        var zoneName = ActGlobals.oFormActMain.CurrentZone;
        if (notifyState.ZoneName == null || !zoneName.Equals(notifyState.ZoneName))
        {
            notifyState.ZoneName = zoneName;
            DispatchToJs(new JSEvents.ZoneChangedEvent(zoneName));
        }

        // The |player| can be null, such as during a zone change.
        var player = dalamudGameState == null
            ? ffxiv.GetSelfData()
            : dalamudSnapshot?.Player == null
                ? null
                : ToEntityData(dalamudSnapshot.Player);

        // onPlayerDiedEvent: Fires when the player dies. All buffs/debuffs are
        // lost.
        if (player != null)
        {
            var dead = player.hp == 0;
            if (dead != notifyState.Dead)
            {
                notifyState.Dead = dead;
                if (dead)
                    DispatchToJs(new JSEvents.PlayerDiedEvent());
            }
        }

        // onPlayerChangedEvent: Fires when current player data changes.
        if (player != null)
        {
            var send = false;
            if (!player.Equals(notifyState.Player))
            {
                notifyState.Player = player;
                send = true;
            }

            var job = dalamudGameState == null
                ? ffxiv.GetJobSpecificData(player.job)
                : null;
            if (job != null)
            {
                if (send || !JToken.DeepEquals(job, notifyState.JobData))
                {
                    notifyState.JobData = job;
                    var ev = new JSEvents.PlayerChangedEvent(player)
                    {
                        jobDetail = job
                    };
                    DispatchToJs(ev);
                }
            }
            else if (send)
            {
                // No job-specific data.
                DispatchToJs(new JSEvents.PlayerChangedEvent(player));
            }
        }

        // onLogEvent: Fires when new combat log events from FFXIV are available. This fires after any
        // more specific events, some of which may involve parsing the logs as well.
        logLinesSemaphore.Wait();
        var logs = logLines;
        logLines = lastLogLines;
        var importLogs = importLogLines;
        importLogLines = lastImportLogLines;

        logLinesSemaphore.Release();

        if (logs.Count > 0)
        {
            DispatchToJs(new JSEvents.LogEvent(logs));
            logs.Clear();
        }

        lastLogLines = logs;
        lastImportLogLines = importLogs;

        return gameActive ? KFastTimerMilli : KSlowTimerMilli;
    }

    private static FFXIVProcess.EntityData ToEntityData(DalamudPartyMember player)
    {
        return new FFXIVProcess.EntityData
        {
            id = player.EntityId,
            type = FFXIVProcess.EntityType.PC,
            name = player.Name,
            job = (FFXIVProcess.EntityJob)player.JobId,
            level = player.Level,
            hp = unchecked((int)player.CurrentHp),
            max_hp = unchecked((int)player.MaxHp),
            mp = player.CurrentMp,
            max_mp = player.MaxMp,
            pos_x = player.PositionX,
            pos_y = player.PositionY,
            pos_z = player.PositionZ,
            rotation = player.Rotation,
            debug_job = ((FFXIVProcess.EntityJob)player.JobId).ToString(),
        };
    }

    // ILogger implementation.
    public void LogDebug(string format, params object[] args)
    {
        this.Log(LogLevel.Debug, format, args);
    }

    public void LogError(string format, params object[] args)
    {
        this.Log(LogLevel.Error, format, args);
    }

    public void LogWarning(string format, params object[] args)
    {
        this.Log(LogLevel.Warning, format, args);
    }

    public void LogInfo(string format, params object[] args)
    {
        this.Log(LogLevel.Info, format, args);
    }

    private Dictionary<string, string> GetDataFiles(string url)
    {
        // Uri is not smart enough to strip the query args here, so we'll do it manually?
        var idx = url.IndexOf('?');
        if (idx > 0)
            url = url[..idx];

        // If file is a remote pointer, load that file explicitly so that the manifest
        // is relative to the pointed to url and not the local file.
        if (url.StartsWith("file:///"))
        {
            var html = File.ReadAllText(new Uri(url).LocalPath);
            var match = System.Text.RegularExpressions.Regex.Match(
                html, @"<meta http-equiv=""refresh"" content=""0; url=(.*)?""\/?>");
            if (match.Groups.Count > 1)
            {
                url = match.Groups[1].Value;
            }
        }

        // TODO: Reimplement
        // return new Dictionary<string, string>();

        var client = container.Resolve<HttpClient>();

        var dataFilePaths = new List<string>();
        try
        {
            var dataDirManifest = new Uri(new Uri(url), "data/manifest.txt");
            var manifestReader = new StringReader(client.GetStringAsync(dataDirManifest).Result);
            for (var line = manifestReader.ReadLine(); line != null; line = manifestReader.ReadLine())
            {
                line = line.Trim();
                if (line.Length > 0)
                    dataFilePaths.Add(line);
            }
        }
        catch (System.Net.WebException e)
        {
            if (e.Status == System.Net.WebExceptionStatus.ProtocolError &&
                e.Response is System.Net.HttpWebResponse &&
                ((System.Net.HttpWebResponse)e.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Ignore file not found.
            }
            else if (e.InnerException != null &&
                     (e.InnerException is FileNotFoundException || e.InnerException is DirectoryNotFoundException))
            {
                // Ignore file not found.
            }
            else if (e.InnerException != null && e.InnerException.InnerException != null &&
                     (e.InnerException.InnerException is FileNotFoundException ||
                      e.InnerException.InnerException is DirectoryNotFoundException))
            {
                // Ignore file not found.
            }
            else
            {
                LogError("Unable to read manifest file: " + e.Message);
            }
        }
        catch (Exception e)
        {
            LogError("Unable to read manifest file: " + e.Message);
        }

        if (dataFilePaths.Count > 0)
        {
            var fileData = new Dictionary<string, string>();
            foreach (var dataFilename in dataFilePaths)
            {
                try
                {
                    var filePath = new Uri(new Uri(url), "data/" + dataFilename);
                    fileData[dataFilename] = client.GetStringAsync(filePath).Result;
                    LogInfo("Read file " + dataFilename);
                }
                catch (Exception e)
                {
                    LogError("Unable to read data file: " + e.Message);
                }
            }

            return fileData;
        }

        return null;
    }

    private JObject FetchDataFiles(JObject msg)
    {
        var result = GetDataFiles(msg["source"].ToString());

        var output = new JObject
        {
            ["detail"] = new JObject
            {
                ["files"] = result == null ? null : JObject.FromObject(result)
            }
        };

        return output;
    }

    private Dictionary<string, string> GetLocalUserFiles(string configDir, string overlayName)
    {
        if (string.IsNullOrEmpty(configDir))
            return null;

        // TODO: It's not great to have to load every js and css file in the user dir.
        // But most of the time they'll be short and there won't be many.  JS
        // could attempt to send an overlay name to C# code (and race with the
        // document ready event), but that's probably overkill.
        var userFiles = new Dictionary<string, string>();
        string topDir;
        string subDir = null;
        try
        {
            topDir = new Uri(configDir).LocalPath;
        }
        catch (UriFormatException)
        {
            // This can happen e.g. "http://localhost:8000".  Thanks, Uri constructor.  /o\
            return null;
        }

        // It's important to return null here vs an empty dictionary.  null here
        // indicates to attempt to load the user overloads indirectly via the path.
        // This is how remote user directories work.
        try
        {
            if (!Directory.Exists(topDir))
            {
                return null;
            }

            if (overlayName != null)
            {
                subDir = Path.Combine(topDir, overlayName);
                if (!Directory.Exists(subDir))
                    subDir = null;
            }
        }
        catch (Exception e)
        {
            LogError("Error checking directory: {0}", e.ToString());
            return null;
        }

        try
        {
            overlayName ??= "*";
            var filenames = Directory.EnumerateFiles(topDir, $"{overlayName}.js").Concat(
                Directory.EnumerateFiles(topDir, $"{overlayName}.css"));
            if (subDir != null)
            {
                filenames = filenames.Concat(
                    Directory.EnumerateFiles(subDir, "*.js", SearchOption.AllDirectories)).Concat(
                    Directory.EnumerateFiles(subDir, "*.css", SearchOption.AllDirectories));
            }

            foreach (var filename in filenames)
            {
                //if (filename.Contains("-example."))
                //    continue;
                userFiles[Path.GetRelativePath(topDir, filename)] = File.ReadAllText(filename) +
                                                               $"\n//# sourceURL={filename}";
            }

            var textFilenames = Directory.EnumerateFiles(topDir, "*.txt");
            if (subDir != null)
            {
                textFilenames =
                    textFilenames.Concat(Directory.EnumerateFiles(subDir, "*.txt", SearchOption.AllDirectories));
            }

            foreach (var filename in textFilenames)
            {
                userFiles[Path.GetRelativePath(topDir, filename)] = File.ReadAllText(filename);
            }
        }
        catch (Exception e)
        {
            LogError("User error file exception: {0}", e.ToString());
        }

        return userFiles;
    }


    private void GetUserConfigDirAndFiles(string overlayName, out string configDir, out Dictionary<string, string> localFiles)
    {
        localFiles = null;
        configDir = null;

        if (!string.IsNullOrEmpty(Config.UserConfigFile))
        {
            // Explicit user config directory specified.
            configDir = Config.UserConfigFile;
            localFiles = GetLocalUserFiles(configDir, overlayName);
            return;
        }
        try
        {
            configDir = Path.Combine(container.Resolve<PluginMain>().ConfigPath, "cactbot_user");
            Directory.CreateDirectory(configDir);
            localFiles = GetLocalUserFiles(configDir, overlayName);
        }
        catch (Exception e)
        {
            LogError("Error creating cactbot_user dir: {0}: {1}", configDir, e.ToString());
            configDir = null;
            localFiles = null;
        }
    }

    private JObject FetchUserFiles(JObject msg)
    {
        var overlayName = msg.ContainsKey("overlayName") ? msg["overlayName"].ToString() : null;
        GetUserConfigDirAndFiles(overlayName, out var configDir, out var userFiles);

        var response = new JObject
        {
            ["detail"] = new JObject
            {
                ["userLocation"] = configDir,
                ["localUserFiles"] = userFiles == null ? null : JObject.FromObject(userFiles),
                ["parserLanguage"] = language,
                ["systemLocale"] = pcLocale,
                ["displayLanguage"] = Config.DisplayLanguage,
                // For backwards compatibility:
                ["language"] = language
            }
        };
        return response;
    }

    private void StartFileWatcher()
    {
        watchers = new List<FileSystemWatcher>();
        var paths = new List<string> { string.IsNullOrEmpty(Config.UserConfigFile)
            ? Path.Combine(container.Resolve<PluginMain>().ConfigPath, "cactbot_user")
            : Config.UserConfigFile };

        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            string watchDir;
            try
            {
                // Get canonical url for paths so that Directory.Exists will work properly.
                watchDir = Path.GetFullPath(new Uri(path).LocalPath);
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(watchDir))
                continue;

            var watcher = new FileSystemWatcher()
            {
                Path = watchDir,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = true,
            };

            // We only care about file changes. New or renamed files don't matter if we don't have a reference to them
            // and adding a new reference causes an existing file to change.
            watcher.Changed += (_, e) =>
            {
                DispatchEvent(JObject.FromObject(new
                {
                    type = "onUserFileChanged",
                    file = e.FullPath,
                }));
            };

            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);

            LogInfo("Started watching {0}", watchDir);
        }
    }

    private void StopFileWatcher()
    {
        if (watchers == null) return;
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        watchers = null;
    }

    // State that is tracked and sent to JS when it changes.
    private class NotifyState
    {
        public bool AddedDomContentListener = false;
        public bool DomContentLoaded = false;
        public bool SentDataDir = false;
        public bool GameExists;
        public bool GameActive;
        public bool? InActCombat;
        public bool? InGameCombat;
        public bool Dead;
        public string ZoneName;
        public JObject JobData = new();
        public FFXIVProcess.EntityData Player;
    }

    private NotifyState notifyState = new();
}

internal sealed class CactbotTtsDuplicateGuard
{
    private readonly object sync = new();
    private readonly long duplicateWindowTicks;
    private string lastText = string.Empty;
    private long lastTimestamp;
    private bool hasLastText;

    public CactbotTtsDuplicateGuard(TimeSpan duplicateWindow)
    {
        duplicateWindowTicks = Math.Max(
            0,
            (long)(duplicateWindow.TotalSeconds * Stopwatch.Frequency));
    }

    public bool TryAccept(string text, long timestamp)
    {
        lock (sync)
        {
            var elapsed = timestamp - lastTimestamp;
            if (hasLastText &&
                string.Equals(text, lastText, StringComparison.Ordinal) &&
                elapsed is >= 0 &&
                elapsed <= duplicateWindowTicks)
            {
                return false;
            }

            lastText = text;
            lastTimestamp = timestamp;
            hasLastText = true;
            return true;
        }
    }
}
