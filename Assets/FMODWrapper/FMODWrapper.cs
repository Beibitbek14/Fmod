using System;
using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace FMODWrapper
{
    /// <summary>
    /// Центральный менеджер аудио-подсистемы. Все вызовы FMOD проходят через него.
    /// Добавьте на пустой GameObject в первой сцене.
    ///
    /// Архитектурные принципы (Architecture Doc v1.0):
    ///   • Core-банки (Master, Core и т.д.) всегда в памяти.
    ///   • Зональные банки загружаются АСИНХРОННО и выгружаются по выходу из зоны.
    ///   • Sample Data загружается отдельно — только по требованию.
    ///   • PlayOneShot — для коротких SFX; не создаёт Handle, освобождает память сам.
    ///   • <see cref="Play"/> возвращает <see cref="PlayBuilder"/> — fluent API.
    /// </summary>
    public class FMODWrapper : MonoBehaviour
    {
        // ─────────────────────────── Singleton ───────────────────────────

        public static FMODWrapper Instance { get; private set; }

        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Core Banks (всегда в памяти)")]
        [Tooltip("Загружаются при старте, никогда не выгружаются.")]
        [SerializeField] private List<string> coreBanks = new() { Banks.Master, Banks.MasterStrings, Banks.Core };

        [Header("Bus Volumes")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;

        // ─────────────────────────── Private state ────────────────────────

        private readonly Dictionary<string, BankEntry> _loadedBanks = new();
        private readonly Dictionary<string, EventInstance> _snapshots = new();
        private readonly Dictionary<string, Coroutine> _pendingLoads = new();
        private readonly List<Handle> _trackedHandles = new();

        private Bus _masterBus;
        private Bus _musicBus;
        private Bus _sfxBus;
        private Bus _voiceBus;

        // ─────────────────────────── Inner types ──────────────────────────

        private readonly struct BankEntry
        {
            public readonly Bank Bank;
            public readonly bool IsCore;
            public BankEntry(Bank bank, bool isCore) { Bank = bank; IsCore = isCore; }
        }

        // ─────────────────────────── Unity ────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitBuses();
            LoadCoreBanks();
            ApplyAllVolumes();
        }

        private void Update()
        {
            _trackedHandles.RemoveAll(h => !h.IsValid || h.IsStopped);
        }

        private void OnDestroy()
        {
            StopAll(allowFadeout: false);
            UnloadAllBanks();
        }

        // ─────────────────────────── Init ─────────────────────────────────

        private void InitBuses()
        {
            _masterBus = RuntimeManager.GetBus(Buses.Master);
            _musicBus = RuntimeManager.GetBus(Buses.Music);
            _sfxBus = RuntimeManager.GetBus(Buses.SFX);
            _voiceBus = RuntimeManager.GetBus(Buses.Voice);
        }

        private void LoadCoreBanks()
        {
            foreach (var bankName in coreBanks)
                LoadBankSync(bankName, isCore: true);
        }

        // ═════════════════════════════════════════════════════════════════
        //  BANKS
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Загружает зональный банк асинхронно (не блокирует основной поток).
        /// Вызывайте заблаговременно — при пересечении триггера на границе зоны.
        /// </summary>
        /// <param name="bankName">Имя банка без расширения (например, "Forest_North").</param>
        /// <param name="loadSamples">
        ///   true — загрузить метаданные + Sample Data сразу.
        ///   false — только метаданные; Sample Data подгружается позже через <see cref="LoadSamples"/>.
        /// </param>
        /// <param name="onLoaded">Вызывается после завершения загрузки.</param>
        public void LoadBankAsync(string bankName, bool loadSamples = false, Action onLoaded = null)
        {
            if (_loadedBanks.ContainsKey(bankName)) { onLoaded?.Invoke(); return; }
            if (_pendingLoads.ContainsKey(bankName)) return;

            var coroutine = StartCoroutine(LoadBankAsyncRoutine(bankName, loadSamples, onLoaded));
            _pendingLoads[bankName] = coroutine;
        }

        /// <summary>
        /// Выгружает зональный банк. Core-банки выгрузить нельзя.
        /// </summary>
        public void UnloadBank(string bankName)
        {
            if (_loadedBanks.TryGetValue(bankName, out var entry) && entry.IsCore)
            {
                Debug.LogWarning($"[Audio] Cannot unload core bank: {bankName}");
                return;
            }

            if (_pendingLoads.TryGetValue(bankName, out var pending))
            {
                StopCoroutine(pending);
                _pendingLoads.Remove(bankName);
            }

            UnloadBankInternal(bankName);
        }

        /// <summary>Выгружает все зональные банки, не трогая Core.</summary>
        public void UnloadZoneBanks()
        {
            var toUnload = new List<string>();
            foreach (var kv in _loadedBanks)
                if (!kv.Value.IsCore) toUnload.Add(kv.Key);
            foreach (var bankName in toUnload)
                UnloadBankInternal(bankName);
        }

        /// <summary>Загружает Sample Data уже загруженного банка в RAM.</summary>
        public void LoadSamples(string bankName)
        {
            if (!_loadedBanks.TryGetValue(bankName, out var entry))
            {
                Debug.LogWarning($"[Audio] LoadSamples: bank '{bankName}' is not loaded.");
                return;
            }
            LogIfError(entry.Bank.loadSampleData(), $"LoadSamples '{bankName}'");
        }

        /// <summary>Выгружает Sample Data банка из RAM (метаданные остаются).</summary>
        public void UnloadSamples(string bankName)
        {
            if (!_loadedBanks.TryGetValue(bankName, out var entry))
            {
                Debug.LogWarning($"[Audio] UnloadSamples: bank '{bankName}' is not loaded.");
                return;
            }
            LogIfError(entry.Bank.unloadSampleData(), $"UnloadSamples '{bankName}'");
        }

        /// <summary>True если банк с данным именем загружен.</summary>
        public bool IsBankLoaded(string bankName) => _loadedBanks.ContainsKey(bankName);

        // ═════════════════════════════════════════════════════════════════
        //  ONE-SHOT  (fire & forget — для коротких SFX)
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Воспроизводит событие один раз (2D). Оптимален по CPU.
        /// Используйте для: ударов, пикапов, UI, одиночных выстрелов.
        /// </summary>
        public void PlayOneShot(EventReference eventRef) => RuntimeManager.PlayOneShot(eventRef);

        /// <summary>Воспроизводит событие один раз в мировой позиции (3D).</summary>
        public void PlayOneShot(EventReference eventRef, Vector3 worldPosition) => RuntimeManager.PlayOneShot(eventRef, worldPosition);

        /// <summary>Воспроизводит событие один раз с произвольными параметрами (3D).</summary>
        public void PlayOneShot(EventReference eventRef, Vector3 worldPosition, Dictionary<string, float> parameters)
        {
            var instance = RuntimeManager.CreateInstance(eventRef);
            if (!instance.isValid()) return;
            instance.set3DAttributes(worldPosition.To3DAttributes());
            
            foreach (var kv in parameters)
                instance.setParameterByName(kv.Key, kv.Value);
            
            instance.start();
            instance.release();
        }

        // ═════════════════════════════════════════════════════════════════
        //  MANAGED INSTANCES  (fluent builder)
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Возвращает <see cref="PlayBuilder"/> для настройки и запуска события.
        ///
        /// <code>
        /// Manager.Instance
        ///     .Play(eventRef)
        ///     .WithParam(Params.WeaponType, 2f)
        ///     .AtPosition(transform.position)
        ///     .WithVolume(0.9f)
        ///     .Start();
        /// </code>
        /// </summary>
        public PlayBuilder Play(EventReference eventRef) => new(this, eventRef);

        /// <summary>
        /// Создаёт управляемый Handle без запуска.
        /// Для запуска вызовите <see cref="Handle.Play"/>.
        /// Предпочтите <see cref="Play"/> для более удобного fluent API.
        /// </summary>
        public Handle CreateHandle(EventReference eventRef)
        {
            var handle = new Handle(eventRef);
            if (handle.IsValid) _trackedHandles.Add(handle);
            return handle;
        }

        /// <summary>Первый валидный активный Handle с данным путём события или null.</summary>
        public Handle GetHandle(string eventPath) => _trackedHandles.Find(h => h.EventPath == eventPath && h.IsValid);

        /// <summary>Все валидные активные Handles с данным путём события.</summary>
        public List<Handle> GetHandles(string eventPath) => _trackedHandles.FindAll(h => h.EventPath == eventPath && h.IsValid);

        // ═════════════════════════════════════════════════════════════════
        //  SNAPSHOTS
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Запускает снапшот FMOD (DSP-эффект на микшер).</summary>
        public void StartSnapshot(string snapshotPath)
        {
            if (_snapshots.ContainsKey(snapshotPath)) return;
            var desc = RuntimeManager.GetEventDescription(snapshotPath);
            desc.createInstance(out EventInstance instance);
            instance.start();
            _snapshots[snapshotPath] = instance;
        }

        /// <summary>Останавливает снапшот.</summary>
        public void StopSnapshot(string snapshotPath, bool allowFadeout = true)
        {
            if (!_snapshots.TryGetValue(snapshotPath, out var instance)) return;
            instance.stop(allowFadeout ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);
            instance.release();
            _snapshots.Remove(snapshotPath);
        }

        // ═════════════════════════════════════════════════════════════════
        //  GLOBAL PARAMETERS
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Устанавливает глобальный параметр FMOD Studio.</summary>
        public void SetGlobalParam(string paramName, float value, bool ignoreSeekSpeed = false)
        {
            LogIfError(RuntimeManager.StudioSystem.setParameterByName(paramName, value, ignoreSeekSpeed), $"SetGlobalParam '{paramName}'");
        }

        /// <summary>Возвращает значение глобального параметра или null если не найден.</summary>
        public float? GetGlobalParam(string paramName)
        {
            var result = RuntimeManager.StudioSystem.getParameterByName(paramName, out float value);
            return result == FMOD.RESULT.OK ? value : null;
        }

        // ═════════════════════════════════════════════════════════════════
        //  VOLUME  (Bus / VCA)
        // ═════════════════════════════════════════════════════════════════

        public float MasterVolume
        {
            get => masterVolume;
            set { masterVolume = Mathf.Clamp01(value); _masterBus.setVolume(masterVolume); }
        }

        public float MusicVolume
        {
            get => musicVolume;
            set { musicVolume = Mathf.Clamp01(value); _musicBus.setVolume(musicVolume); }
        }

        public float SfxVolume
        {
            get => sfxVolume;
            set { sfxVolume = Mathf.Clamp01(value); _sfxBus.setVolume(sfxVolume); }
        }

        public float VoiceVolume
        {
            get => voiceVolume;
            set { voiceVolume = Mathf.Clamp01(value); _voiceBus.setVolume(voiceVolume); }
        }

        public void SetBusVolume(string busPath, float volume) => RuntimeManager.GetBus(busPath).setVolume(Mathf.Clamp01(volume));

        public void SetBusPaused(string busPath, bool paused) => RuntimeManager.GetBus(busPath).setPaused(paused);

        public void SetBusMuted(string busPath, bool muted) => RuntimeManager.GetBus(busPath).setMute(muted);

        public void SetVCAVolume(string vcaPath, float volume) => RuntimeManager.GetVCA(vcaPath).setVolume(Mathf.Clamp01(volume));

        // ═════════════════════════════════════════════════════════════════
        //  LISTENER
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Переопределяет позицию слушателя вручную (если не используется StudioListener).</summary>
        public void SetListenerAttributes(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up, int listenerIndex = 0)
        {
            var attr = position.To3DAttributes();
            attr.velocity = velocity.ToFMODVector();
            attr.forward = forward.ToFMODVector();
            attr.up = up.ToFMODVector();
            RuntimeManager.StudioSystem.setListenerAttributes(listenerIndex, attr);
        }

        // ═════════════════════════════════════════════════════════════════
        //  GLOBAL CONTROL
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Останавливает и освобождает все отслеживаемые Handles и снапшоты.</summary>
        public void StopAll(bool allowFadeout = false)
        {
            foreach (var handle in _trackedHandles)
                handle.Stop(allowFadeout, release: true);
            _trackedHandles.Clear();

            foreach (var kv in _snapshots)
            {
                kv.Value.stop(STOP_MODE.IMMEDIATE);
                kv.Value.release();
            }
            _snapshots.Clear();
        }

        /// <summary>Останавливает все события на указанной шине.</summary>
        public void StopAllOnBus(string busPath, bool allowFadeout = true) => RuntimeManager.GetBus(busPath).stopAllEvents(allowFadeout ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);

        // ─────────────────────────── Internal ─────────────────────────────

        private void LoadBankSync(string bankName, bool isCore)
        {
            if (_loadedBanks.ContainsKey(bankName)) return;
            try
            {
                RuntimeManager.LoadBank(bankName, loadSamples: isCore);
                RuntimeManager.StudioSystem.getBank($"bank:/{bankName}", out Bank bank);
                _loadedBanks[bankName] = new BankEntry(bank, isCore);
                Debug.Log($"[Audio] Bank loaded ({(isCore ? "core" : "zone")}): {bankName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Audio] Failed to load bank '{bankName}': {e.Message}");
            }
        }

        private IEnumerator LoadBankAsyncRoutine(string bankName, bool loadSamples, Action onLoaded)
        {
            yield return null; // отдаём кадр перед любой работой

            // ── Фаза 1: загрузка метаданных (синхронная, но вне главного init) ──
            Bank bank = default;
            bool success = false;

            try
            {
                RuntimeManager.LoadBank(bankName, loadSamples: false);
                RuntimeManager.StudioSystem.getBank($"bank:/{bankName}", out bank);
                success = bank.isValid();

                if (!success)
                    Debug.LogError($"[Audio] Bank handle invalid after load: '{bankName}'");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Audio] Failed to load bank '{bankName}': {e.Message}");
            }

            if (!success)
            {
                _pendingLoads.Remove(bankName);
                yield break;
            }

            // ── Фаза 2: загрузка Sample Data + ожидание (yield вне try/catch) ──
            if (loadSamples)
            {
                LogIfError(bank.loadSampleData(), $"LoadBankAsync samples '{bankName}'");

                LOADING_STATE state;
                do
                {
                    bank.getSampleLoadingState(out state);
                    yield return null;
                }
                while (state == LOADING_STATE.LOADING);
            }

            // ── Фаза 3: регистрация ──
            _loadedBanks[bankName] = new BankEntry(bank, isCore: false);
            _pendingLoads.Remove(bankName);
            Debug.Log($"[Audio] Bank async loaded: {bankName}");

            onLoaded?.Invoke();
        }

        private void UnloadBankInternal(string bankName)
        {
            if (!_loadedBanks.TryGetValue(bankName, out var entry)) return;
            entry.Bank.unload();
            _loadedBanks.Remove(bankName);
            Debug.Log($"[Audio] Bank unloaded: {bankName}");
        }

        private void UnloadAllBanks()
        {
            foreach (var kv in _loadedBanks)
                kv.Value.Bank.unload();
            _loadedBanks.Clear();
        }

        private void ApplyAllVolumes()
        {
            _masterBus.setVolume(masterVolume);
            _musicBus.setVolume(musicVolume);
            _sfxBus.setVolume(sfxVolume);
            _voiceBus.setVolume(voiceVolume);
        }

        private static void LogIfError(FMOD.RESULT result, string context)
        {
            if (result != FMOD.RESULT.OK)
                Debug.LogWarning($"[Audio] {context} failed: {result}");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            ApplyAllVolumes();
        }
#endif
    }
}