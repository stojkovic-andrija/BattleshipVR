using System.Collections;
using UnityEngine;

public class CameraFinder : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        if(Camera.main.enabled) canvas.worldCamera = Camera.main;
    }
}
