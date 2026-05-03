using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;
using System;
using System.Linq;

namespace MapEnhancer;

internal sealed class SwitchResetAuditStorage : IPropertyAccessControlDelegate, IDisposable
{
    public const string ObjectId = "_mapEnhancer.switchResetAudit";

    private readonly KeyValueObject _keyValueObject;

    public SwitchResetAuditStorage(KeyValueObject keyValueObject)
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

    public void Write(string key, SwitchResetAuditState state)
    {
        _keyValueObject[key] = state.ToPropertyValue();
    }

    public string[] Keys()
    {
        return _keyValueObject.Keys.ToArray();
    }

    public bool TryRead(string key, out SwitchResetAuditState? state)
    {
        state = null;
        if (!_keyValueObject.Keys.Contains(key))
        {
            return false;
        }

        state = SwitchResetAuditState.FromPropertyValue(_keyValueObject[key]);
        return true;
    }

    public IDisposable ObserveRequests(Action<string, SwitchResetAuditState?> action)
    {
        return _keyValueObject.ObserveKeyChanges((key, keyChange) =>
        {
            if (keyChange == KeyChange.Remove || !_keyValueObject.Keys.Contains(key))
            {
                return;
            }

            action(key, SwitchResetAuditState.FromPropertyValue(_keyValueObject[key]));
        });
    }
}
