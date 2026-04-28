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
    public static class FMODWrapper
    {
        private static readonly List<string> CoreBanks = new()
        {
            Banks.Master, 
            Banks.MasterStrings,
            
            // Banks.Core
        };

        private static float _masterVolume = 1f;
        // Examples
        // [Range(0f, 1f)] private static float _musicVolume = 1f;
        // [Range(0f, 1f)] private static float _sfxVolume = 1f;
        // [Range(0f, 1f)] private static float _voiceVolume = 1f;

        private static readonly Dictionary<string, BankEntry> LoadedBanks = new();
        private static readonly Dictionary<string, EventInstance> Snapshots = new();
        private static readonly Dictionary<string, CancellationTokenSource> PendingLoads = new();
        private static readonly List<FMODEventHandle> TrackedHandles = new();
        
        private static Bus _masterBus;
        // Examples
        // private static Bus _musicBus;
        // private static Bus _sfxBus;
        // private static Bus _voiceBus;

        private readonly struct BankEntry
        {
            public readonly Bank Bank;
            public readonly bool IsCore;
            public BankEntry(Bank bank, bool isCore) { Bank = bank; IsCore = isCore; }
        }

        private static void Initialize()
        {
            InitBuses();
            LoadCoreBanks();
            ApplyAllVolumes();
        }

        private static void UpdateHandles()
        {
            TrackedHandles.RemoveAll(h => !h.IsValid || h.IsStopped);
        }

        private static void Destroy()
        {
            StopAll(allowFadeout: false);
            UnloadAllBanks();
        }

        private static void InitBuses()
        {
            _masterBus = RuntimeManager.GetBus(Buses.Master);
            // _musicBus  = RuntimeManager.GetBus(Buses.Music);
            // _sfxBus = RuntimeManager.GetBus(Buses.Sfx);
            // _voiceBus  = RuntimeManager.GetBus(Buses.Voice);
        }

        private static void LoadCoreBanks()
        {
            foreach (var bankName in CoreBanks)
                LoadBankSync(bankName, isCore: true);
        }

        // ── Banks ────────────────────────────────────────────────────────────

        public static void LoadBankAsync(string bankName, bool loadSamples = false, Action onLoaded = null)
        {
            if (LoadedBanks.ContainsKey(bankName)) { onLoaded?.Invoke(); return; }
            if (PendingLoads.ContainsKey(bankName)) return;

            var cts = new CancellationTokenSource();
            PendingLoads[bankName] = cts;
            LoadBankAsyncTask(bankName, loadSamples, onLoaded, cts.Token).Forget();
        }

        public static void UnloadBank(string bankName)
        {
            if (LoadedBanks.TryGetValue(bankName, out var entry) && entry.IsCore) return;

            if (PendingLoads.TryGetValue(bankName, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                PendingLoads.Remove(bankName);
            }

            UnloadBankInternal(bankName);
        }

        public static void UnloadZoneBanks()
        {
            var toUnload = new List<string>();
            foreach (var kv in LoadedBanks)
                if (!kv.Value.IsCore) toUnload.Add(kv.Key);
            foreach (var bankName in toUnload)
                UnloadBankInternal(bankName);
        }

        public static void LoadSamples(string bankName)
        {
            if (LoadedBanks.TryGetValue(bankName, out var entry))
                entry.Bank.loadSampleData();
        }

        public static void UnloadSamples(string bankName)
        {
            if (LoadedBanks.TryGetValue(bankName, out var entry))
                entry.Bank.unloadSampleData();
        }

        public static bool IsBankLoaded(string bankName) => LoadedBanks.ContainsKey(bankName);

        // ── One-shot ─────────────────────────────────────────────────────────

        public static void PlayOneShot(EventReference eventRef) => 
            RuntimeManager.PlayOneShot(eventRef);

        public static void PlayOneShot(EventReference eventRef, Vector3 worldPosition) =>
            RuntimeManager.PlayOneShot(eventRef, worldPosition);

        public static void PlayOneShot(EventReference eventRef, Vector3 worldPosition, Dictionary<string, float> parameters)
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

        public static PlayBuilder Play(EventReference eventRef) => new(eventRef);

        public static FMODEventHandle CreateHandle(EventReference eventRef)
        {
            var handle = new FMODEventHandle(eventRef);
            if (handle.IsValid) TrackedHandles.Add(handle);
            return handle;
        }

        public static FMODEventHandle GetHandle(FMOD.GUID guid) =>
            TrackedHandles.Find(h => h.EventGuid == guid && h.IsValid);

        public static List<FMODEventHandle> GetHandles(FMOD.GUID guid) =>
            TrackedHandles.FindAll(h => h.EventGuid == guid && h.IsValid);

        // ── Snapshots ────────────────────────────────────────────────────────

        public static void StartSnapshot(string snapshotPath)
        {
            if (Snapshots.ContainsKey(snapshotPath)) return;
            var desc = RuntimeManager.GetEventDescription(snapshotPath);
            desc.createInstance(out EventInstance instance);
            instance.start();
            Snapshots[snapshotPath] = instance;
        }

        public static void StopSnapshot(string snapshotPath, bool allowFadeout = true)
        {
            if (!Snapshots.TryGetValue(snapshotPath, out var instance)) return;
            instance.stop(allowFadeout ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);
            instance.release();
            Snapshots.Remove(snapshotPath);
        }

        // ── Global parameters ────────────────────────────────────────────────

        public static void SetGlobalParam(string paramName, float value, bool ignoreSeekSpeed = false) =>
            RuntimeManager.StudioSystem.setParameterByName(paramName, value, ignoreSeekSpeed);


        // ── Volume ───────────────────────────────────────────────────────────

        public static float MasterVolume
        {
            get => _masterVolume;
            set { _masterVolume = Mathf.Clamp01(value); _masterBus.setVolume(_masterVolume); }
        } 
        
        /* // Custom 
        public static float MusicVolume
        {
            get => _musicVolume;
            set { _musicVolume = Mathf.Clamp01(value); _musicBus.setVolume(_musicVolume); }
        }

        public static float SfxVolume
        {
            get => _sfxVolume;
            set { _sfxVolume = Mathf.Clamp01(value); _sfxBus.setVolume(_sfxVolume); }
        }

        public static float VoiceVolume
        {
            get => _voiceVolume;
            set { _voiceVolume = Mathf.Clamp01(value); _voiceBus.setVolume(_voiceVolume); }
        }
        */

        public static void SetBusVolume(string busPath, float volume) =>
            RuntimeManager.GetBus(busPath).setVolume(Mathf.Clamp01(volume));

        public static void SetBusPaused(string busPath, bool paused) =>
            RuntimeManager.GetBus(busPath).setPaused(paused);

        public static void SetBusMuted(string busPath, bool muted) =>
            RuntimeManager.GetBus(busPath).setMute(muted);

        public static void SetVcaVolume(string vcaPath, float volume) =>
            RuntimeManager.GetVCA(vcaPath).setVolume(Mathf.Clamp01(volume));

        // ── Listener ─────────────────────────────────────────────────────────

        public static void SetListenerAttributes(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up, int listenerIndex = 0)
        {
            var attr = position.To3DAttributes();
            attr.velocity = velocity.ToFMODVector();
            attr.forward  = forward.ToFMODVector();
            attr.up = up.ToFMODVector();
            RuntimeManager.StudioSystem.setListenerAttributes(listenerIndex, attr);
        }

        // ── Global control ───────────────────────────────────────────────────

        public static void StopAll(bool allowFadeout = false)
        {
            foreach (var handle in TrackedHandles)
                handle.Stop(allowFadeout, release: true);
            TrackedHandles.Clear();

            foreach (var kv in Snapshots)
            {
                kv.Value.stop(STOP_MODE.IMMEDIATE);
                kv.Value.release();
            }
            Snapshots.Clear();
        }

        public static void StopAllOnBus(string busPath, bool allowFadeout = true) =>
            RuntimeManager.GetBus(busPath).stopAllEvents(allowFadeout ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);

        // ── Internal ─────────────────────────────────────────────────────────

        private static void LoadBankSync(string bankName, bool isCore)
        {
            if (LoadedBanks.ContainsKey(bankName)) return;
            RuntimeManager.LoadBank(bankName, loadSamples: isCore);
            RuntimeManager.StudioSystem.getBank($"bank:/{bankName}", out Bank bank);
            LoadedBanks[bankName] = new BankEntry(bank, isCore);
        }

        private static async UniTaskVoid LoadBankAsyncTask(string bankName, bool loadSamples, Action onLoaded, CancellationToken ct)
        {
            await UniTask.Yield(ct);

            RuntimeManager.LoadBank(bankName, loadSamples: false);
            RuntimeManager.StudioSystem.getBank($"bank:/{bankName}", out Bank bank);

            if (!bank.isValid())
            {
                PendingLoads.Remove(bankName);
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

            LoadedBanks[bankName] = new BankEntry(bank, isCore: false);
            PendingLoads.Remove(bankName);
            onLoaded?.Invoke();
        }

        private static void UnloadBankInternal(string bankName)
        {
            if (!LoadedBanks.TryGetValue(bankName, out var entry)) return;
            entry.Bank.unload();
            LoadedBanks.Remove(bankName);
        }

        private static void UnloadAllBanks()
        {
            foreach (var kv in LoadedBanks)
                kv.Value.Bank.unload();
            LoadedBanks.Clear();
        }

        private static void ApplyAllVolumes()
        {
            _masterBus.setVolume(_masterVolume);
            // _musicBus.setVolume(_musicVolume);
            // _sfxBus.setVolume(_sfxVolume);
            // _voiceBus.setVolume(_voiceVolume);
        }
    }
}
