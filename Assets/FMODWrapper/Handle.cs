using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    /// <summary>
    /// Обёртка над нативным <see cref="EventInstance"/> FMOD.
    /// Предоставляет типобезопасный fluent API для управления одним событием.
    ///
    /// Жизненный цикл:
    ///   Manager.CreateHandle() → Play() → [SetParam / SetVolume / ...] → Stop()
    ///
    /// Для одноразовых коротких SFX используйте <see cref="FMODWrapper.PlayOneShot"/> —
    /// он не выделяет Handle и освобождает память автоматически.
    /// </summary>
    public sealed class Handle : IDisposable
    {
        // ─────────────────────────── Fields ──────────────────────────────

        private EventInstance _instance;
        private bool _disposed;

        /// <summary>Путь события FMOD Studio, например "event:/SFX/Explosion".</summary>
        public string EventPath { get; }

        /// <summary>True если экземпляр валиден и не освобождён.</summary>
        public bool IsValid => !_disposed && _instance.isValid();

        // ─────────────────────────── Constructor ─────────────────────────

        internal Handle(EventReference eventRef)
        {
            EventPath = eventRef.Path;
            try
            {
                _instance = RuntimeManager.CreateInstance(eventRef);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Audio] Failed to create handle for '{EventPath}': {e.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════
        //  PLAYBACK
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Запускает воспроизведение.</summary>
        public Handle Play()
        {
            AssertValid();
            _instance.start();
            return this;
        }

        /// <summary>Останавливает воспроизведение и опционально освобождает экземпляр.</summary>
        /// <param name="allowFadeout">Разрешить фейдаут FMOD перед остановкой.</param>
        /// <param name="release">Освободить нативный экземпляр после остановки.</param>
        public void Stop(bool allowFadeout = true, bool release = false)
        {
            if (!IsValid) return;
            _instance.stop(allowFadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
            if (release) Release();
        }

        /// <summary>Ставит на паузу / снимает с паузы.</summary>
        public Handle SetPaused(bool paused)
        {
            AssertValid();
            _instance.setPaused(paused);
            return this;
        }

        /// <summary>Переключает паузу.</summary>
        public Handle TogglePause()
        {
            AssertValid();
            _instance.getPaused(out bool current);
            _instance.setPaused(!current);
            return this;
        }

        /// <summary>Освобождает нативный экземпляр. После вызова Handle невалиден.</summary>
        public void Release()
        {
            if (_disposed) return;
            if (_instance.isValid()) _instance.release();
            _disposed = true;
        }

        // ═════════════════════════════════════════════════════════════════
        //  PARAMETERS
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Устанавливает локальный параметр по имени.</summary>
        public Handle SetParam(string name, float value, bool ignoreSeekSpeed = false)
        {
            AssertValid();
            LogIfError(_instance.setParameterByName(name, value, ignoreSeekSpeed), $"SetParam '{name}'");
            return this;
        }

        /// <summary>
        /// Устанавливает локальный параметр по ID.
        /// Предпочтительнее имени — не требует поиска строки в таблице FMOD.
        /// </summary>
        public Handle SetParam(PARAMETER_ID id, float value, bool ignoreSeekSpeed = false)
        {
            AssertValid();
            LogIfError(_instance.setParameterByID(id, value, ignoreSeekSpeed), $"SetParam id={id.data1}/{id.data2}");
            return this;
        }

        /// <summary>
        /// Возвращает текущее значение параметра или null, если хэндл невалиден / параметр не найден.
        /// </summary>
        public float? GetParam(string name)
        {
            if (!IsValid) return null;
            var result = _instance.getParameterByName(name, out float value);
            return result == FMOD.RESULT.OK ? value : null;
        }

        // ═════════════════════════════════════════════════════════════════
        //  PRIORITY (Voice Stealing)
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Устанавливает приоритет голоса. При нехватке каналов FMOD убивает
        /// низкоприоритетные голоса первыми.
        /// Используйте константы из <see cref="Config.Priority"/>.
        /// </summary>
        public Handle SetPriority(int priority)
        {
            AssertValid();

            // ChannelGroup не имеет setPriority — итерируем индивидуальные каналы.
            // Группа может быть не создана сразу после CreateInstance; если пуста — игнорируем.
            var result = _instance.getChannelGroup(out var channelGroup);
            if (result != FMOD.RESULT.OK || !channelGroup.hasHandle()) return this;

            int clamped = Mathf.Clamp(priority, 0, 256);
            channelGroup.getNumChannels(out int count);
            for (int i = 0; i < count; i++)
            {
                channelGroup.getChannel(i, out FMOD.Channel channel);
                if (channel.hasHandle())
                    channel.setPriority(clamped);
            }

            return this;
        }

        // ═════════════════════════════════════════════════════════════════
        //  SPATIAL
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Устанавливает позицию источника звука (3D).</summary>
        public Handle SetPosition(Vector3 worldPosition)
        {
            AssertValid();
            _instance.set3DAttributes(worldPosition.To3DAttributes());
            return this;
        }

        /// <summary>
        /// Привязывает источник к GameObject.
        /// FMOD обновляет позицию / скорость автоматически каждый кадр.
        /// </summary>
        public Handle AttachTo(GameObject go, Rigidbody rb = null)
        {
            AssertValid();
            RuntimeManager.AttachInstanceToGameObject(_instance, go);
            return this;
        }

        /// <summary>Устанавливает полные пространственные атрибуты (позиция, скорость, ориентация).</summary>
        public Handle SetSpatial(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up)
        {
            AssertValid();
            var attr = position.To3DAttributes();
            attr.velocity = velocity.ToFMODVector();
            attr.forward = forward.ToFMODVector();
            attr.up = up.ToFMODVector();
            _instance.set3DAttributes(attr);
            return this;
        }

        // ═════════════════════════════════════════════════════════════════
        //  VOLUME / PITCH
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Устанавливает громкость [0..1].</summary>
        public Handle SetVolume(float volume)
        {
            AssertValid();
            _instance.setVolume(Mathf.Clamp01(volume));
            return this;
        }

        /// <summary>Устанавливает высоту тона (1.0 = нормальная).</summary>
        public Handle SetPitch(float pitch)
        {
            AssertValid();
            _instance.setPitch(pitch);
            return this;
        }

        /// <summary>Устанавливает уровень ревербератора [0..1].</summary>
        public Handle SetReverb(int index, float level)
        {
            AssertValid();
            _instance.setReverbLevel(index, Mathf.Clamp01(level));
            return this;
        }

        // ═════════════════════════════════════════════════════════════════
        //  TIMELINE / CUES
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Устанавливает позицию на таймлайне в миллисекундах.</summary>
        public Handle SetTimeline(int positionMs)
        {
            AssertValid();
            _instance.setTimelinePosition(positionMs);
            return this;
        }

        /// <summary>Возвращает текущую позицию таймлайна в миллисекундах.</summary>
        public int GetTimeline()
        {
            if (!IsValid) return 0;
            _instance.getTimelinePosition(out int pos);
            return pos;
        }

        /// <summary>
        /// Отпускает Sustain Point на таймлайне (Key Off) — позволяет событию
        /// продолжить воспроизведение после паузы на маркере.
        /// </summary>
        public Handle KeyOff()
        {
            AssertValid();
            LogIfError(_instance.keyOff(), "KeyOff");
            return this;
        }

        // ═════════════════════════════════════════════════════════════════
        //  STATE
        // ═════════════════════════════════════════════════════════════════

        public PLAYBACK_STATE PlaybackState
        {
            get
            {
                if (!IsValid) return PLAYBACK_STATE.STOPPED;
                _instance.getPlaybackState(out var state);
                return state;
            }
        }

        public bool IsPlaying  => PlaybackState == PLAYBACK_STATE.PLAYING;
        public bool IsPaused => PlaybackState == PLAYBACK_STATE.SUSTAINING;
        public bool IsStopping => PlaybackState == PLAYBACK_STATE.STOPPING;
        public bool IsStopped => PlaybackState == PLAYBACK_STATE.STOPPED;

        // ═════════════════════════════════════════════════════════════════
        //  CALLBACKS
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Регистрирует колбэк на события экземпляра.</summary>
        public Handle SetCallback(EVENT_CALLBACK callback, EVENT_CALLBACK_TYPE mask = EVENT_CALLBACK_TYPE.ALL)
        {
            AssertValid();
            _instance.setCallback(callback, mask);
            return this;
        }

        // ═════════════════════════════════════════════════════════════════
        //  IDisposable
        // ═════════════════════════════════════════════════════════════════

        public void Dispose() => Release();

        // ─────────────────────────── Helpers ─────────────────────────────

        private void AssertValid([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            if (!IsValid)
                Debug.LogWarning($"[Audio] Handle.{caller}: instance is not valid (path: {EventPath})");
        }

        private static void LogIfError(FMOD.RESULT result, string context)
        {
            if (result != FMOD.RESULT.OK)
                Debug.LogWarning($"[Audio] {context} failed: {result}");
        }
    }
}