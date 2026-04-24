using System.Collections;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace FMODWrapper
{
    /// <summary>
    /// Автоматически управляет Sample Data событий по дистанции от слушателя
    /// (Architecture Doc §2.3).
    ///
    /// Проблема:
    ///   FMOD переводит далёкие звуки в Virtual Voice (нулевой CPU), но сами аудиофайлы
    ///   (Sample Data) остаются в RAM. В открытом мире это приводит к росту потребления памяти.
    ///
    /// Решение:
    ///   Компонент явно выгружает Sample Data при удалении источника и загружает при приближении.
    ///   Гистерезис (<see cref="Config.Distance.LoadThreshold"/> &lt; <see cref="Config.Distance.UnloadThreshold"/>)
    ///   предотвращает частые переключения на границе зоны.
    ///
    /// Настройка:
    ///   1. Добавьте компонент на GameObject с пространственным источником звука.
    ///   2. Укажите событие в поле <c>_event</c>.
    ///   3. Настройте пороги или оставьте значения из <see cref="Config.Distance"/>.
    /// </summary>
    [AddComponentMenu("Audio/Sample Data Culler")]
    public class SampleDataCuller : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Event")]
        [Tooltip("Событие FMOD, Sample Data которого нужно контролировать.")]
        [SerializeField] private EventReference _event;

        [Header("Distance Thresholds")]
        [Tooltip("Дистанция, при превышении которой Sample Data выгружается из RAM.")]
        [SerializeField] private float unloadThreshold = Config.Distance.UnloadThreshold;

        [Tooltip("Дистанция повторной загрузки. Должна быть меньше Unload Threshold.")]
        [SerializeField] private float loadThreshold = Config.Distance.LoadThreshold;

        [Header("Performance")]
        [Tooltip("Интервал проверки дистанции (секунды). Каждый кадр избыточен.")]
        [SerializeField] private float checkInterval = Config.Distance.CheckInterval;
        
        [Header("PLayer")]
        [SerializeField] private Transform listenerTransform;

        // ─────────────────────────── State ───────────────────────────────

        private EventDescription _description;
        private bool _samplesLoaded = true;
        private bool _descriptionValid;

        // ─────────────────────────── Unity ────────────────────────────────

        private void Start()
        {
            ResolveDescription();

            if (_descriptionValid)
                StartCoroutine(CullingRoutine());
        }

        // ─────────────────────────── Coroutine ────────────────────────────

        private IEnumerator CullingRoutine()
        {
            var wait = new WaitForSeconds(checkInterval);

            while (true)
            {
                yield return wait;
                CheckDistance();
            }
        }

        private void CheckDistance()
        {
            if (!_descriptionValid) return;

            float sqrDist = (transform.position - listenerTransform.position).sqrMagnitude;

            if (_samplesLoaded && sqrDist > unloadThreshold * unloadThreshold)
            {
                var result = _description.unloadSampleData();
                if (result == FMOD.RESULT.OK)
                {
                    _samplesLoaded = false;
#if UNITY_EDITOR
                    Debug.Log($"[Audio] SampleDataCuller: unloaded '{_event.Path}' " +
                              $"(dist={Mathf.Sqrt(sqrDist):F1}m)");
#endif
                }
            }
            else if (!_samplesLoaded && sqrDist < loadThreshold * loadThreshold)
            {
                var result = _description.loadSampleData();
                if (result == FMOD.RESULT.OK)
                {
                    _samplesLoaded = true;
#if UNITY_EDITOR
                    Debug.Log($"[Audio] SampleDataCuller: loaded '{_event.Path}' " +
                              $"(dist={Mathf.Sqrt(sqrDist):F1}m)");
#endif
                }
            }
        }

        // ─────────────────────────── Init helpers ─────────────────────────

        private void ResolveDescription()
        {
            if (string.IsNullOrEmpty(_event.Path))
            {
                Debug.LogWarning($"[Audio] SampleDataCuller on '{name}': no event assigned.");
                return;
            }

            try
            {
                _description = RuntimeManager.GetEventDescription(_event);
                _descriptionValid = _description.isValid();

                if (!_descriptionValid)
                    Debug.LogWarning($"[Audio] SampleDataCuller: invalid description for '{_event.Path}'");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Audio] SampleDataCuller: failed to resolve '{_event.Path}': {e.Message}");
            }
        }

        // ─────────────────────────── Gizmos ───────────────────────────────

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
