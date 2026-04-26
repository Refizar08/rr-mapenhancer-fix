using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;
using System;

namespace MapEnhancer;

internal sealed class TurntableRequestStorage : IPropertyAccessControlDelegate, IDisposable
{
    public const string ObjectId = "_mapEnhancer.turntableRequests";

    private readonly KeyValueObject _keyValueObject;

    public TurntableRequestStorage(KeyValueObject keyValueObject)
    {
        _keyValueObject = keyValueObject;
        StateManager.Shared.RegisterPropertyObject(ObjectId, keyValueObject, this);
    }

    public AuthorizationRequirementInfo AuthorizationRequirementForPropertyWrite(string key)
    {
        return AuthorizationRequirement.MinimumLevelCrew;
    }

    public void Dispose()
    {
        if (_keyValueObject != null)
        {
            UnityEngine.Object.DestroyImmediate(_keyValueObject);
            StateManager.Shared.UnregisterPropertyObject(ObjectId);
        }
    }

    public void SendRequest(TurntableRequestState request)
    {
        _keyValueObject[request.TurntableId] = request.ToPropertyValue();
    }

    public IDisposable ObserveRequest(string turntableId, Action<TurntableRequestState?> action, bool callInitial)
    {
        return _keyValueObject.Observe(turntableId, value =>
        {
            action(TurntableRequestState.FromPropertyValue(value));
        }, callInitial);
    }
}
