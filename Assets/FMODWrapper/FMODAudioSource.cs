using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    [AddComponentMenu("Audio/FMODAudioSource")]
    [DisallowMultipleComponent]
    public class FMODAudioSource : MonoBehaviour
    {
        [Header("Event")]
        [SerializeField] private EventReference @event;
        [SerializeField] private bool playOnAwake;

        [Header("Playback")]
        [SerializeField] [Range(0f, 1f)] private float volume = 1f;
        [SerializeField] private bool use3D = true;

        [Header("Voice Priority")]
        [SerializeField] [Range(0, 3)] private int priority = Config.Priority.Normal;

        private FMODEventHandle _fmodEventHandle;
        private Rigidbody _rb;

        public bool IsPlaying => _fmodEventHandle?.IsPlaying ?? false;
        public bool IsPaused => _fmodEventHandle?.IsPaused ?? false;

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private void Start()
        {
            if (playOnAwake) Play();
        }

        private void OnDestroy() => Stop(allowFadeout: false);

        public void Play()
        {
            Stop();

            _fmodEventHandle = FMODWrapper.Instance
                .Play(@event)
                .WithVolume(volume)
                .WithPriority(priority)
                .Start();

            if (use3D && _fmodEventHandle.IsValid)
                _fmodEventHandle.AttachTo(gameObject, _rb);
        }

        public void Stop(bool allowFadeout = true)
        {
            _fmodEventHandle?.Stop(allowFadeout, release: true);
            _fmodEventHandle = null;
        }

        public void SetPaused(bool paused) => _fmodEventHandle?.SetPaused(paused);
        public void TogglePause() => _fmodEventHandle?.TogglePause();
        public void SetParam(string paramName, float value) => _fmodEventHandle?.SetParam(paramName, value);
        public void KeyOff() => _fmodEventHandle?.KeyOff();

        public void SetVolume(float v)
        {
            volume = Mathf.Clamp01(v);
            _fmodEventHandle?.SetVolume(volume);
        }

        public void SetPriority(int channelPriority)
        {
            priority = Mathf.Clamp(channelPriority, 0, 3);
            _fmodEventHandle?.SetPriority(priority);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            _fmodEventHandle?.SetVolume(volume);
            _fmodEventHandle?.SetPriority(priority);
        }
#endif
    }
}
