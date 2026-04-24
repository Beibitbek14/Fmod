using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    /// <summary>
    /// Fluent-строитель для запуска управляемого события.
    /// Позволяет указать параметры, позицию, громкость и приоритет до старта.
    ///
    /// Использование:
    /// <code>
    /// Manager.Instance
    ///     .Play(eventRef)
    ///     .WithParam(Params.FootstepSurface, 1f)
    ///     .WithVolume(0.8f)
    ///     .AtPosition(transform.position)
    ///     .Start();
    /// </code>
    ///
    /// Для одноразовых коротких SFX предпочтите <see cref="FMODWrapper.PlayOneShot"/> —
    /// он не выделяет Handle и автоматически освобождает память.
    /// </summary>
    public sealed class PlayBuilder
    {
        // ─────────────────────────── State ───────────────────────────────

        private readonly FMODWrapper fmodWrapper;
        private readonly EventReference _eventRef;
        private readonly Dictionary<string, float> _params   = new();
        private Vector3? _position;
        private GameObject _attachTo;
        private Rigidbody _rigidbody;
        private float _volume   = 1f;
        private int _priority = Config.Priority.Normal;

        // ─────────────────────────── Constructor ─────────────────────────

        internal PlayBuilder(FMODWrapper fmodWrapper, EventReference eventRef)
        {
            this.fmodWrapper = fmodWrapper;
            _eventRef = eventRef;
        }

        // ═════════════════════════════════════════════════════════════════
        //  FLUENT API
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Устанавливает локальный параметр события.</summary>
        public PlayBuilder WithParam(string name, float value)
        {
            _params[name] = value;
            return this;
        }

        /// <summary>Устанавливает несколько параметров сразу.</summary>
        public PlayBuilder WithParams(Dictionary<string, float> parameters)
        {
            foreach (var kv in parameters)
                _params[kv.Key] = kv.Value;
            return this;
        }

        /// <summary>Устанавливает мировую позицию источника звука (3D).</summary>
        public PlayBuilder AtPosition(Vector3 worldPosition)
        {
            _position = worldPosition;
            return this;
        }

        /// <summary>
        /// Привязывает источник к GameObject (позиция обновляется каждый кадр).
        /// Заменяет <see cref="AtPosition"/> если заданы оба.
        /// </summary>
        public PlayBuilder AttachedTo(GameObject go, Rigidbody rb = null)
        {
            _attachTo  = go;
            _rigidbody = rb;
            return this;
        }

        /// <summary>Устанавливает начальную громкость [0..1].</summary>
        public PlayBuilder WithVolume(float volume)
        {
            _volume = Mathf.Clamp01(volume);
            return this;
        }

        /// <summary>
        /// Устанавливает приоритет голоса.
        /// Используйте константы из <see cref="Config.Priority"/>.
        /// </summary>
        public PlayBuilder WithPriority(int priority)
        {
            _priority = priority;
            return this;
        }

        // ═════════════════════════════════════════════════════════════════
        //  TERMINAL
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Создаёт экземпляр, применяет настройки и запускает воспроизведение.
        /// Возвращает <see cref="Handle"/> для дальнейшего управления.
        /// </summary>
        public Handle Start()
        {
            var handle = fmodWrapper.CreateHandle(_eventRef);
            if (!handle.IsValid) return handle;

            // Параметры
            foreach (var kv in _params)
                handle.SetParam(kv.Key, kv.Value);

            // Громкость и приоритет
            handle.SetVolume(_volume);
            handle.SetPriority(_priority);

            // Позиционирование
            if (_attachTo != null)
                handle.AttachTo(_attachTo, _rigidbody);
            else if (_position.HasValue)
                handle.SetPosition(_position.Value);

            handle.Play();
            return handle;
        }

        /// <summary>
        /// Запускает воспроизведение и немедленно освобождает Handle (fire & forget).
        /// Удобно когда управление событием после старта не нужно.
        /// </summary>
        public void StartAndForget()
        {
            using var handle = Start();
            // Handle освобождается сразу — FMOD продолжит воспроизведение до конца
        }
    }
}
