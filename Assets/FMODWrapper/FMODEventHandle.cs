using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    public sealed class FMODEventHandle : IDisposable
    {
        private EventInstance _instance;
        private bool _disposed;

        public string EventPath { get; }
        public bool IsValid => !_disposed && _instance.isValid();

        internal FMODEventHandle(EventReference eventRef)
        {
            EventPath = eventRef.Path;
            _instance = RuntimeManager.CreateInstance(eventRef);
        }

        // ── Playback ─────────────────────────────────────────────────────────

        public FMODEventHandle Play()
        {
            _instance.start();
            return this;
        }

        public void Stop(bool allowFadeout = true, bool release = false)
        {
            if (!IsValid) return;
            _instance.stop(allowFadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
            if (release) Release();
        }

        public FMODEventHandle SetPaused(bool paused)
        {
            _instance.setPaused(paused);
            return this;
        }

        public FMODEventHandle TogglePause()
        {
            _instance.getPaused(out bool current);
            _instance.setPaused(!current);
            return this;
        }

        public void Release()
        {
            if (_disposed) return;
            if (_instance.isValid()) _instance.release();
            _disposed = true;
        }

        // ── Parameters ───────────────────────────────────────────────────────

        public FMODEventHandle SetParam(string name, float value, bool ignoreSeekSpeed = false)
        {
            _instance.setParameterByName(name, value, ignoreSeekSpeed);
            return this;
        }

        public FMODEventHandle SetParam(PARAMETER_ID id, float value, bool ignoreSeekSpeed = false)
        {
            _instance.setParameterByID(id, value, ignoreSeekSpeed);
            return this;
        }

        public float? GetParam(string name)
        {
            if (!IsValid) return null;
            var result = _instance.getParameterByName(name, out float value);
            return result == FMOD.RESULT.OK ? value : null;
        }

        // ── Priority ─────────────────────────────────────────────────────────

        public FMODEventHandle SetPriority(int priority)
        {
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

        // ── Spatial ──────────────────────────────────────────────────────────

        public FMODEventHandle SetPosition(Vector3 worldPosition)
        {
            _instance.set3DAttributes(worldPosition.To3DAttributes());
            return this;
        }

        public FMODEventHandle AttachTo(GameObject go, Rigidbody rb = null)
        {
            RuntimeManager.AttachInstanceToGameObject(_instance, go);
            return this;
        }

        public FMODEventHandle SetSpatial(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up)
        {
            var attr = position.To3DAttributes();
            attr.velocity = velocity.ToFMODVector();
            attr.forward = forward.ToFMODVector();
            attr.up = up.ToFMODVector();
            _instance.set3DAttributes(attr);
            return this;
        }

        // ── Volume / Pitch ───────────────────────────────────────────────────

        public FMODEventHandle SetVolume(float volume)
        {
            _instance.setVolume(Mathf.Clamp01(volume));
            return this;
        }

        public FMODEventHandle SetPitch(float pitch)
        {
            _instance.setPitch(pitch);
            return this;
        }

        public FMODEventHandle SetReverb(int index, float level)
        {
            _instance.setReverbLevel(index, Mathf.Clamp01(level));
            return this;
        }

        // ── Timeline / Cues ──────────────────────────────────────────────────

        public FMODEventHandle SetTimeline(int positionMs)
        {
            _instance.setTimelinePosition(positionMs);
            return this;
        }

        public int GetTimeline()
        {
            if (!IsValid) return 0;
            _instance.getTimelinePosition(out int pos);
            return pos;
        }

        public FMODEventHandle KeyOff()
        {
            _instance.keyOff();
            return this;
        }

        // ── State ────────────────────────────────────────────────────────────

        public PLAYBACK_STATE PlaybackState
        {
            get
            {
                if (!IsValid) return PLAYBACK_STATE.STOPPED;
                _instance.getPlaybackState(out var state);
                return state;
            }
        }

        public bool IsPlaying => PlaybackState == PLAYBACK_STATE.PLAYING;
        public bool IsPaused => PlaybackState == PLAYBACK_STATE.SUSTAINING;
        public bool IsStopping => PlaybackState == PLAYBACK_STATE.STOPPING;
        public bool IsStopped  => PlaybackState == PLAYBACK_STATE.STOPPED;

        // ── Callbacks ────────────────────────────────────────────────────────

        public FMODEventHandle SetCallback(EVENT_CALLBACK callback, EVENT_CALLBACK_TYPE mask = EVENT_CALLBACK_TYPE.ALL)
        {
            _instance.setCallback(callback, mask);
            return this;
        }

        public void Dispose() => Release();
    }
}
