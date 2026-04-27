using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FMODWrapper
{
    [AddComponentMenu("Audio/Bank Zone Trigger")]
    [RequireComponent(typeof(Collider))]
    public class BankZoneTrigger : MonoBehaviour
    {
        [Header("Banks")]
        [SerializeField] private string[] banksToLoad = {};

        [Header("Options")]
        [SerializeField] private bool loadSamplesOnEnter;
        [SerializeField] [Range(0f, 10f)] private float unloadDelay = 3f;
        [SerializeField] private LayerMask playerLayer;

        private int _playerCount;
        private CancellationTokenSource _unloadCts;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other)) return;

            _playerCount++;
            CancelUnload();
            LoadBanks();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;

            _playerCount = Mathf.Max(0, _playerCount - 1);
            if (_playerCount > 0) return;

            _unloadCts = new CancellationTokenSource();
            UnloadDelayed(_unloadCts.Token).Forget();
        }

        private void LoadBanks()
        {
            foreach (var bankName in banksToLoad)
                FMODWrapper.LoadBankAsync(bankName, loadSamples: loadSamplesOnEnter);
        }

        private async UniTaskVoid UnloadDelayed(CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(unloadDelay), cancellationToken: ct);

            foreach (var bankName in banksToLoad)
                FMODWrapper.UnloadBank(bankName);

            _unloadCts?.Dispose();
            _unloadCts = null;
        }

        private void CancelUnload()
        {
            _unloadCts?.Cancel();
            _unloadCts?.Dispose();
            _unloadCts = null;
        }

        private bool IsPlayer(Collider other) => (playerLayer.value & (1 << other.gameObject.layer)) != 0;

#if UNITY_EDITOR
        private void OnValidate()
        {
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
                col.isTrigger = true;
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
