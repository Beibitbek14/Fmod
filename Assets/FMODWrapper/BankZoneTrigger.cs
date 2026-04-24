using System.Collections;
using UnityEngine;

namespace FMODWrapper
{
    /// <summary>
    /// Триггер автоматической загрузки и выгрузки зональных банков FMOD (Architecture Doc §2.1).
    ///
    /// Принцип:
    ///   • Вход в зону → асинхронная загрузка банков (не блокирует поток)
    ///   • Выход из зоны → выгрузка с задержкой (даёт звукам доиграть)
    ///
    /// Настройка:
    ///   1. Добавьте Collider (Is Trigger = true) на пустой GameObject.
    ///   2. Добавьте этот компонент.
    ///   3. Укажите имена банков в <c>_banksToLoad</c>.
    ///   4. Настройте <c>_playerLayer</c> под Layer вашего игрока.
    /// </summary>
    [AddComponentMenu("Audio/Bank Zone Trigger")]
    [RequireComponent(typeof(Collider))]
    public class BankZoneTrigger : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Banks")]
        [Tooltip("Имена банков для загрузки при входе в зону (без расширения .bank).")]
        [SerializeField] private string[] banksToLoad = {};

        [Header("Options")]
        [Tooltip("Загружать Sample Data вместе с банком при входе в зону?\n" + "false — только метаданные; Sample Data подгружается позже через SampleDataCuller.")]
        [SerializeField] private bool loadSamplesOnEnter;

        [Tooltip("Задержка выгрузки при выходе из зоны (секунды). " + "Позволяет активным звукам доиграть перед выгрузкой.")]
        [SerializeField] [Range(0f, 10f)] private float unloadDelay = 3f;

        [Tooltip("Layer объекта-игрока. Только объекты с этим Layer активируют триггер.")]
        [SerializeField] private LayerMask playerLayer;

        // ─────────────────────────── State ───────────────────────────────

        private int _playerCount;
        private Coroutine _unloadCoroutine;

        // ─────────────────────────── Trigger ─────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other)) return;

            _playerCount++;

            // Отменяем запланированную выгрузку, если игрок вернулся
            if (_unloadCoroutine != null)
            {
                StopCoroutine(_unloadCoroutine);
                _unloadCoroutine = null;
            }

            LoadBanks();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;

            _playerCount = Mathf.Max(0, _playerCount - 1);
            if (_playerCount > 0) return;

            _unloadCoroutine = StartCoroutine(UnloadDelayedRoutine());
        }

        // ─────────────────────────── Load / Unload ───────────────────────

        private void LoadBanks()
        {
            var manager = FMODWrapper.Instance;
            if (manager == null)
            {
                Debug.LogError("[Audio] BankZoneTrigger: Audio.Manager not found.");
                return;
            }

            foreach (var bankName in banksToLoad)
            {
                manager.LoadBankAsync(
                    bankName,
                    loadSamples: loadSamplesOnEnter,
                    onLoaded: () => Debug.Log($"[Audio] Zone bank ready: {bankName}")
                );
            }
        }

        private IEnumerator UnloadDelayedRoutine()
        {
            yield return new WaitForSeconds(unloadDelay);

            var manager = FMODWrapper.Instance;
            if (manager != null)
                foreach (var bankName in banksToLoad)
                    manager.UnloadBank(bankName);

            _unloadCoroutine = null;
        }

        // ─────────────────────────── Helpers ─────────────────────────────

        private bool IsPlayer(Collider other) => (playerLayer.value & (1 << other.gameObject.layer)) != 0;

        // ─────────────────────────── Editor ──────────────────────────────

#if UNITY_EDITOR
        private void OnValidate()
        {
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                Debug.LogWarning($"[Audio] BankZoneTrigger on '{name}': Collider must be Is Trigger.");
                col.isTrigger = true;
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.12f);
            var col = GetComponent<Collider>();
            if (col is BoxCollider box)
            {
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.55f);
                Gizmos.DrawWireCube(box.center, box.size);
            }
        }
#endif
    }
}
