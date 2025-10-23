using UnityEngine;

namespace BattleshipsVR.Visuals
{
    /// <summary>
    /// Grid overlay that writes hit/miss marks into a texture and applies it to a renderer via MaterialPropertyBlock.
    /// </summary>
    public sealed class GridOverlayMarks : MonoBehaviour
    {
        [SerializeField, Tooltip("Target renderer using a shader with a _MarkTex sampler.")]
        private Renderer _renderer;
        [SerializeField, Tooltip("Grid width in cells (X).")]
        private int _nx = 10;
        [SerializeField, Tooltip("Grid height in cells (Y).")]
        private int _ny = 10;
        [SerializeField, Tooltip("Flip X when using index-based marking.")]
        private bool _flipX = false;
        [SerializeField, Tooltip("Flip Y when using index-based marking.")]
        private bool _flipY = false;

        private Texture2D _mask;
        private MaterialPropertyBlock _mpb;
        private static readonly int MARK_TEX_ID = Shader.PropertyToID("_MarkTex");

        /// <summary>Initializes the mark texture and applies a clear state.</summary>
        private void Awake()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            _mask = new Texture2D(_nx, _ny, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _mpb = new MaterialPropertyBlock();
            Clear();
        }

        /// <summary>Marks a grid cell (by linear index) as a hit.</summary>
        public void MarkHit(int cellIndex) => SetCellIndexed(cellIndex, new Color(1f, 0f, 0f, 1f));

        /// <summary>Marks a grid cell (by linear index) as a miss.</summary>
        public void MarkMiss(int cellIndex) => SetCellIndexed(cellIndex, new Color(1f, 1f, 1f, 1f));

        /// <summary>Marks a grid cell at (gx, gy) directly; caller passes final (already flipped) coordinates.</summary>
        public void MarkCell(int gx, int gy, bool isHit)
        {
            if ((uint)gx >= _nx || (uint)gy >= _ny) return;
            Color c = isHit ? new Color(1f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
            _mask.SetPixel(gx, gy, c);
            _mask.Apply(false, false);
            Apply();
        }

        /// <summary>Clears all marks from the overlay.</summary>
        public void Clear()
        {
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < _ny; y++)
                for (int x = 0; x < _nx; x++)
                    _mask.SetPixel(x, y, clear);

            _mask.Apply(false, false);
            Apply();
        }

        /// <summary>Sets horizontal flip for index-based marking.</summary>
        public void SetFlipX(bool v) => _flipX = v;

        /// <summary>Sets vertical flip for index-based marking.</summary>
        public void SetFlipY(bool v) => _flipY = v;

        private void SetCellIndexed(int idx, Color c)
        {
            if (idx < 0) return;
            int x = idx % _nx;
            int y = idx / _nx;

            if (_flipX) x = (_nx - 1) - x;
            if (_flipY) y = (_ny - 1) - y;

            if ((uint)x >= _nx || (uint)y >= _ny) return;
            _mask.SetPixel(x, y, c);
            _mask.Apply(false, false);
            Apply();
        }

        private void Apply()
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(MARK_TEX_ID, _mask);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
