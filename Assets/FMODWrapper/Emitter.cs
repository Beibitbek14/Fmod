using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    /// <summary>
    /// Пространственный источник звука, привязанный к GameObject.
    /// Аналог Unity AudioSource, но для FMOD.
    ///
    /// Позиция обновляется автоматически через RuntimeManager.AttachInstanceToGameObject.
    ///
    /// Для одноразовых коротких SFX предпочтите <see cref="FMODWrapper.PlayOneShot"/> —
    /// он не выделяет Handle и работает эффективнее.
    /// </summary>
    [AddComponentMenu("Audio/Emitter")]
    [DisallowMultipleComponent]
    public class Emitter : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Event")]
        [SerializeField] private EventReference _event;
        [SerializeField] private bool playOnAwake;
        
        [Header("Playback")]
        [SerializeField] [Range(0f, 1f)] private float volume = 1f;
        [SerializeField] private bool  use3D  = true;

        [Header("Voice Priority")]
        [Tooltip(
            "Приоритет голоса при конкуренции за каналы (Architecture Doc §2.4).\n" +
            "0 = Highest\n" +
            "1 = High\n" +
            "2 = Normal\n" +
            "3 = Low"
        )]
        [SerializeField] [Range(0, 3)] private int priority = Config.Priority.Normal;

        // ─────────────────────────── Runtime ─────────────────────────────

        private Handle _handle;
        private Rigidbody _rb;

        public bool IsPlaying => _handle?.IsPlaying ?? false;
        public bool IsPaused => _handle?.IsPaused ?? false;

        // ─────────────────────────── Unity ────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            if (playOnAwake) Play();
        }

        private void OnDestroy()
        {
            Stop(allowFadeout: false);
        }

        // ═════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Запускает воспроизведение. Предыдущий экземпляр останавливается.</summary>
        public void Play()
        {
            Stop();

            _handle = FMODWrapper.Instance
                .Play(_event)
                .WithVolume(volume)
                .WithPriority(priority)
                .Start();

            // AttachTo вызывается отдельно после Start() если use3D,
            // потому что PlayBuilder.Start() уже возвращает запущенный Handle
            if (use3D && _handle.IsValid)
                _handle.AttachTo(gameObject, _rb);
        }

        /// <summary>Останавливает воспроизведение и освобождает Handle.</summary>
        public void Stop(bool allowFadeout = true)
        {
            _handle?.Stop(allowFadeout, release: true);
            _handle = null;
        }

        /// <summary>Пауза / продолжение.</summary>
        public void SetPaused(bool paused) => _handle?.SetPaused(paused);

        /// <summary>Переключает паузу.</summary>
        public void TogglePause() => _handle?.TogglePause();

        /// <summary>Устанавливает параметр события по имени.</summary>
        public void SetParam(string paramName, float value) => _handle?.SetParam(paramName, value);

        /// <summary>Обновляет громкость в реальном времени.</summary>
        public void SetVolume(float v)
        {
            volume = Mathf.Clamp01(v);
            _handle?.SetVolume(volume);
        }

        /// <summary>
        /// Изменяет приоритет голоса (влияет только на активный экземпляр).
        /// Используйте константы из <see cref="Config.Priority"/>.
        /// </summary>
        public void SetPriority(int channelPriority)
        {
            priority = Mathf.Clamp(channelPriority, 0, 3);
            _handle?.SetPriority(this.priority);
        }

        /// <summary>Отпускает Sustain Point (Key Off) — позволяет событию продолжить воспроизведение.</summary>
        public void KeyOff() => _handle?.KeyOff();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            _handle?.SetVolume(volume);
            _handle?.SetPriority(priority);
        }
#endif
    }
}
