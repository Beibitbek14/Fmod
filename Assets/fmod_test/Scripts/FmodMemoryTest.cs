using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FMOD.Studio;

#if UNITY_EDITOR
using Unity.Profiling.Memory;
#endif

/// <summary>
/// Тестовый стенд для снятия снапшотов MemoryProfiler в связке с FMOD.
/// sfxEventPaths заполняется автоматически из банков UI и SFX (папки event:/UI/... и event:/SFX/...)
/// musicEventPath — из Music.bank (папка event:/Music/...)
/// ambienceEventPath — из Ambience.bank (папка event:/Ambience/...)
/// </summary>
public class FmodMemoryTest : MonoBehaviour
{
    [Header("FMOD Events (заполняются автоматически)")]
    [Tooltip("Пути к SFX/UI событиям — заполняются из банков SFX и UI")]
    public string[] sfxEventPaths;

    [Tooltip("Путь к длинному треку — первый найденный в Music.bank/Music/")]
    public string musicEventPath;

    [Tooltip("Путь к ambience-треку — первый найденный в Ambience.bank/Ambience/")]
    public string ambienceEventPath;

    [Header("UI Buttons")]
    public Button btnSnapshot1;     // До инициализации
    public Button btnInitFmod;      // Инициализировать FMOD + снапшот 2
    public Button btnPlaySfx;       // Проиграть все SFX + снапшот 3
    public Button btnPlayMusic;     // Старт трека + снапшот 4
    public Button btnStopMusic;     // Стоп трека
    public Button btnPlayAmbience;  // Старт ambience + снапшот 5
    public Button btnStopAmbience;  // Стоп ambience

    [Header("Settings")]
    [Tooltip("Сколько раз проиграть каждый SFX перед снапшотом")]
    public int sfxRepeatCount = 3;

    // Названия банков, из которых берём SFX-события
    private static readonly string[] SfxBankNames     = { "SFX", "UI" };
    // Префиксы путей событий, которые считаются SFX
    private static readonly string[] SfxPathPrefixes  = { "event:/SFX/", "event:/UI/" };

    private const string MusicBankName     = "Music";
    private const string MusicPathPrefix   = "event:/Music/";

    private const string AmbienceBankName  = "Ambience";
    private const string AmbiencePathPrefix = "event:/Ambience/";

    private EventInstance _musicInstance;
    private EventInstance _ambienceInstance;
    public bool _fmodReady = false;

    void Awake()
    {
        btnSnapshot1.onClick.AddListener(OnSnapshot1_BeforeInit);
        btnInitFmod.onClick.AddListener(OnInitFmod);
        btnPlaySfx.onClick.AddListener(() => StartCoroutine(OnPlaySfxThenSnapshot()));
        btnPlayMusic.onClick.AddListener(OnPlayMusic);
        btnStopMusic.onClick.AddListener(OnStopMusic);
        btnPlayAmbience.onClick.AddListener(OnPlayAmbience);
        btnStopAmbience.onClick.AddListener(OnStopAmbience);

        btnInitFmod.interactable    = false;
        btnPlaySfx.interactable     = false;
        btnPlayMusic.interactable   = false;
        btnStopMusic.interactable   = false;
        btnPlayAmbience.interactable = false;
        btnStopAmbience.interactable = false;
    }

    // =========================================================================
    // ШАГ 1 — снапшот ДО инициализации FMOD
    // =========================================================================
    void OnSnapshot1_BeforeInit()
    {
        TakeSnapshot("1_BeforeFMOD");
        btnSnapshot1.interactable = false;
        btnInitFmod.interactable  = true;
        Debug.Log("[MemTest] Snapshot 1 taken (before FMOD init)");
    }

    // =========================================================================
    // ШАГ 2 — инициализация FMOD + загрузка банков + снапшот ПОСЛЕ
    // =========================================================================
    void OnInitFmod()
    {
        var _ = FMODUnity.RuntimeManager.StudioSystem; // убедимся, что система поднята

        FMODUnity.RuntimeManager.LoadBank("Master.strings");
        FMODUnity.RuntimeManager.LoadBank("Master");
        FMODUnity.RuntimeManager.LoadBank(MusicBankName);
        FMODUnity.RuntimeManager.LoadBank(AmbienceBankName);
        
        foreach (var bankName in SfxBankNames)
            FMODUnity.RuntimeManager.LoadBank(bankName);

        StartCoroutine(WaitForBanksThenSnapshot());
    }

    // =========================================================================
    // ШАГ 2 — ждём загрузки банков, затем автозаполняем пути и делаем снапшот
    // =========================================================================
    IEnumerator WaitForBanksThenSnapshot()
    {
        while (!FMODUnity.RuntimeManager.HaveAllBanksLoaded)
            yield return null;

        // Настраиваем слушателя
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var unityListener = mainCam.GetComponent<AudioListener>();
            if (unityListener != null) Destroy(unityListener);
            if (mainCam.GetComponent<FMODUnity.StudioListener>() == null)
                mainCam.gameObject.AddComponent<FMODUnity.StudioListener>();
        }

        _fmodReady = true;
        TakeSnapshot("2_AfterFMODInit");
        btnInitFmod.interactable  = false;
        btnPlaySfx.interactable   = sfxEventPaths.Length > 0;
        Debug.Log("[MemTest] Snapshot 2 taken (after FMOD init).");
    }

    // =========================================================================
    // ШАГ 3 — проигрываем N SFX + снапшот
    // =========================================================================
    IEnumerator OnPlaySfxThenSnapshot()
    {
        btnPlaySfx.interactable = false;

        if (sfxEventPaths == null || sfxEventPaths.Length == 0)
        {
            Debug.LogWarning("[MemTest] sfxEventPaths is empty!");
            yield break;
        }

        foreach (var path in sfxEventPaths)
        {
            for (int i = 0; i < sfxRepeatCount; i++)
            {
                var instance = FMODUnity.RuntimeManager.CreateInstance(path);
                instance.start();
                instance.release(); // fire-and-forget
                Debug.Log($"[MemTest] Played SFX: {path} ({i + 1}/{sfxRepeatCount})");
                yield return new WaitForSeconds(0.3f);
            }
        }

        yield return new WaitForSeconds(0.5f);

        TakeSnapshot("3_AfterSFX");
        btnPlayMusic.interactable   = !string.IsNullOrEmpty(musicEventPath);
        btnPlayAmbience.interactable = !string.IsNullOrEmpty(ambienceEventPath);
        Debug.Log("[MemTest] All SFX played. Snapshot 3 taken.");
    }

    // =========================================================================
    // ШАГ 4 — запуск музыки + снапшот
    // =========================================================================
    void OnPlayMusic()
    {
        if (string.IsNullOrEmpty(musicEventPath))
        {
            Debug.LogWarning("[MemTest] musicEventPath is empty!");
            return;
        }

        _musicInstance = FMODUnity.RuntimeManager.CreateInstance(musicEventPath);
        _musicInstance.start();

        btnPlayMusic.interactable = false;
        btnStopMusic.interactable = true;

        StartCoroutine(SnapshotAfterDelay("4_DuringMusic", 1.0f));
        Debug.Log($"[MemTest] Music started: {musicEventPath}");
    }

    void OnStopMusic()
    {
        if (_musicInstance.isValid())
        {
            _musicInstance.stop(STOP_MODE.ALLOWFADEOUT);
            _musicInstance.release();
        }
        btnStopMusic.interactable = false;
        Debug.Log("[MemTest] Music stopped.");
    }

    // =========================================================================
    // ШАГ 5 — запуск Ambience + снапшот
    // =========================================================================
    void OnPlayAmbience()
    {
        if (string.IsNullOrEmpty(ambienceEventPath))
        {
            Debug.LogWarning("[MemTest] ambienceEventPath is empty!");
            return;
        }

        _ambienceInstance = FMODUnity.RuntimeManager.CreateInstance(ambienceEventPath);
        _ambienceInstance.start();

        btnPlayAmbience.interactable = false;
        btnStopAmbience.interactable = true;

        StartCoroutine(SnapshotAfterDelay("5_DuringAmbience", 1.0f));
        Debug.Log($"[MemTest] Ambience started: {ambienceEventPath}");
    }

    void OnStopAmbience()
    {
        if (_ambienceInstance.isValid())
        {
            _ambienceInstance.stop(STOP_MODE.ALLOWFADEOUT);
            _ambienceInstance.release();
        }
        btnStopAmbience.interactable = false;
        Debug.Log("[MemTest] Ambience stopped.");
    }

    // =========================================================================
    // Утилита: снапшот с задержкой
    // =========================================================================
    IEnumerator SnapshotAfterDelay(string label, float delay)
    {
        yield return new WaitForSeconds(delay);
        TakeSnapshot(label);
        Debug.Log($"[MemTest] Snapshot taken: {label}");
    }

    // =========================================================================
    // Утилита: сделать снапшот
    // =========================================================================
    void TakeSnapshot(string label)
    {
#if UNITY_EDITOR
        string fileName = $"{label}.snap";
        string path = System.IO.Path.Combine(Application.dataPath, "../", fileName);
        MemoryProfiler.TakeSnapshot(path, OnSnapshotFinished,
            CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects);
#else
        Debug.Log($"[MemTest] [DEVICE] Snapshot point: {label} — сделайте снапшот вручную в Memory Profiler.");
#endif
    }

#if UNITY_EDITOR
    void OnSnapshotFinished(string path, bool result)
    {
        if (result)
            Debug.Log($"[MemTest] Snapshot saved: {path}");
        else
            Debug.LogError("[MemTest] Snapshot FAILED.");
    }
#endif
    
#if UNITY_EDITOR
    [ContextMenu("Collect FMOD Event Paths")]
    void CollectAllEventPaths()
    {
        var allEvents = FMODUnity.EventManager.Events;
        var sfxList = new List<string>();

        foreach (var ev in allEvents)
        {
            foreach (var prefix in SfxPathPrefixes)
            {
                if (ev.Path.StartsWith(prefix))
                {
                    sfxList.Add(ev.Path);
                    break;
                }
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

        Debug.Log($"[MemTest] SFX paths found: {sfxEventPaths.Length}");
        Debug.Log($"[MemTest] Music path: {musicEventPath}");
        Debug.Log($"[MemTest] Ambience path: {ambienceEventPath}");

        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}