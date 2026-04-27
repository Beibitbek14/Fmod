using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    public sealed class PlayBuilder
    {
        private readonly FMODWrapper _fmodWrapper;
        private readonly EventReference _eventRef;
        private readonly Dictionary<string, float> _params = new();
        private Vector3? _position;
        private GameObject _attachTo;
        private Rigidbody _rigidbody;
        private float _volume = 1f;
        private int _priority = Config.Priority.Normal;

        internal PlayBuilder(FMODWrapper fmodWrapper, EventReference eventRef)
        {
            _fmodWrapper = fmodWrapper;
            _eventRef = eventRef;
        }

        public PlayBuilder WithParam(string name, float value)
        {
            _params[name] = value;
            return this;
        }

        public PlayBuilder WithParams(Dictionary<string, float> parameters)
        {
            foreach (var kv in parameters)
                _params[kv.Key] = kv.Value;
            return this;
        }

        public PlayBuilder AtPosition(Vector3 worldPosition)
        {
            _position = worldPosition;
            return this;
        }

        public PlayBuilder AttachedTo(GameObject go, Rigidbody rb = null)
        {
            _attachTo = go;
            _rigidbody = rb;
            return this;
        }

        public PlayBuilder WithVolume(float volume)
        {
            _volume = Mathf.Clamp01(volume);
            return this;
        }

        public PlayBuilder WithPriority(int priority)
        {
            _priority = priority;
            return this;
        }

        public FMODEventHandle Start()
        {
            var handle = _fmodWrapper.CreateHandle(_eventRef);
            if (!handle.IsValid) return handle;

            foreach (var kv in _params)
                handle.SetParam(kv.Key, kv.Value);

            handle.SetVolume(_volume);
            handle.SetPriority(_priority);

            if (_attachTo != null)
                handle.AttachTo(_attachTo, _rigidbody);
            else if (_position.HasValue)
                handle.SetPosition(_position.Value);

            handle.Play();
            return handle;
        }

        public void StartAndForget()
        {
            using var handle = Start();
        }
    }
}
