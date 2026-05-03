using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System;
using Game.AccessControl;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Helpers;
using RollingStock;
using Model.Physics;
using Network;
using Track;
using UnityEngine;
using MapEnhancer.UMM;

namespace MapEnhancer;

public class TurntableHelper : MonoBehaviour
{
    private sealed class PendingTurntableRequest
    {
        public PendingTurntableRequest(int trackNodeIndex, int sequence, string context)
        {
            TrackNodeIndex = trackNodeIndex;
            Sequence = sequence;
            Context = context;
        }

        public int TrackNodeIndex { get; }

        public int Sequence { get; }

        public string Context { get; }
    }

    private TurntableController _controller;
    private List<TrackNode> _nodes;
    private HashSet<int> _activeIndexes;
    private bool _isInitialized = false;

    private float? _targetAngle;
    private int _targetIndex;
    private bool _isRotating = false;
    private float _lastRotationTime = 0f;
    private float _rotationStartedAt = 0f;
    private float _fallbackFromAngle = 0f;
    private float _fallbackDurationSec = 0.25f;
    private float _lastNetworkAngleSyncTime = 0f;
    private IDisposable _requestObserver;
    private int _requestSequence = 0;
    private readonly Queue<PendingTurntableRequest> _pendingRequests = new Queue<PendingTurntableRequest>();

    private const float ROTATION_COOLDOWN = 0.5f;
    private const float SMOOTH_ROTATION_SPEED_DEG_PER_SEC = 30f;
    private const float NETWORK_ANGLE_SYNC_INTERVAL_SEC = 0.2f;
    private const int MAX_PENDING_SYNC_REQUESTS = 8;

    public TurntableController Controller => _controller;
    public List<int> ActiveTrackIndexes => _activeIndexes?.ToList() ?? new List<int>();
    public bool IsInitialized => _isInitialized;
    public System.Action OnRotationComplete;

    public void Awake()
    {
        EnsureInitialized(logFailures: true);
    }

    public bool EnsureInitialized(bool logFailures = false)
    {
        if (_isInitialized)
        {
            return true;
        }

        _controller = GetComponent<TurntableController>();
        if (_controller == null || _controller.turntable == null)
        {
            if (logFailures)
            {
                Loader.Log($"TurntableHelper initialization FAILED: No controller or turntable found on {gameObject.name}");
            }

            return false;
        }

        _nodes = Traverse.Create(_controller.turntable).Field<List<TrackNode>>("nodes").Value;
        if (_nodes == null || _nodes.Count == 0)
        {
            if (logFailures)
            {
                Loader.Log($"TurntableHelper initialization FAILED: No nodes found for turntable at {_controller.turntable.transform.position}");
            }

            return false;
        }

        _activeIndexes = new HashSet<int>();
        for (var i = 0; i < _nodes.Count; i++)
        {
            var j = (i + _nodes.Count / 2) % _nodes.Count;
            var nodeIConnections = Graph.Shared.SegmentsConnectedTo(_nodes[i]).Count;
            var nodeJConnections = Graph.Shared.SegmentsConnectedTo(_nodes[j]).Count;

            if (nodeIConnections > 0 || nodeJConnections > 0)
            {
                _activeIndexes.Add(i);
                _activeIndexes.Add(j);
            }
        }

        var position = _controller.turntable.transform.position;
        _isInitialized = true;
        Loader.Log($"Turntable initialized at ({position.x:F2}, {position.y:F2}, {position.z:F2}) with {_activeIndexes.Count} active track connections");
        RegisterTurntableRequestObserver();
        return true;
    }

    public bool MoveToIndex(int trackNodeIndex)
    {
        RegisterTurntableRequestObserver();

        if (_isRotating)
        {
            Loader.LogDebug("TurntableHelper: Rotation already in progress, ignoring command");
            return false;
        }

        var timeSinceLastRotation = Time.time - _lastRotationTime;
        if (timeSinceLastRotation < ROTATION_COOLDOWN)
        {
            Loader.LogDebug($"TurntableHelper: Cooldown active ({ROTATION_COOLDOWN - timeSinceLastRotation:F2}s remaining)");
            return false;
        }

        if (_controller == null || _controller.turntable == null)
        {
            Loader.Log("Turntable rotation FAILED: No controller available");
            return false;
        }

        if (_nodes == null || trackNodeIndex < 0 || trackNodeIndex >= _nodes.Count)
        {
            Loader.Log($"Turntable rotation FAILED: Invalid track node index {trackNodeIndex}");
            return false;
        }

        if (IsTurntableFouled(out var foulReason))
        {
            Loader.Log($"Turntable rotation BLOCKED: {foulReason}");
            return false;
        }

        if (IsMultiplayer())
        {
            if (TrySendMultiplayerTurntableRequest(trackNodeIndex))
            {
                return true;
            }

            Loader.Log($"Turntable rotation BLOCKED: Could not send multiplayer request for track {trackNodeIndex}");
            return false;
        }

        return StartTurntableRequest(trackNodeIndex, "single-player only");
    }

    private bool BeginLocalRotation(int trackNodeIndex, string context)
    {
        Loader.Log($"WARNING: Rotating turntable to track {trackNodeIndex} using smooth local fallback ({context})");
        var currentIndex = GetCurrentIndex();
        _fallbackFromAngle = currentIndex >= 0
            ? _controller.turntable.AngleForIndex(currentIndex)
            : _controller.turntable.AngleForIndex(trackNodeIndex);
        _targetAngle = _controller.turntable.AngleForIndex(trackNodeIndex);
        _controller.turntable.SetStopIndex(null);
        var delta = Mathf.Abs(Mathf.DeltaAngle(_fallbackFromAngle, _targetAngle.Value));
        _fallbackDurationSec = Mathf.Max(0.2f, delta / SMOOTH_ROTATION_SPEED_DEG_PER_SEC);
        _lastNetworkAngleSyncTime = 0f;
        SyncNetworkStopIndex(_fallbackFromAngle, null);
        return true;
    }

    private bool TrySendMultiplayerTurntableRequest(int trackNodeIndex)
    {
        var storage = MapEnhancer.Instance?.TurntableSyncStorage;
        if (storage == null)
        {
            Loader.Log("TurntableHelper: Multiplayer turntable request storage is not available");
            return false;
        }

        var turntableId = GetTurntableId();
        if (string.IsNullOrWhiteSpace(turntableId))
        {
            Loader.Log($"TurntableHelper: Unable to resolve turntable id for multiplayer request to track {trackNodeIndex}");
            return false;
        }

        var request = new TurntableRequestState(turntableId, trackNodeIndex, ++_requestSequence);
        if (Multiplayer.IsHost)
        {
            Loader.Log($"Rotating turntable to track {trackNodeIndex} (host applying request for '{turntableId}', seq {request.Sequence})");
            ApplyTurntableRequest(request, "host applying map request");
            return true;
        }

        Loader.Log($"Rotating turntable to track {trackNodeIndex} (request queued for '{turntableId}', seq {request.Sequence})");
        StateManager.ApplyLocal(new PropertyChange(TurntableRequestStorage.ObjectId, turntableId, PropertyValueConverter.RuntimeToSnapshot(request.ToPropertyValue())));
        return true;
    }

    public void FixedUpdate()
    {
        if (_controller == null || _controller.turntable == null)
        {
            return;
        }

        if (_isRotating && _targetAngle.HasValue)
        {
            var elapsed = Time.time - _rotationStartedAt;
            var t = Mathf.Clamp01(elapsed / _fallbackDurationSec);
            var nextAngle = Mathf.LerpAngle(_fallbackFromAngle, _targetAngle.Value, t);

            _controller.SetAngle(nextAngle);
            SyncNetworkAngle(nextAngle);

            if (t >= 1f)
            {
                _controller.SetAngle(_targetAngle.Value);
                _controller.turntable.SetStopIndex(_targetIndex);
                SyncNetworkStopIndex(_targetAngle.Value, _targetIndex);
                _targetAngle = null;
                CompleteRotation($"Turntable rotation completed at track index {_targetIndex}");
            }
        }
    }

    private void SyncNetworkAngle(float angle)
    {
        if (!Multiplayer.IsHost || StateManager.Shared == null || _controller?.turntable == null)
        {
            return;
        }

        if (Time.time - _lastNetworkAngleSyncTime < NETWORK_ANGLE_SYNC_INTERVAL_SEC)
        {
            return;
        }

        _lastNetworkAngleSyncTime = Time.time;
        StateManager.ApplyLocal(new TurntableUpdateAngle(_controller.turntable.id, StateManager.Now, angle));
    }

    private void SyncNetworkStopIndex(float angle, int? stopIndex)
    {
        if (!Multiplayer.IsHost || StateManager.Shared == null || _controller?.turntable == null)
        {
            return;
        }

        StateManager.ApplyLocal(new TurntableUpdateStopIndex(_controller.turntable.id, StateManager.Now, angle, stopIndex));
    }

    private void CompleteRotation(string message)
    {
        _isRotating = false;
        Loader.Log(message);
        OnRotationComplete?.Invoke();
        StartNextQueuedRequest();
    }

    private void RegisterTurntableRequestObserver()
    {
        if (_requestObserver != null)
        {
            return;
        }

        var storage = MapEnhancer.Instance?.TurntableSyncStorage;
        if (storage == null)
        {
            return;
        }

        var turntableId = GetTurntableId();
        if (string.IsNullOrWhiteSpace(turntableId))
        {
            return;
        }

        _requestObserver = storage.ObserveRequest(turntableId, OnTurntableRequestChanged, false);
    }

    private void OnTurntableRequestChanged(TurntableRequestState? request)
    {
        if (request == null)
        {
            return;
        }

        var requestTurntableId = request.TurntableId ?? string.Empty;
        if (!string.Equals(requestTurntableId, GetTurntableId(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyTurntableRequest(request, Multiplayer.IsHost ? "host applying synced request" : "client applying synced request");
    }

    private void ApplyTurntableRequest(TurntableRequestState request, string context)
    {
        Loader.Log($"Turntable request received for '{request.TurntableId}' -> track {request.TrackNodeIndex} (seq {request.Sequence})");

        if (_isRotating)
        {
            EnqueueRequest(request, context);
            return;
        }

        StartTurntableRequest(request.TrackNodeIndex, context);
    }

    private void EnqueueRequest(TurntableRequestState request, string context)
    {
        if (_targetIndex == request.TrackNodeIndex)
        {
            Loader.LogDebug($"TurntableHelper: Request seq {request.Sequence} matches in-flight target {_targetIndex}, ignoring duplicate");
            return;
        }

        if (_pendingRequests.Any(pending => pending.Sequence == request.Sequence && pending.TrackNodeIndex == request.TrackNodeIndex))
        {
            Loader.LogDebug($"TurntableHelper: Request seq {request.Sequence} already queued, skipping duplicate");
            return;
        }

        if (_pendingRequests.Count >= MAX_PENDING_SYNC_REQUESTS)
        {
            _pendingRequests.Dequeue();
            Loader.LogDebug("TurntableHelper: Pending request queue full, dropped oldest synced request");
        }

        _pendingRequests.Enqueue(new PendingTurntableRequest(request.TrackNodeIndex, request.Sequence, context));
        Loader.LogDebug($"TurntableHelper: Queued synced request seq {request.Sequence} for track {request.TrackNodeIndex}");
    }

    private bool StartTurntableRequest(int trackNodeIndex, string context)
    {
        if (_nodes == null || trackNodeIndex < 0 || trackNodeIndex >= _nodes.Count)
        {
            Loader.Log($"Turntable rotation FAILED: Invalid track node index {trackNodeIndex}");
            return false;
        }

        _isRotating = true;
        _lastRotationTime = Time.time;
        _rotationStartedAt = Time.time;
        _targetIndex = trackNodeIndex;
        if (!BeginLocalRotation(trackNodeIndex, context))
        {
            _isRotating = false;
            return false;
        }

        return true;
    }

    private void StartNextQueuedRequest()
    {
        while (_pendingRequests.Count > 0)
        {
            var pending = _pendingRequests.Dequeue();
            if (GetCurrentIndex() == pending.TrackNodeIndex)
            {
                Loader.LogDebug($"TurntableHelper: Skipping queued request seq {pending.Sequence} because turntable is already at track {pending.TrackNodeIndex}");
                continue;
            }

            if (StartTurntableRequest(pending.TrackNodeIndex, pending.Context))
            {
                return;
            }
        }
    }

    private bool IsMultiplayer()
    {
        var shared = StateManager.Shared;
        if (shared == null)
        {
            return false;
        }

        var storage = shared.Storage;
        if (storage != null)
        {
            // Prefer explicit mode booleans if present.
            bool? storageBoolMode = TryGetBoolMember(storage,
                "IsMultiplayer",
                "IsNetworked",
                "IsOnline",
                "IsOnlineSession",
                "IsClient",
                "IsHost",
                "IsServer",
                "Multiplayer");
            if (storageBoolMode.HasValue)
            {
                return storageBoolMode.Value;
            }

            // Fall back to mode text/enum names often used by game state models.
            string storageMode = TryGetTextMember(storage,
                "Mode",
                "GameMode",
                "SessionMode",
                "ConnectionMode",
                "StorageMode",
                "Type",
                "SessionType");
            bool? parsedStorageMode = ParseModeText(storageMode);
            if (parsedStorageMode.HasValue)
            {
                return parsedStorageMode.Value;
            }
        }

        var playersManager = shared._playersManager;
        if (playersManager == null)
        {
            return false;
        }

        // Players manager may expose explicit mode flags even when only 1 player is connected.
        bool? playersBoolMode = TryGetBoolMember(playersManager,
            "IsMultiplayer",
            "IsNetworked",
            "IsOnline",
            "IsOnlineSession",
            "IsClient",
            "IsHost",
            "IsServer");
        if (playersBoolMode.HasValue)
        {
            return playersBoolMode.Value;
        }

        string playersMode = TryGetTextMember(playersManager,
            "Mode",
            "SessionMode",
            "ConnectionMode",
            "Type");
        bool? parsedPlayersMode = ParseModeText(playersMode);
        if (parsedPlayersMode.HasValue)
        {
            return parsedPlayersMode.Value;
        }

        // Fast path: try common member names first.
        int? count = TryGetPlayerCount(playersManager, "Players")
            ?? TryGetPlayerCount(playersManager, "AllPlayers")
            ?? TryGetPlayerCount(playersManager, "ConnectedPlayers")
            ?? TryGetPlayerCount(playersManager, "_players");

        // Reflection fallback for unknown internals across game versions.
        if (!count.HasValue)
        {
            count = TryGetEnumerableCount(playersManager);
        }

        // If we can determine player count, multiplayer means more than one participant.
        if (count.HasValue)
        {
            return count.Value > 1;
        }

        // Unknown shape: default to single-player-safe behavior so local control still works.
        Loader.LogDebug("TurntableHelper: Could not determine multiplayer player count, treating as single-player");
        return false;
    }

    private string GetTurntableId()
    {
        if (_controller == null || _controller.turntable == null)
        {
            return string.Empty;
        }

        var turntable = _controller.turntable;
        string turntableId = TryGetTextMember(turntable,
            "TurntableId",
            "turntableId",
            "Id",
            "id",
            "Name",
            "name");

        if (!string.IsNullOrWhiteSpace(turntableId))
        {
            return turntableId;
        }

        string controllerId = TryGetTextMember(_controller,
            "TurntableId",
            "turntableId",
            "Id",
            "id",
            "Name",
            "name");

        if (!string.IsNullOrWhiteSpace(controllerId))
        {
            return controllerId;
        }

        var position = _controller.turntable.transform.position;
        return $"tt:{Mathf.RoundToInt(position.x * 10f)}:{Mathf.RoundToInt(position.y * 10f)}:{Mathf.RoundToInt(position.z * 10f)}";
    }

    private bool? TryGetBoolMember(object obj, params string[] memberNames)
    {
        var type = obj.GetType();
        foreach (var memberName in memberNames)
        {
            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                object? value = null;
                try
                {
                    value = property.GetValue(obj);
                }
                catch
                {
                    value = null;
                }

                if (value is bool boolValue)
                {
                    return boolValue;
                }
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var value = field.GetValue(obj);
                if (value is bool boolValue)
                {
                    return boolValue;
                }
            }
        }

        return null;
    }

    private string TryGetTextMember(object obj, params string[] memberNames)
    {
        var type = obj.GetType();
        foreach (var memberName in memberNames)
        {
            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                object? value = null;
                try
                {
                    value = property.GetValue(obj);
                }
                catch
                {
                    value = null;
                }

                if (value != null)
                {
                    return value.ToString();
                }
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var value = field.GetValue(obj);
                if (value != null)
                {
                    return value.ToString();
                }
            }
        }

        return string.Empty;
    }

    private bool? ParseModeText(string modeText)
    {
        if (string.IsNullOrWhiteSpace(modeText))
        {
            return null;
        }

        if (modeText.IndexOf("single", StringComparison.OrdinalIgnoreCase) >= 0 ||
            modeText.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0 ||
            modeText.IndexOf("local", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        if (modeText.IndexOf("multi", StringComparison.OrdinalIgnoreCase) >= 0 ||
            modeText.IndexOf("online", StringComparison.OrdinalIgnoreCase) >= 0 ||
            modeText.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
            modeText.IndexOf("host", StringComparison.OrdinalIgnoreCase) >= 0 ||
            modeText.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0 ||
            modeText.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return null;
    }

    private int? TryGetPlayerCount(object obj, string memberName)
    {
        var type = obj.GetType();

        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
        {
            var value = property.GetValue(obj);
            var count = ExtractCount(value);
            if (count.HasValue)
            {
                return count.Value;
            }
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            var value = field.GetValue(obj);
            var count = ExtractCount(value);
            if (count.HasValue)
            {
                return count.Value;
            }
        }

        return null;
    }

    private int? TryGetEnumerableCount(object obj)
    {
        foreach (var property in obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object value;
            try
            {
                value = property.GetValue(obj);
            }
            catch
            {
                continue;
            }

            var count = ExtractCount(value);
            if (count.HasValue)
            {
                return count.Value;
            }
        }

        return null;
    }

    private int? ExtractCount(object value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is ICollection nonGenericCollection)
        {
            return nonGenericCollection.Count;
        }

        var valueType = value.GetType();
        var countProperty = valueType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        if (countProperty != null && countProperty.PropertyType == typeof(int))
        {
            return (int)countProperty.GetValue(value);
        }

        if (value is IEnumerable enumerable)
        {
            int count = 0;
            var enumerator = enumerable.GetEnumerator();
            while (enumerator.MoveNext())
            {
                count++;
                if (count > 8)
                {
                    break;
                }
            }
            return count;
        }

        return null;
    }

    private IPickable? FindPickable(TrackNode targetNode)
    {
        const float maxSearchDistance = 6f;
        var targetPos = targetNode.transform.position;
        var candidates = new List<(IPickable pickable, float dist)>();

        // Directly on target node
        var direct = targetNode.GetComponent<IPickable>();
        if (direct != null)
        {
            return direct;
        }

        // Search target node subtree first
        foreach (var behaviour in targetNode.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is IPickable localPickable)
            {
                var dist = Vector3.Distance(targetPos, behaviour.transform.position);
                if (dist <= maxSearchDistance)
                {
                    candidates.Add((localPickable, dist));
                }
            }
        }

        // Some multiplayer turntable prefabs place the actionable pickable elsewhere on the turntable root
        // instead of inside the track node subtree, so search the full turntable hierarchy as a fallback.
        if (_controller != null && _controller.turntable != null)
        {
            foreach (var behaviour in _controller.turntable.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IPickable turntablePickable)
                {
                    var dist = Vector3.Distance(targetPos, behaviour.transform.position);
                    if (dist <= maxSearchDistance)
                    {
                        candidates.Add((turntablePickable, dist));
                    }
                }
            }
        }

        // Search only first two parent levels (avoid accidentally selecting global turntable controls)
        var parent = targetNode.transform.parent;
        var level = 0;
        while (parent != null && level < 2)
        {
            foreach (var behaviour in parent.GetComponents<MonoBehaviour>())
            {
                if (behaviour is IPickable parentPickable)
                {
                    var dist = Vector3.Distance(targetPos, behaviour.transform.position);
                    if (dist <= maxSearchDistance)
                    {
                        candidates.Add((parentPickable, dist));
                    }
                }
            }

            parent = parent.parent;
            level++;
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.OrderBy(c => c.dist).First().pickable;
    }

    private void OnDestroy()
    {
        _requestObserver?.Dispose();
        _requestObserver = null;
        _pendingRequests.Clear();
    }

    private bool IsTurntableFouled(out string reason)
    {
        reason = string.Empty;

        if (_nodes == null || _nodes.Count == 0)
        {
            return false;
        }

        var cars = TrainController.Shared?.Cars;
        if (cars == null || !cars.Any())
        {
            return false;
        }

        var center = _controller.turntable.transform.position;
        float radius = 0f;
        foreach (var node in _nodes)
        {
            radius += Vector3.Distance(center, node.transform.position);
        }

        radius /= _nodes.Count;
        if (radius <= 1f)
        {
            return false;
        }

        const float onTableMargin = 0.75f;
        const float connectorMargin = 0.75f;

        foreach (var car in cars)
        {
            if (car == null)
            {
                continue;
            }

            var colliders = car.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0)
            {
                continue;
            }

            bool hasBounds = false;
            var bounds = new Bounds();
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
            {
                continue;
            }

            var closest = bounds.ClosestPoint(center);
            float minDist = Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(center.x, center.z));

            float centerDist = Vector2.Distance(new Vector2(bounds.center.x, bounds.center.z), new Vector2(center.x, center.z));
            float boundsRadius = Mathf.Sqrt(bounds.extents.x * bounds.extents.x + bounds.extents.z * bounds.extents.z);
            float maxDist = centerDist + boundsRadius;

            bool overlapsTurntableDeck = minDist <= (radius - onTableMargin);
            bool overlapsConnectorTrack = maxDist >= (radius + connectorMargin);

            if (overlapsTurntableDeck && overlapsConnectorTrack)
            {
                reason = $"car '{car.DisplayName}' is fouling the table/connector boundary";
                return true;
            }
        }

        return false;
    }

    public Vector3 GetTrackNodePosition(int index)
    {
        if (_nodes == null || index < 0 || index >= _nodes.Count)
        {
            return Vector3.zero;
        }

        return _nodes[index].transform.position;
    }

    public int GetCurrentIndex()
    {
        if (_controller == null || _controller.turntable == null)
        {
            return -1;
        }

        return _controller.turntable.StopIndex ?? -1;
    }

    public string GetTrackIndexLabel(int index)
    {
        var currentIndex = GetCurrentIndex();
        var label = $"Track {index}";

        if (index == currentIndex)
        {
            label += " (Current)";
        }

        return label;
    }

    public int GetNextTrackIndex(bool clockwise = true)
    {
        if (_activeIndexes == null || _activeIndexes.Count == 0)
        {
            return -1;
        }

        var sortedIndexes = _activeIndexes.OrderBy(i => i).ToList();
        var currentIndex = GetCurrentIndex();
        var currentPos = sortedIndexes.IndexOf(currentIndex);

        if (currentPos == -1)
        {
            return sortedIndexes[0];
        }

        if (clockwise)
        {
            return sortedIndexes[(currentPos + 1) % sortedIndexes.Count];
        }

        return sortedIndexes[(currentPos - 1 + sortedIndexes.Count) % sortedIndexes.Count];
    }

    public bool RotateClockwise()
    {
        var nextIndex = GetNextTrackIndex(clockwise: true);
        if (nextIndex != -1)
        {
            Loader.LogDebug($"TurntableHelper: Rotating clockwise to index {nextIndex}");
            return MoveToIndex(nextIndex);
        }

        return false;
    }

    public bool RotateCounterClockwise()
    {
        var nextIndex = GetNextTrackIndex(clockwise: false);
        if (nextIndex != -1)
        {
            Loader.LogDebug($"TurntableHelper: Rotating counter-clockwise to index {nextIndex}");
            return MoveToIndex(nextIndex);
        }

        return false;
    }

    public bool Rotate180()
    {
        if (_nodes == null || _controller == null || _controller.turntable == null)
        {
            Loader.Log("TurntableHelper: Cannot rotate 180 - no nodes or controller");
            return false;
        }

        var currentIndex = GetCurrentIndex();
        if (currentIndex == -1)
        {
            Loader.Log("TurntableHelper: Cannot rotate 180 - no current index");
            return false;
        }

        var oppositeIndex = (currentIndex + _nodes.Count / 2) % _nodes.Count;
        Loader.LogDebug($"TurntableHelper: Rotating 180 degrees from index {currentIndex} to {oppositeIndex}");
        return MoveToIndex(oppositeIndex);
    }
}
