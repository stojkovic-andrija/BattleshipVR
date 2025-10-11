using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BattleshipsVR.Core;
using BattleshipsVR.Bootstrap;

namespace BattleshipsVR.Dev.UI
{
    /// <summary>UX helper for join code: Enter submits, Paste pulls from clipboard</summary>
    public sealed class DevJoinCodeUX : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _joinCodeField;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _pasteButton;
        [SerializeField] private NetworkEntry _entry;

        private void OnEnable()
        {
            if (_joinButton != null) _joinButton.onClick.AddListener(JoinNow);
            if (_pasteButton != null) _pasteButton.onClick.AddListener(PasteClipboard);
        }

        private void OnDisable()
        {
            if (_joinButton != null) _joinButton.onClick.RemoveListener(JoinNow);
            if (_pasteButton != null) _pasteButton.onClick.RemoveListener(PasteClipboard);
        }

        private void Update()
        {
            if (_joinCodeField != null && Input.GetKeyDown(KeyCode.Return))
                JoinNow();
        }

        private void JoinNow()
        {
            if (_entry == null || _joinCodeField == null) return;

            string code = _joinCodeField.text.Trim();
            if (string.IsNullOrEmpty(code))
            {
                AppLogger.Warn("Join code is empty");
                return;
            }

            _entry.StartClientWithCode(code);
        }

        private void PasteClipboard()
        {
            if (_joinCodeField == null) return;
            _joinCodeField.text = GUIUtility.systemCopyBuffer.Trim();
        }
    }
} 
