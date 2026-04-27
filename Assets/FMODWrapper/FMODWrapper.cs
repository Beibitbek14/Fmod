using System;
using System.Collections.Generic;
using System.Threading;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using Cysharp.Threading.Tasks;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace FMODWrapper
{
    public class FMODWrapper : MonoBehaviour
    {
        public static FMODWrapper Instance { get; private set; }

        [Header("Core Banks")]
        [SerializeField] private List<string> coreBanks = new() { Banks.Master, Banks.MasterStrings /*, Banks.Core*/ };

        [Header("Bus Volumes")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;

        private readonly Dictionary<string, BankEntry> _loadedBanks = new();
        private readonly Dictionary<string, EventInstance> _snapshots = new();
        private readonly Dictionary<string, CancellationTokenSource> _pendingLoads = new();
        private readonly List<FMODEventHandle> _trackedHandles = new();

        private Bus _masterBus;
        private Bus _musicBus;
        private Bus _sfxBus;
        private Bus _voiceBus;

        private readonly struct BankEntry
        {
            public readonly Bank Bank;
            public readonly bool IsCore;
            public BankEntry(Bank bank, bool isCore) { Bank = bank; IsCore = isCore; }
        }

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

        private void InitBuses()
        {
            _masterBus = RuntimeManager.GetBus(Buses.Master);
            // _musicBus  = RuntimeManager.GetBus(Buses.Music);
            // _sfxBus = RuntimeManager.GetBus(Buses.Sfx);
            // _voiceBus  = RuntimeManager.GetBus(Buses.Voice);
        }

        private void LoadCoreBanks()
        {
            foreach (var bankName in coreBanks)
                LoadBankSync(bankName, isCore: true);
        }

        // ── Banks ────────────────────────────────────────────────────────────

        public void LoadBankAsync(string bankName, bool loadSamples = false, Action onLoaded = null)
        {
            if (_loadedBanks.ContainsKey(bankName)) { onLoaded?.Invoke(); return; }
            if (_pendingLoads.ContainsKey(bankName)) return;

            var cts = new CancellationTokenSource();
            _pendingLoads[bankName] = cts;
            LoadBankAsyncTask(bankName, loadSamples, onLoaded, cts.Token).Forget();
        }

        public void UnloadBank(string bankName)
        {
            if (_loadedBanks.TryGetValue(bankName, out var entry) && entry.IsCore) return;

            if (_pendingLoads.TryGetValue(bankName, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _pendingLoads.Remove(bankName);
            }

            UnloadBankInternal(bankName);
        }

        public void UnloadZoneBanks()
        {
            var toUnload = new List<string>();
            foreach (var kv in _loadedBanks)
                if (!kv.Value.IsCore) toUnload.Add(kv.Key);
            foreach (var bankName in toUnload)
                UnloadBankInternal(bankName);
        }

        public void LoadSamples(string bankName)
        {
            if (_loadedBanks.TryGetValue(bankName, out var entry))
                entry.Bank.loadSampleData();
        }

        public void UnloadSamples(string bankName)
        {
            if (_loadedBanks.TryGetValue(bankName, out var entry))
                entry.Bank.unloadSampleData();
        }

        public bool IsBankLoaded(string bankName) => _loadedBanks.ContainsKey(bankName);

        // ── One-shot ─────────────────────────────────────────────────────────

        public void PlayOneShot(EventReference eventRef) => RuntimeManager.PlayOneShot(eventRef);

        public void PlayOneShot(EventReference eventRef, Vector3 worldPosition) =>
            RuntimeManager.PlayOneShot(eventRef, worldPosition);

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

        // ── Managed instances ────────────────────────────────────────────────

        public PlayBuilder Play(EventReference eventRef) => new(this, eventRef);

        public FMODEventHandle CreateHandle(EventReference eventRef)
        {
            var handle = new FMODEventHandle(eventRef);
            if (handle.IsValid) _trackedHandles.Add(handle);
            return handle;
        }

        public FMODEventHandle GetHandle(string eventPath) =>
            _trackedHandles.Find(h => h.EventPath == eventPath && h.IsValid);

        public List<FMODEventHandle> GetHandles(string eventPath) =>
            _trackedHandles.FindAll(h => h.EventPath == eventPath && h.IsValid);

        // ── Snapshots ────────────────────────────────────────────────────────

        public void StartSnapshot(string snapshotPath)
        {
            if (_snapshots.ContainsKey(snapshotPath)) return;
            var desc = RuntimeManager.GetEventDescription(snapshotPath);
            desc.createInstance(out EventInstance instance);
            instance.start();
            _snapshots[snapshotPath] = instance;
        }

        public void StopSnapshot(string snapshotPath, bool allowFadeout = true)
        {
            if (!_snapshots.TryGetValue(snapshotPath, out var instance)) return;
            instance.stop(allowFadeout ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);
            instance.release();
            _snapshots.Remove(snapshotPath);
        }

        // ── Global parameters ────────────────────────────────────────────────

        public void SetGlobalParam(string paramName, float value, bool ignoreSeekSpeed = false) =>
            RuntimeManager.StudioSystem.setParameterByName(paramName, value, ignoreSeekSpeed);

        public float? GetGlobalParam(string paramName)
        {
            var result = RuntimeManager.StudioSystem.getParameterByName(paramName, out float value);
            return result == FMOD.RESULT.OK ? value : null;
        }

        // ── Volume ───────────────────────────────────────────────────────────

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

        public void SetBusVolume(string busPath, float volume) =>
            RuntimeManager.GetBus(busPath).setVolume(Mathf.Clamp01(volume));

        public void SetBusPaused(string busPath, bool paused) =>
            RuntimeManager.GetBus(busPath).setPaused(paused);

        public void SetBusMuted(string busPath, bool muted) =>
            RuntimeManager.GetBus(busPath).setMute(muted);

        public void SetVcaVolume(string vcaPath, float volume) =>
            RuntimeManager.GetVCA(vcaPath).setVolume(Mathf.Clamp01(volume));

        // ── Listener ─────────────────────────────────────────────────────────

        public void SetListenerAttributes(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up, int listenerIndex = 0)
        {
            var attr = position.To3DAttributes();
            attr.velocity = velocity.ToFMODVector();
            attr.forward  = forward.ToFMODVector();
            attr.up = up.ToFMODVector();
            RuntimeManager.StudioSystem.setListenerAttributes(listenerIndex, attr);
        }

        // ── Global control ───────────────────────────────────────────────────

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

        public void StopAllOnBus(string busPath, bool allowFadeout = true) =>
            RuntimeManager.GetBus(busPath).stopAllEvents(allowFadeout ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);

        // ── Internal ─────────────────────────────────────────────────────────

        private void LoadBankSync(string bankName, bool isCore)
        {
            if (_loadedBanks.ContainsKey(bankName)) return;
            RuntimeManager.LoadBank(bankName, loadSamples: isCore);
            RuntimeManager.StudioSystem.getBank($"bank:/{bankName}", out Bank bank);
            _loadedBanks[bankName] = new BankEntry(bank, isCore);
        }

        private async UniTaskVoid LoadBankAsyncTask(string bankName, bool loadSamples, Action onLoaded, CancellationToken ct)
        {
            await UniTask.Yield(ct);

            RuntimeManager.LoadBank(bankName, loadSamples: false);
            RuntimeManager.StudioSystem.getBank($"bank:/{bankName}", out Bank bank);

            if (!bank.isValid())
            {
                _pendingLoads.Remove(bankName);
                return;
            }

            if (loadSamples)
            {
                bank.loadSampleData();

                LOADING_STATE state;
                do
                {
                    bank.getSampleLoadingState(out state);
                    await UniTask.Yield(ct);
                }
                while (state == LOADING_STATE.LOADING);
            }

            _loadedBanks[bankName] = new BankEntry(bank, isCore: false);
            _pendingLoads.Remove(bankName);
            onLoaded?.Invoke();
        }

        private void UnloadBankInternal(string bankName)
        {
            if (!_loadedBanks.TryGetValue(bankName, out var entry)) return;
            entry.Bank.unload();
            _loadedBanks.Remove(bankName);
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            ApplyAllVolumes();
        }
#endif
    }
}
