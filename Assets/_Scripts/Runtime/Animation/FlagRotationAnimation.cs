using UnityEngine;
using Zenject;

[ExecuteAlways]
/// <summary>
/// Procedural flag sway: time-varying Euler rotation driven by Perlin-modulated sine waves,
/// with optional debug path preview of the pole tip.
/// </summary>
public sealed class FlagRotationAnimation : MonoBehaviour
{
    [Header("Motion Settings")]
    [SerializeField] private float _baseSpeed = 1.5f;
    [SerializeField, Range(0f, 2f)] private float _speedVariance = 0.35f;
    [SerializeField] private float _amplitude = 10f;
    [SerializeField, Range(0f, 1f)] private float _amplitudeVariance = 0.3f;

    [Header("Perlin Noise")]
    [SerializeField] private float _noiseFrequency = 0.25f;
    [SerializeField] private int _noiseSeedSpeed = 1234;
    [SerializeField] private int _noiseSeedAmp = 9876;

    [Header("Axis Weights (deg scale from Amplitude)")]
    [SerializeField] private Vector3 _axisScale = new Vector3(1.0f, 0.45f, 0.8f);

    [Header("Phase Offsets (rad)")]
    [SerializeField] private Vector3 _phase = new Vector3(0f, 1.2f, 2.0f);

    [Header("Debug Preview")]
    [SerializeField] private bool _showDebugPath = true;
    [SerializeField] private float _poleLength = 1f;
    [SerializeField] private int _debugResolution = 48;

    private float _t;
    private Quaternion _baseRot;
    private float _editorAccum;

    /// <summary>Cache initial local rotation as the baseline for additive sway.</summary>
    private void Awake()
    {
        _baseRot = transform.localRotation;
    }

    /// <summary>Clamp serialized values to safe ranges for editor previews.</summary>
    private void OnValidate()
    {
        if (_debugResolution < 8) _debugResolution = 8;
        if (_noiseFrequency < 0.001f) _noiseFrequency = 0.001f;
    }

    /// <summary>Advance time, compute Euler sway, and apply as local rotation.</summary>
    private void Update()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;

        float nSpeed = Perlin01(Time.time * _noiseFrequency, _noiseSeedSpeed);
        float speedMul = 1f + (nSpeed * 2f - 1f) * _speedVariance;

        _t += dt * _baseSpeed * speedMul;
        if (!Application.isPlaying)
            _editorAccum += dt * _baseSpeed * speedMul;

        float nAmp = Perlin01(Time.time * _noiseFrequency, _noiseSeedAmp);
        float ampMul = 1f + (nAmp * 2f - 1f) * _amplitudeVariance;

        float ax = _amplitude * _axisScale.x * ampMul;
        float ay = _amplitude * _axisScale.y * ampMul;
        float az = _amplitude * _axisScale.z * ampMul;

        float t = Application.isPlaying ? _t : _editorAccum;

        float rx = Mathf.Sin(t + _phase.x) * ax;
        float ry = Mathf.Sin(t * 0.66f + _phase.y) * ay + Mathf.Sin(t * 0.18f) * ay * 0.35f;
        float rz = Mathf.Sin(t * 0.5f + _phase.z) * az;

        Quaternion q = Quaternion.Euler(rx, ry, rz);
        transform.localRotation = _baseRot * q;
    }

    /// <summary>Draws the pole tip trajectory for one period using current settings.</summary>
    private void OnDrawGizmos()
    {
        if (!_showDebugPath)
            return;

        Gizmos.color = Color.white;

        float nAmp = Perlin01((Application.isPlaying ? Time.time : _editorAccum) * _noiseFrequency, _noiseSeedAmp);
        float ampMul = 1f + (nAmp * 2f - 1f) * _amplitudeVariance;

        float ax = _amplitude * _axisScale.x * ampMul;
        float ay = _amplitude * _axisScale.y * ampMul;
        float az = _amplitude * _axisScale.z * ampMul;

        Vector3 origin = transform.position;
        Quaternion baseWorld = transform.parent ? transform.parent.rotation * _baseRot : _baseRot;

        Vector3 prev = origin;
        int steps = _debugResolution;
        float loop = Mathf.PI * 2f;

        for (int i = 0; i <= steps; i++)
        {
            float tt = (i / (float)steps) * loop;

            float rx = Mathf.Sin(tt + _phase.x) * ax;
            float ry = Mathf.Sin(tt * 0.66f + _phase.y) * ay + Mathf.Sin(tt * 0.18f) * ay * 0.35f;
            float rz = Mathf.Sin(tt * 0.5f + _phase.z) * az;

            Quaternion q = Quaternion.Euler(rx, ry, rz);
            Vector3 tipDir = (baseWorld * q) * Vector3.up;
            Vector3 tip = origin + tipDir * _poleLength;

            if (i > 0) Gizmos.DrawLine(prev, tip);
            prev = tip;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + (transform.rotation * Vector3.up) * _poleLength);
    }

    /// <summary>Perlin noise remapped to [0,1], seeded via Y input.</summary>
    private float Perlin01(float t, int seed)
    {
        return Mathf.PerlinNoise(t, seed * 0.0132719f);
    }
}
