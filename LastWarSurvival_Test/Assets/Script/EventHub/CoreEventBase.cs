// Cần cải tiến CoreEventBase để tách ra khỏi UnityEngine

using UnityEngine;

public interface ICoreEventListener
{
    EventBinder Binder { get; }
    void SubscribeEvents();
    //void UnsubscribeEvents();
}

/// <summary>
/// CoreEventBase hỗ trợ đăng ký và hủy đăng ký sự kiện một cách tự động thông qua EventBinder.
/// CoreEventBase sẽ tự động thêm EventBinder nếu chưa có trong GameObject.
/// CoreEventBase kế thừa MonoBehaviour để sử dụng trong Unity.
/// </summary>
/// <remarks>
/// Vd: public class MyEventListener : CoreEventBase.
/// Override SubscribeEvents() để đăng ký sự kiện.
/// Trong SubscribeEvents() dùng Binder để auto đăng ký và hủy đăng ký sự kiện.
/// Vd: CoreEvents.OnSomeEvent.Subscribe(e => HandleSomeEvent(e), Binder);
/// </remarks>
public abstract class CoreEventBase : MonoBehaviour, ICoreEventListener
{
    private EventBinder _binder;
    public EventBinder Binder => _binder;

    protected virtual void Awake()
    {
        if (!_binder && !TryGetComponent(out _binder)) // an toàn, không add trùng
            _binder = gameObject.AddComponent<EventBinder>();
    }

    protected virtual void OnEnable() => SubscribeEvents();
    //protected virtual void OnDisable() => UnsubscribeEvents();
    public abstract void SubscribeEvents();
    //public abstract void UnsubscribeEvents();
}