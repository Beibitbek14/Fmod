using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using FMOD.Studio;
using TMPro;

/// <summary>
/// Тестовый стенд для измерения памяти FMOD через нативный API. (Обновлено)
/// Версия FMOD: 2.01+ (Проверено для 2.03)
/// </summary>
public class FmodMemoryTest : MonoBehaviour
{
    [Serializable]
    public struct MemoryReport
    {
        public string label;
        public DateTime timestamp;

        public int lowLevelCurrent;
        public int lowLevelPeak;

        public int studioExclusive;
        public int studioInclusive;
        public int studioSampleData;

        public int loadedBankCount;

        public int musicInclusive;
        public int ambienceInclusive;

        public string ToDisplayString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {label} ===");
            sb.AppendLine($"[Low-level] Current: {MB(lowLevelCurrent)} | Peak: {MB(lowLevelPeak)}");
            sb.AppendLine($"[Studio]    Excl: {MB(studioExclusive)} | Incl: {MB(studioInclusive)} | Samples: {MB(studioSampleData)}");
            sb.AppendLine($"[Banks]     Loaded: {loadedBankCount}");

            if (musicInclusive > 0)
                sb.AppendLine($"[Music inst] Inclusive: {MB(musicInclusive)}");
            if (ambienceInclusive > 0)
                sb.AppendLine($"[Amb inst]   Inclusive: {MB(ambienceInclusive)}");

            return sb.ToString();
        }

        public string ToTsvRow()
        {
            return string.Join("\t",
                label,
                timestamp.ToString("HH:mm:ss.fff"),
                lowLevelCurrent, lowLevelPeak,
                studioExclusive, studioInclusive, studioSampleData,
                loadedBankCount,
                musicInclusive, ambienceInclusive
            );
        }

        public static string TsvHeader =>
            "Label\tTime\tLL_Current\tLL_Peak\t" +
            "Studio_Excl\tStudio_Incl\tStudio_Samples\t" +
            "Banks_Count\tBanks_Samples\t" +
            "Music_Incl\tAmbience_Incl";

        // private static string KB(int bytes) => $"{bytes / 1024} KB";
        private static string MB(int bytes) => $"{bytes / 1048576f} MB";
    }

    [Header("FMOD Events (заполняются автоматически или вручную)")]
    public string[] sfxEventPaths;
    public string musicEventPath;
    public string ambienceEventPath;

    [Header("UI Кнопки")]
    public Button btnMeasure1;
    public Button btnInitFmod;
    public Button btnPlaySfx;
    public Button btnPlayMusic;
    public Button btnStopMusic;
    public Button btnPlayAmbience;
    public Button btnStopAmbience;
    public Button btnMeasureNow;
    public Button btnToggleLive;

    [Header("UI Текст")]
    public Text txtCurrentReport;
    public Text txtDelta;
    public Text txtStatus;

    [Header("Настройки")]
    public int sfxRepeatCount = 3;
    public float liveUpdateInterval = 1.0f;

    private static readonly string[] SfxBankNames    = { "SFX", "UI" };
    private static readonly string[] SfxPathPrefixes = { "event:/SFX/", "event:/UI/" };
    private const string MusicBankName      = "Music";
    private const string MusicPathPrefix    = "event:/Music/";
    private const string AmbienceBankName   = "Ambience";
    private const string AmbiencePathPrefix = "event:/Ambience/";

    private EventInstance _musicInstance;
    private EventInstance _ambienceInstance;
    private bool _fmodReady;
    private bool _liveUpdateActive;

    private readonly List<MemoryReport> _reportHistory = new List<MemoryReport>();
    private string _logFilePath;

    void Awake()
    {
        _logFilePath = Path.Combine(Application.persistentDataPath, "fmod_memory_log.tsv");

        btnMeasure1.onClick.AddListener(OnStep1_BeforeInit);
        btnInitFmod.onClick.AddListener(OnStep2_InitFmod);
        btnPlaySfx.onClick.AddListener(() => StartCoroutine(OnStep3_PlaySfx()));
        btnPlayMusic.onClick.AddListener(OnStep4_PlayMusic);
        btnStopMusic.onClick.AddListener(OnStopMusic);
        btnPlayAmbience.onClick.AddListener(OnStep5_PlayAmbience);
        btnStopAmbience.onClick.AddListener(OnStopAmbience);
        btnMeasureNow.onClick.AddListener(OnManualMeasure);
        btnToggleLive.onClick.AddListener(OnToggleLive);

        SetButtonsInteractable(btnInitFmod, false);
        SetButtonsInteractable(btnPlaySfx, false);
        SetButtonsInteractable(btnPlayMusic, false);
        SetButtonsInteractable(btnStopMusic, false);
        SetButtonsInteractable(btnPlayAmbience, false);
        SetButtonsInteractable(btnStopAmbience, false);

        File.WriteAllText(_logFilePath, MemoryReport.TsvHeader + "\n");
        SetStatus($"Лог: {_logFilePath}");
    }

    void OnStep1_BeforeInit()
    {
        var report = Measure("1_BeforeFMOD");
        ShowReports(report);
        btnMeasure1.interactable = false;
        btnInitFmod.interactable = true;
        SetStatus("Шаг 1 выполнен. Инициализируйте FMOD.");
    }

    void OnStep2_InitFmod()
    {
        btnInitFmod.interactable = false;
        SetStatus("Загрузка банков...");

        var _ = FMODUnity.RuntimeManager.StudioSystem;

        // Примечание: если в настройках FMOD стоит "Load All Banks", эти строки 
        // просто увеличат счетчик ссылок (ref count). Это безопасно для FMOD.
        FMODUnity.RuntimeManager.LoadBank("Master.strings");
        FMODUnity.RuntimeManager.LoadBank("Master");
        FMODUnity.RuntimeManager.LoadBank(MusicBankName);
        FMODUnity.RuntimeManager.LoadBank(AmbienceBankName);
        foreach (var bankName in SfxBankNames)
            FMODUnity.RuntimeManager.LoadBank(bankName);

        StartCoroutine(WaitForBanksAndMeasure());
    }

    IEnumerator WaitForBanksAndMeasure()
    {
        while (!FMODUnity.RuntimeManager.HaveAllBanksLoaded)
            yield return null;

        var mainCam = Camera.main;
        if (mainCam != null)
        {
            var unityListener = mainCam.GetComponent<AudioListener>();
            if (unityListener != null) Destroy(unityListener);
            if (mainCam.GetComponent<FMODUnity.StudioListener>() == null)
                mainCam.gameObject.AddComponent<FMODUnity.StudioListener>();
        }

        _fmodReady = true;
        var report = Measure("2_AfterFMODInit");
        ShowReports(report);
        btnPlaySfx.interactable = sfxEventPaths is { Length: > 0 };
        SetStatus("FMOD инициализирован. Банки загружены.");
    }

    IEnumerator OnStep3_PlaySfx()
    {
        btnPlaySfx.interactable = false;

        if (sfxEventPaths == null || sfxEventPaths.Length == 0)
        {
            SetStatus("sfxEventPaths пуст!");
            yield break;
        }

        SetStatus("Проигрываю SFX...");

        foreach (var path in sfxEventPaths)
        {
            for (int i = 0; i < sfxRepeatCount; i++)
            {
                var inst = FMODUnity.RuntimeManager.CreateInstance(path);
                if (inst.isValid()) // Добавлена проверка валидности
                {
                    inst.start();
                    inst.release();
                }
                else
                {
                    Debug.LogWarning($"[FmodMemTest] Не удалось создать инстанс по пути: {path}");
                }
                yield return new WaitForSeconds(0.3f);
            }
        }

        yield return new WaitForSeconds(0.5f);

        var report = Measure("3_AfterSFX");
        ShowReports(report);
        btnPlayMusic.interactable    = !string.IsNullOrEmpty(musicEventPath);
        btnPlayAmbience.interactable = !string.IsNullOrEmpty(ambienceEventPath);
        SetStatus("SFX проиграны. Замер выполнен.");
    }

    void OnStep4_PlayMusic()
    {
        if (string.IsNullOrEmpty(musicEventPath)) { SetStatus("musicEventPath пуст!"); return; }

        _musicInstance = FMODUnity.RuntimeManager.CreateInstance(musicEventPath);
        if (_musicInstance.isValid())
        {
            _musicInstance.start();
            btnPlayMusic.interactable = false;
            btnStopMusic.interactable = true;
            StartCoroutine(MeasureAfterDelay("4_DuringMusic", 1.0f));
            SetStatus($"Музыка запущена: {musicEventPath}");
        }
    }

    void OnStopMusic()
    {
        if (_musicInstance.isValid())
        {
            _musicInstance.stop(STOP_MODE.ALLOWFADEOUT);
            _musicInstance.release();
        }
        btnStopMusic.interactable = false;
        SetStatus("Музыка остановлена.");
    }

    void OnStep5_PlayAmbience()
    {
        if (string.IsNullOrEmpty(ambienceEventPath)) { SetStatus("ambienceEventPath пуст!"); return; }

        _ambienceInstance = FMODUnity.RuntimeManager.CreateInstance(ambienceEventPath);
        if (_ambienceInstance.isValid())
        {
            _ambienceInstance.start();
            btnPlayAmbience.interactable = false;
            btnStopAmbience.interactable = true;
            StartCoroutine(MeasureAfterDelay("5_DuringMusicAndAmbience", 1.0f));
            SetStatus($"Ambience запущен: {ambienceEventPath}");
        }
    }

    void OnStopAmbience()
    {
        if (_ambienceInstance.isValid())
        {
            _ambienceInstance.stop(STOP_MODE.ALLOWFADEOUT);
            _ambienceInstance.release();
        }
        btnStopAmbience.interactable = false;
        SetStatus("Ambience остановлен.");
    }

    void OnManualMeasure()
    {
        var report = Measure("manual_" + DateTime.Now.ToString("HH:mm:ss"));
        ShowReports(report);
    }

    void OnToggleLive()
    {
        _liveUpdateActive = !_liveUpdateActive;
        if (_liveUpdateActive)
        {
            StartCoroutine(LiveUpdateLoop());
            btnToggleLive.GetComponentInChildren<TextMeshProUGUI>().text = "⏹ Стоп Live";
        }
        else
        {
            btnToggleLive.GetComponentInChildren<TextMeshProUGUI>().text = "▶ Live Update";
        }
    }

    IEnumerator LiveUpdateLoop()
    {
        while (_liveUpdateActive && _fmodReady)
        {
            // Передаем false, чтобы не засорять память историей и диск записью логов каждую секунду
            var report = Measure("live", false); 
            ShowCurrentOnly(report); 
            yield return new WaitForSeconds(liveUpdateInterval);
        }
    }

    // ИСПРАВЛЕНО: Добавлен параметр saveToHistoryAndLog
    MemoryReport Measure(string label, bool saveToHistoryAndLog = true) 
    {
        var report = new MemoryReport
        {
            label     = label,
            timestamp = DateTime.Now,
        };

        FMOD.Memory.GetStats(out report.lowLevelCurrent, out report.lowLevelPeak);

        if (_fmodReady)
        {
            FMODUnity.RuntimeManager.StudioSystem.getMemoryUsage(out var sysUsage);
            report.studioExclusive  = sysUsage.exclusive;
            report.studioInclusive  = sysUsage.inclusive;
            report.studioSampleData = sysUsage.sampledata;

            if (FMODUnity.RuntimeManager.StudioSystem.getBankList(out var banks) == FMOD.RESULT.OK)
            {
                report.loadedBankCount = banks.Length;
            }

            if (_musicInstance.isValid())
            {
                _musicInstance.getMemoryUsage(out var mUsage);
                report.musicInclusive = mUsage.inclusive;
            }
            if (_ambienceInstance.isValid())
            {
                _ambienceInstance.getMemoryUsage(out var aUsage);
                report.ambienceInclusive = aUsage.inclusive;
            }
        }

        // Логика сохранения вынесена под условие
        if (saveToHistoryAndLog)
        {
            _reportHistory.Add(report);
            AppendToLog(report);
            Debug.Log($"[FmodMemTest] {report.ToDisplayString()}");
        }
        
        return report;
    }

    void ShowReports(MemoryReport current)
    {
        if (txtCurrentReport != null)
            txtCurrentReport.text = current.ToDisplayString();

        if (txtDelta != null && _reportHistory.Count >= 2)
        {
            var prev = _reportHistory[^2];
            txtDelta.text = BuildDelta(prev, current);
        }
    }

    void ShowCurrentOnly(MemoryReport current)
    {
        if (txtCurrentReport != null)
            txtCurrentReport.text = current.ToDisplayString();
    }

    string BuildDelta(MemoryReport a, MemoryReport b)
    {
        string Diff(int va, int vb)
        {
            int d = vb - va;
            float dMB = d / 1048576f;
            return d == 0 ? "±0 MB" : (d > 0 ? $"+{dMB:F2} MB" : $"{dMB:F2} MB");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Δ  {a.label}  →  {b.label}");
        sb.AppendLine($"[Low-level] Current: {Diff(a.lowLevelCurrent, b.lowLevelCurrent)} | Peak: {Diff(a.lowLevelPeak, b.lowLevelPeak)}");
        sb.AppendLine($"[Studio]    Incl: {Diff(a.studioInclusive, b.studioInclusive)} | Samples: {Diff(a.studioSampleData, b.studioSampleData)}");
        sb.AppendLine($"[Banks]     Count: {Diff(a.loadedBankCount, b.loadedBankCount)}");
        
        if (a.musicInclusive > 0 || b.musicInclusive > 0)
            sb.AppendLine($"[Music]     Incl: {Diff(a.musicInclusive, b.musicInclusive)}");
        
        if (a.ambienceInclusive > 0 || b.ambienceInclusive > 0)
            sb.AppendLine($"[Ambience]  Incl: {Diff(a.ambienceInclusive, b.ambienceInclusive)}");
        
        return sb.ToString();
    }

    void AppendToLog(MemoryReport report)
    {
        try { File.AppendAllText(_logFilePath, report.ToTsvRow() + "\n"); }
        catch (Exception e) { Debug.LogWarning($"[FmodMemTest] Ошибка записи лога: {e.Message}"); }
    }

    IEnumerator MeasureAfterDelay(string label, float delay)
    {
        yield return new WaitForSeconds(delay);
        var report = Measure(label);
        ShowReports(report);
        Debug.Log($"[FmodMemTest] Замер выполнен: {label}");
    }

    void SetStatus(string msg)
    {
        if (txtStatus != null)
            txtStatus.text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
    }

    static void SetButtonsInteractable(Button btn, bool state) =>
        btn.interactable = state;

#if UNITY_EDITOR
    [ContextMenu("Collect FMOD Event Paths")]
    void CollectAllEventPaths()
    {
        var allEvents = FMODUnity.EventManager.Events;
        var sfxList   = new List<string>();

        foreach (var ev in allEvents)
        {
            foreach (var prefix in SfxPathPrefixes)
            {
                if (ev.Path.StartsWith(prefix)) { sfxList.Add(ev.Path); break; }
            }
        }
        sfxEventPaths = sfxList.ToArray();
        musicEventPath    = string.Empty;
        ambienceEventPath = string.Empty;

        foreach (var ev in allEvents)
        {
            if (string.IsNullOrEmpty(musicEventPath) && ev.Path.StartsWith(MusicPathPrefix))
                musicEventPath = ev.Path;
            if (string.IsNullOrEmpty(ambienceEventPath) && ev.Path.StartsWith(AmbiencePathPrefix))
                ambienceEventPath = ev.Path;
        }

        Debug.Log($"[FmodMemTest] SFX: {sfxEventPaths.Length} | Music: {musicEventPath} | Ambience: {ambienceEventPath}");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}