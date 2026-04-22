using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using FMOD.Studio;

#if UNITY_EDITOR
using Unity.Profiling.Memory;
#endif

/// <summary>
/// Тестовый стенд для снятия снапшотов MemoryProfiler в связке с FMOD.
/// Снапшоты делаются в 4 точках:
///   1. До инициализации FMOD
///   2. После инициализации FMOD
///   3. После проигрывания N SFX-эффектов
///   4. Во время воспроизведения долгого трека
/// </summary>
public class FmodMemoryTest : MonoBehaviour
{
    [Header("FMOD Events")]
    [Tooltip("Пути к SFX событиям, например: event:/SFX/Explosion")]
    public string[] sfxEventPaths;

    [Tooltip("Путь к длинному треку, например: event:/Music/MainTheme")]
    public string musicEventPath;

    [Header("UI Buttons")]
    public Button btnSnapshot1;   // До инициализации
    public Button btnInitFmod;    // Инициализировать FMOD + снапшот 2
    public Button btnPlaySfx;     // Проиграть все SFX + снапшот 3
    public Button btnPlayMusic;   // Старт трека + снапшот 4
    public Button btnStopMusic;   // Стоп трека

    [Header("Settings")]
    [Tooltip("Сколько раз проиграть каждый SFX перед снапшотом")]
    public int sfxRepeatCount = 3;

    private EventInstance _musicInstance;
    public bool _fmodReady = false;

    void Awake()
    {
        btnSnapshot1.onClick.AddListener(OnSnapshot1_BeforeInit);
        btnInitFmod.onClick.AddListener(OnInitFmod);
        btnPlaySfx.onClick.AddListener(() => StartCoroutine(OnPlaySfxThenSnapshot()));
        btnPlayMusic.onClick.AddListener(OnPlayMusic);
        btnStopMusic.onClick.AddListener(OnStopMusic);

        btnInitFmod.interactable  = false;
        btnPlaySfx.interactable   = false;
        btnPlayMusic.interactable = false;
        btnStopMusic.interactable = false;
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
    // ШАГ 2 — инициализация FMOD + снапшот ПОСЛЕ
    // =========================================================================
    void OnInitFmod()
    {
        var rm = FMODUnity.RuntimeManager.StudioSystem;
        
        // основные банки
        FMODUnity.RuntimeManager.LoadBank("Master.strings");
        FMODUnity.RuntimeManager.LoadBank("Master");
        
        // кастомные банки
        FMODUnity.RuntimeManager.LoadBank("System");
        FMODUnity.RuntimeManager.LoadBank("Player");
        FMODUnity.RuntimeManager.LoadBank("Music");

        StartCoroutine(WaitForBanksThenSnapshot());
    }

    // =========================================================================
    // ШАГ 2 — ждём загрузки банков
    // =========================================================================
    IEnumerator WaitForBanksThenSnapshot()
    {
        while (!FMODUnity.RuntimeManager.HaveAllBanksLoaded)
            yield return null;
        
        // while (FMODUnity.RuntimeManager.AnySampleDataLoading())
        //     yield return null;

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var unityListener = mainCam.GetComponent<AudioListener>();
            if (unityListener != null) Destroy(unityListener);
            mainCam.gameObject.AddComponent<FMODUnity.StudioListener>();
        }

        _fmodReady = true;
        TakeSnapshot("2_AfterFMODInit");
        btnInitFmod.interactable = false;
        btnPlaySfx.interactable  = true;
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
        btnPlayMusic.interactable = true;
        Debug.Log("[MemTest] All SFX played. Snapshot 3 taken.");
    }

    // =========================================================================
    // ШАГ 4 — запуск долгого трека + снапшот ВО ВРЕМЯ воспроизведения
    // =========================================================================
    void OnPlayMusic()
    {
        _musicInstance = FMODUnity.RuntimeManager.CreateInstance(musicEventPath);
        _musicInstance.start();

        btnPlayMusic.interactable = false;
        btnStopMusic.interactable = true;

        // Небольшая задержка, чтобы трек точно начал играть
        StartCoroutine(SnapshotAfterDelay("4_DuringMusic", 1.0f));
        Debug.Log("[MemTest] Music started.");
    }

    IEnumerator SnapshotAfterDelay(string label, float delay)
    {
        yield return new WaitForSeconds(delay);
        TakeSnapshot(label);
        Debug.Log($"[MemTest] Snapshot 4 taken (during music playback).");
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
    // Утилита: сделать снапшот
    // =========================================================================
    void TakeSnapshot(string label)
    {
#if UNITY_EDITOR
        string fileName = $"{label}.snap";
        string path = System.IO.Path.Combine(Application.dataPath, "../", fileName);
        MemoryProfiler.TakeSnapshot(path, OnSnapshotFinished, CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects);
#else
        // На устройстве — через командную строку (ADB) или вручную через Profiler.
        // Здесь можно интегрировать PlayerConnection / Development Build profiler.
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
}