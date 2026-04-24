using System.Collections;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    /// <summary>
    /// Управляет фоновой музыкой: запуск, кроссфейд, пауза, параметры.
    ///
    /// Отличия от простого <see cref="Emitter"/>:
    ///   • Кроссфейд между треками через <see cref="Play"/>
    ///   • Защита от двойного запуска одного трека
    ///   • Кроссфейд работает на <c>Time.unscaledDeltaTime</c> — не замирает при паузе игры
    ///   • Fluent API через <see cref="PlayBuilder"/>
    /// </summary>
    [DisallowMultipleComponent]
    public class MusicPlayer : MonoBehaviour
    {
        public static MusicPlayer Instance { get; private set; }

        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Defaults")]
        [Tooltip("Длительность кроссфейда по умолчанию (секунды). " + "Передайте отрицательное значение в Play() чтобы использовать это.")]
        [SerializeField] [Range(0f, 5f)] private float defaultCrossfade = 1f;

        // ─────────────────────────── State ────────────────────────────────

        private Handle _currentTrack;
        private Handle _previousTrack;
        private Coroutine _fadeCoroutine;

        /// <summary>Путь текущего воспроизводимого трека или null.</summary>
        public string CurrentTrackPath => _currentTrack?.EventPath;

        /// <summary>True если музыка воспроизводится.</summary>
        public bool IsPlaying => _currentTrack?.IsPlaying ?? false;

        // ─────────────────────────── Unity ────────────────────────────────

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

        // ═════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Запускает трек с кроссфейдом. Если тот же трек уже играет — ничего не делает.
        /// </summary>
        /// <param name="eventRef">Событие музыки в FMOD Studio.</param>
        /// <param name="crossfade">
        ///   Длительность кроссфейда в секундах.
        ///   0 — мгновенное переключение.
        ///   Отрицательное — использовать значение из Inspector (<see cref="defaultCrossfade"/>).
        /// </param>
        /// <param name="initialParams">Начальные параметры (имя, значение).</param>
        public void Play(EventReference eventRef, float crossfade = -1f, params (string name, float value)[] initialParams)
        {
            if (_currentTrack is { IsValid: true } &&
                _currentTrack.EventPath == eventRef.Path)
                return;

            float fade = crossfade < 0f ? defaultCrossfade : crossfade;

            StopFade();
            _previousTrack = _currentTrack;

            // Создаём новый трек через Manager
            _currentTrack = FMODWrapper.Instance.CreateHandle(eventRef);
            if (!_currentTrack.IsValid)
            {
                Debug.LogError($"[Audio] MusicPlayer: failed to create handle '{eventRef.Path}'");
                return;
            }

            foreach (var (paramName, value) in initialParams)
                _currentTrack.SetParam(paramName, value);

            _currentTrack.SetVolume(fade > 0f ? 0f : 1f);
            _currentTrack.Play();

            _fadeCoroutine = StartCoroutine(fade > 0f
                ? CrossfadeRoutine(fade)
                : StopPreviousImmediateRoutine());
        }

        /// <summary>Останавливает текущий трек.</summary>
        public void Stop(bool allowFadeout = true)
        {
            StopFade();
            _currentTrack?.Stop(allowFadeout, release: true);
            _currentTrack = null;
        }

        /// <summary>Пауза / продолжение.</summary>
        public void SetPaused(bool paused) => _currentTrack?.SetPaused(paused);

        /// <summary>Устанавливает параметр на текущем треке (например, MusicIntensity).</summary>
        public void SetParam(string paramName, float value) => _currentTrack?.SetParam(paramName, value);

        /// <summary>Устанавливает громкость текущего трека.</summary>
        public void SetVolume(float volume) => _currentTrack?.SetVolume(volume);

        /// <summary>
        /// Отпускает Sustain Point (Key Off) — переход между секциями адаптивной музыки.
        /// </summary>
        public void KeyOff() => _currentTrack?.KeyOff();

        // ═════════════════════════════════════════════════════════════════
        //  COROUTINES
        // ═════════════════════════════════════════════════════════════════

        private IEnumerator CrossfadeRoutine(float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                // unscaledDeltaTime: не зависит от Time.timeScale, кроссфейд идёт даже при паузе
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                _currentTrack?.SetVolume(t);
                _previousTrack?.SetVolume(1f - t);

                yield return null;
            }

            _currentTrack?.SetVolume(1f);
            _previousTrack?.Stop(allowFadeout: false, release: true);
            _previousTrack = null;
            _fadeCoroutine = null;
        }

        private IEnumerator StopPreviousImmediateRoutine()
        {
            _previousTrack?.Stop(allowFadeout: false, release: true);
            _previousTrack = null;
            _fadeCoroutine = null;
            yield break;
        }

        private void StopFade()
        {
            if (_fadeCoroutine == null) return;
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
    }
}
