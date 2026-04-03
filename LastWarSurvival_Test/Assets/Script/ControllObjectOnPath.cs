using System;
using Unity.VisualScripting;
using UnityEngine;


/// <summary>
/// lấy data từ listPoint và điều khiển các object trong root trên path
/// </summary>
public class ControllObjectOnPath : MonoBehaviour
{
    private PointBaker _pointBaker;
    private ListPoint _listPoint;

    // nơi chứa các object cần điều khiển
    private Transform rootSpawnObject;
    
    private void Start()
    {
        TryGetPointBaker();
        _listPoint = _pointBaker.listPoint;
    }

    private void TryGetPointBaker()
    {
        try
        {
            if (!_pointBaker.IsUnityNull()) return;
            gameObject.TryGetComponent(out _pointBaker);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
