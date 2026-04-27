using System.Threading;
using Cysharp.Threading.Tasks;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    [DisallowMultipleComponent]
    public class MusicPlayer : MonoBehaviour
    {
        public static MusicPlayer Instance { get; private set; }

        [Header("Defaults")]
        [SerializeField] [Range(0f, 5f)] private float defaultCrossfade = 1f;

        private FMODEventHandle _currentTrack;
        private FMODEventHandle _previousTrack;
        private CancellationTokenSource _fadeCts;

        public string CurrentTrackPath => _currentTrack?.EventPath;
        public bool IsPlaying => _currentTrack?.IsPlaying ?? false;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            StopFade();
            _currentTrack?.Stop(allowFadeout: false, release: true);
            _previousTrack?.Stop(allowFadeout: false, release: true);
        }

        public void Play(EventReference eventRef, float crossfade = -1f, params (string name, float value)[] initialParams)
        {
            if (_currentTrack is { IsValid: true } && _currentTrack.EventPath == eventRef.Path) return;

            float fade = crossfade < 0f ? defaultCrossfade : crossfade;

            StopFade();
            _previousTrack = _currentTrack;

            _currentTrack = FMODWrapper.Instance.CreateHandle(eventRef);
            if (!_currentTrack.IsValid) return;

            foreach (var (paramName, value) in initialParams)
                _currentTrack.SetParam(paramName, value);

            _currentTrack.SetVolume(fade > 0f ? 0f : 1f);
            _currentTrack.Play();

            _fadeCts = new CancellationTokenSource();

            if (fade > 0f)
                CrossfadeTask(fade, _fadeCts.Token).Forget();
            else
                StopPreviousImmediate();
        }

        public void Stop(bool allowFadeout = true)
        {
            StopFade();
            _currentTrack?.Stop(allowFadeout, release: true);
            _currentTrack = null;
        }

        public void SetPaused(bool paused) => _currentTrack?.SetPaused(paused);
        public void SetParam(string paramName, float value) => _currentTrack?.SetParam(paramName, value);
        public void SetVolume(float volume) => _currentTrack?.SetVolume(volume);
        public void KeyOff() => _currentTrack?.KeyOff();

        private async UniTaskVoid CrossfadeTask(float duration, CancellationToken ct)
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

        private void StopPreviousImmediate()
        {
            _previousTrack?.Stop(allowFadeout: false, release: true);
            _previousTrack = null;

            _fadeCts?.Dispose();
            _fadeCts = null;
        }

        private void StopFade()
        {
            _fadeCts?.Cancel();
            _fadeCts?.Dispose();
            _fadeCts = null;
        }
    }
}
