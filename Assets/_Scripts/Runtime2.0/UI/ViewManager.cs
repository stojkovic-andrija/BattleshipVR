using UnityEngine;

public class ViewManager : MonoBehaviour
{
    public static ViewManager Instance { get; private set; }

    [SerializeField] private View[] _views;
    [SerializeField] private View _defaultView;
    [SerializeField] private View _currentView;
    [SerializeField] private bool _autoInitialize;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (_autoInitialize) Initialize();
    }

    public void Initialize()
    {
        foreach (View view in _views)
        {
            view.Initialize();

            view.Hide();
        }

        if (_defaultView != null) _defaultView.Show(_defaultView);
    }

    public void Show<TView>(object args = null) where TView : View
    {
        foreach (View view in _views)
        {
            if (view is TView) continue;

            if (_currentView != null) _currentView.Hide();

            view.Show();

            _currentView = view;

            break;
        }
    }

    public void Show(View view, object args = null)
    {
        if (_currentView != null) _currentView.Hide();

        view.Show(args);

        _currentView = view;
    }
}
