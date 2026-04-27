using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    [AddComponentMenu("Audio/Sample Data Culler")]
    public class SampleDataCuller : MonoBehaviour
    {
        [Header("Event")]
        [SerializeField] private EventReference @event;

        [Header("Distance Thresholds")]
        [SerializeField] private float unloadThreshold = Config.Distance.UnloadThreshold;
        [SerializeField] private float loadThreshold = Config.Distance.LoadThreshold;

        [Header("Performance")]
        [SerializeField] private float checkInterval = Config.Distance.CheckInterval;

        [Header("Player")]
        [SerializeField] private Transform listenerTransform;

        private EventDescription _description;
        private bool _samplesLoaded = true;
        private bool _descriptionValid;

        private void Start()
        {
            ResolveDescription();
            if (_descriptionValid)
                CullingLoop(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid CullingLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(checkInterval), cancellationToken: ct);
                CheckDistance();
            }
        }

        private void CheckDistance()
        {
            if (!_descriptionValid) return;

            float sqrDist = (transform.position - listenerTransform.position).sqrMagnitude;

            if (_samplesLoaded && sqrDist > unloadThreshold * unloadThreshold)
            {
                if (_description.unloadSampleData() == FMOD.RESULT.OK)
                    _samplesLoaded = false;
            }
            else if (!_samplesLoaded && sqrDist < loadThreshold * loadThreshold)
            {
                if (_description.loadSampleData() == FMOD.RESULT.OK)
                    _samplesLoaded = true;
            }
        }

        private void ResolveDescription()
        {
            if (string.IsNullOrEmpty(@event.Path)) return;

            _description = RuntimeManager.GetEventDescription(@event);
            _descriptionValid = _description.isValid();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, unloadThreshold);

            Gizmos.color = new Color(0f, 1f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, loadThreshold);
        }

        private void OnValidate()
        {
            if (loadThreshold >= unloadThreshold)
                loadThreshold = unloadThreshold * 0.8f;
        }
#endif
    }
}
