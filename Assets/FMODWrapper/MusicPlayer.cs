using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    public static class MusicPlayer
    { 
        private static readonly float DefaultCrossfade = 1f;

        private static FMODEventHandle _currentTrack;
        private static FMODEventHandle _previousTrack;
        private static CancellationTokenSource _fadeCts;

        public static bool IsPlaying => _currentTrack?.IsPlaying ?? false;

        public static void Play(EventReference eventRef, float crossfade = -1f, Dictionary<string, float> initialParams = null)
        {
            if (_currentTrack is { IsValid: true } && _currentTrack.EventGuid == eventRef.Guid) return;

            float fade = crossfade < 0f ? DefaultCrossfade : crossfade;

            StopFade();
            _previousTrack = _currentTrack;

            _currentTrack = FMODWrapper.CreateHandle(eventRef);
            if (!_currentTrack.IsValid) return;

            if (initialParams != null)
                foreach (var kv in initialParams)
                    _currentTrack.SetParam(kv.Key, kv.Value);

            _currentTrack.SetVolume(fade > 0f ? 0f : 1f);
            _currentTrack.Play();

            _fadeCts = new CancellationTokenSource();

            if (fade > 0f)
                CrossfadeTask(fade, _fadeCts.Token).Forget();
            else
                StopPreviousImmediate();
        }

        public static void Stop(bool allowFadeout = true)
        {
            StopFade();
            _currentTrack?.Stop(allowFadeout, release: true);
            _currentTrack = null;
        }
        
        private static void StopAll()
        {
            StopFade();
            _currentTrack?.Stop(allowFadeout: false, release: true);
            _previousTrack?.Stop(allowFadeout: false, release: true);
            _currentTrack = null;
            _previousTrack = null;
        }

        public static void SetPaused(bool paused) => _currentTrack?.SetPaused(paused);
        public static void SetParam(string paramName, float value) => _currentTrack?.SetParam(paramName, value);
        public static void SetVolume(float volume) => _currentTrack?.SetVolume(volume);
        public static void KeyOff() => _currentTrack?.KeyOff();

        private static async UniTaskVoid CrossfadeTask(float duration, CancellationToken ct)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                _currentTrack?.SetVolume(t);
                _previousTrack?.SetVolume(1f - t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _currentTrack?.SetVolume(1f);
            _previousTrack?.Stop(allowFadeout: false, release: true);
            _previousTrack = null;

            _fadeCts?.Dispose();
            _fadeCts = null;
        }

        private static void StopPreviousImmediate()
        {
            _previousTrack?.Stop(allowFadeout: false, release: true);
            _previousTrack = null;

            _fadeCts?.Dispose();
            _fadeCts = null;
        }

        private static void StopFade()
        {
            _fadeCts?.Cancel();
            _fadeCts?.Dispose();
            _fadeCts = null;
        }
    }
}
