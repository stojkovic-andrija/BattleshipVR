using UnityEngine;
using BattleshipsVR.Interaction.Abstractions;

namespace BattleshipsVR.Visuals
{
    /// <summary>
    /// Diegetic target visualizer for hover/lock/launch feedback.
    /// Handles blinking, missile drop, and impact VFX; designed for minimal overdraw in VR.
    /// </summary>
    public sealed class TargetVisualizer : MonoBehaviour, ITargetAimer
    {
        [Header("Quad / Colors")]
        [SerializeField, Tooltip("Quad renderer used to show the targeting reticle.")]
        private MeshRenderer _quadRenderer;
        [SerializeField, Tooltip("Emission multiplier applied while blinking/active.")]
        private float _emissionIntensity = 1.5f;
        [SerializeField, Tooltip("Blink frequency (Hz) while locked and before launch.")]
        private float _blinkHz = 2.0f;

        [Header("Missile")]
        [SerializeField, Tooltip("Transform of the missile object (child).")]
        private Transform _missileRoot;
        [SerializeField, Tooltip("Local Z start position for the missile before drop.")]
        private float _missileStartLocalZ = 0.6f;
        [SerializeField, Tooltip("Seconds for the missile to drop from start Z to impact.")]
        private float _missileDropTime = 0.12f;

        [Header("Impact VFX (children)")]
        [SerializeField, Tooltip("VFX GameObject played on a miss.")]
        private GameObject _missVfx;
        [SerializeField, Tooltip("VFX GameObject played on a hit.")]
        private GameObject _hitVfx;

        /// <summary>True while a cell is locked (pre/post launch) until cleared.</summary>
        public bool IsLocked => _locked;
        /// <summary>Locked cell index, or -1 when none.</summary>
        public int LockedCellIndex => _lockedCellIndex;

        private static readonly int BASE_COLOR = Shader.PropertyToID("_BaseColor");
        private static readonly int EMISSION_COLOR = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _mpb;
        private bool _locked;
        private bool _launched;
        private bool _waitingResult;
        private bool _resultKnown;
        private bool _resultIsHit;
        private int _lockedCellIndex = -1;
        private float _dropT;
        private Vector3 _missileLocalPos;

        /// <summary>Initializes material state, VFX visibility, and missile position.</summary>
        private void Awake()
        {
            if (_quadRenderer == null) _quadRenderer = GetComponentInChildren<MeshRenderer>(true);
            if (_quadRenderer != null && _quadRenderer.sharedMaterial != null)
                _quadRenderer.sharedMaterial.EnableKeyword("_EMISSION");

            _mpb = new MaterialPropertyBlock();
            SetWhite();
            SetActive(_missVfx, false);
            SetActive(_hitVfx, false);

            if (_missileRoot != null)
            {
                _missileLocalPos = _missileRoot.localPosition;
                _missileLocalPos.z = _missileStartLocalZ;
                _missileRoot.localPosition = _missileLocalPos;
            }

            if (_quadRenderer != null) _quadRenderer.enabled = false;
            gameObject.SetActive(true);
        }

        /// <summary>Animates blink while locked and handles missile drop timing.</summary>
        private void Update()
        {
            if (_locked && !_launched)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Mathf.PI * 2f * _blinkHz * Time.time);
                SetBlink(k);
            }

            if (_launched)
            {
                _dropT += Time.deltaTime;
                float u = Mathf.Clamp01(_dropT / Mathf.Max(0.01f, _missileDropTime));
                float z = Mathf.Lerp(_missileStartLocalZ, 0f, u);

                if (_missileRoot != null)
                {
                    _missileLocalPos = _missileRoot.localPosition;
                    _missileLocalPos.z = z;
                    _missileRoot.localPosition = _missileLocalPos;
                }

                if (u >= 1f && !_waitingResult)
                {
                    _waitingResult = true;
                    if (_resultKnown) PlayImpactAndFinish();
                }
            }
        }

        /// <summary>Shows hover at world position without committing lock.</summary>
        public void ShowHover(Vector3 worldPos, int cellIndex)
        {
            if (_locked) return;
            transform.position = worldPos;
            _lockedCellIndex = cellIndex;
            SetWhite();
            if (_quadRenderer != null && !_quadRenderer.enabled) _quadRenderer.enabled = true;
        }

        /// <summary>Enters locked state and primes missile/VFX.</summary>
        public void Lock()
        {
            if (_locked) return;
            _locked = true;
            _launched = false;
            _waitingResult = false;
            _resultKnown = false;
            _resultIsHit = false;
            ResetMissile();
            SetActive(_missVfx, false);
            SetActive(_hitVfx, false);
        }

        /// <summary>Cancels lock before launch and restores idle visuals.</summary>
        public void Cancel()
        {
            if (!_locked || _launched) return;
            _locked = false;
            _waitingResult = false;
            _resultKnown = false;
            _resultIsHit = false;
            _lockedCellIndex = -1;
            SetWhite();
            ResetMissile();
            SetActive(_missVfx, false);
            SetActive(_hitVfx, false);
        }

        /// <summary>Starts missile drop animation from configured start Z.</summary>
        public void Launch()
        {
            if (!_locked || _launched) return;
            _launched = true;
            _dropT = 0f;
            SetWhite();
        }

        /// <summary>Supplies hit/miss result to be played once drop completes.</summary>
        public void ApplyResult(bool isHit)
        {
            _resultKnown = true;
            _resultIsHit = isHit;
            if (_launched && _waitingResult) PlayImpactAndFinish();
        }

        /// <summary>Plays an incoming impact at a world position (defender side feedback).</summary>
        public void PlayIncoming(Vector3 impactWorldPos, bool isHit)
        {
            transform.position = impactWorldPos;
            _lockedCellIndex = -1;

            if (_quadRenderer != null && !_quadRenderer.enabled) _quadRenderer.enabled = true;

            _locked = true;
            _launched = true;
            _waitingResult = false;
            _resultKnown = true;
            _resultIsHit = isHit;
            _dropT = 0f;

            ResetMissile();
            SetWhite();
        }

        /// <summary>Hides all visuals and resets internal state.</summary>
        public void HideAll()
        {
            _locked = false;
            _launched = false;
            _waitingResult = false;
            _resultKnown = false;
            _resultIsHit = false;
            _lockedCellIndex = -1;
            SetWhite();
            ResetMissile();
            SetActive(_missVfx, false);
            SetActive(_hitVfx, false);
            if (_quadRenderer != null) _quadRenderer.enabled = false;
        }

        private void ResetMissile()
        {
            if (_missileRoot == null) return;
            _missileLocalPos = _missileRoot.localPosition;
            _missileLocalPos.z = _missileStartLocalZ;
            _missileRoot.localPosition = _missileLocalPos;
        }

        private void SetWhite()
        {
            SetColors(Color.white, Color.white * _emissionIntensity);
        }

        private void SetBlink(float k01)
        {
            Color baseCol = Color.Lerp(Color.white, Color.red, k01);
            Color emisCol = baseCol * _emissionIntensity;
            SetColors(baseCol, emisCol);
        }

        private void SetColors(Color baseColor, Color emissionColor)
        {
            if (_quadRenderer == null) return;
            _quadRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BASE_COLOR, baseColor);
            _mpb.SetColor(EMISSION_COLOR, emissionColor);
            _quadRenderer.SetPropertyBlock(_mpb);
        }

        private void PlayImpactAndFinish()
        {
            _waitingResult = false;
            SetActive(_hitVfx, _resultIsHit);
            SetActive(_missVfx, !_resultIsHit);
            ResetMissile();
            Invoke(nameof(ResetAfterImpact), 3.0f);
        }

        private void ResetAfterImpact()
        {
            _launched = false;
            _locked = false;
            SetActive(_hitVfx, false);
            SetActive(_missVfx, false);
            SetWhite();
        }

        private static void SetActive(GameObject go, bool v)
        {
            if (go != null && go.activeSelf != v) go.SetActive(v);
        }
    }
}
