using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Zenject;
using BattleshipsVR.Audio;

namespace BattleshipsVR.UI
{
    /// <summary>
    /// Fades in the lobby UI: first the main element (optionally with scale), then other elements
    /// with staggered delays. Can be restarted via <see cref="PlayAnimation"/>.
    /// </summary>
    public sealed class LobbyUIAnimator : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private CanvasGroup _mainElement;
        [SerializeField] private List<CanvasGroup> _otherElements = new();

        [Header("Animation Settings")]
        [SerializeField] private float _fadeDuration = 0.6f;
        [SerializeField] private float _startDelay = 0.25f;
        [SerializeField] private float _perElementDelay = 0.1f;
        [SerializeField] private AnimationCurve _fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Special Animation (Main Element)")]
        [SerializeField] private bool _useScaleAnimation = true;
        [SerializeField] private AnimationCurve _scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private float _scaleMultiplier = 1.1f;

        [Inject] private AudioManager _audioManager;
        private CancellationTokenSource _cts;

        /// <summary>Starts the lobby intro animation.</summary>
        private void Start()
        {
            PlayAnimation();
        }

        /// <summary>Cancels any running animation when disabled.</summary>
        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        /// <summary>Restarts the full lobby animation sequence.</summary>
        public void PlayAnimation()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            AnimateLobbyAsync(_cts.Token).Forget();
        }

        /// <summary>Runs the main element animation, then staggers the rest.</summary>
        private async UniTaskVoid AnimateLobbyAsync(CancellationToken token)
        {
            if (_mainElement == null)
                return;

            if (_useScaleAnimation)
                await AnimateFadeAndScaleAsync(_mainElement, _fadeDuration, _scaleCurve, _scaleMultiplier, token);
            else
                await AnimateFadeAsync(_mainElement, _fadeDuration, _fadeCurve, token);

            await UniTask.Delay(System.TimeSpan.FromSeconds(_startDelay), cancellationToken: token);

            for (int i = 0; i < _otherElements.Count; i++)
            {
                CanvasGroup cg = _otherElements[i];
                if (cg == null)
                    continue;

                AnimateFadeAsync(cg, _fadeDuration, _fadeCurve, token).Forget();
                await UniTask.Delay(System.TimeSpan.FromSeconds(_perElementDelay), cancellationToken: token);
            }
        }

        /// <summary>Fades a CanvasGroup from 0 to 1 using a curve over a duration.</summary>
        private async UniTask AnimateFadeAsync(CanvasGroup group, float duration, AnimationCurve curve, CancellationToken token)
        {
            group.alpha = 0f;
            float time = 0f;

            while (time < duration)
            {
                if (token.IsCancellationRequested)
                    return;

                float t = time / duration;
                group.alpha = curve.Evaluate(t);
                time += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            group.alpha = 1f;
        }

        /// <summary>Fades and scales a CanvasGroup, then restores the scale; plays a subtle audio cue at the end.</summary>
        private async UniTask AnimateFadeAndScaleAsync(CanvasGroup group, float duration, AnimationCurve curve, float scaleMul, CancellationToken token)
        {
            group.alpha = 0f;
            Transform t = group.transform;
            Vector3 baseScale = t.localScale;
            float time = 0f;

            while (time < duration)
            {
                if (token.IsCancellationRequested)
                    return;

                float t01 = time / duration;
                float eval = curve.Evaluate(t01);
                group.alpha = eval;
                t.localScale = baseScale * Mathf.Lerp(1f, scaleMul, eval);
                time += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            group.alpha = 1f;
            t.localScale = baseScale;

            _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.StoneFall, .2f);
        }
    }
}
