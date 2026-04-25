using System.Collections.Generic;
using System.Linq;
using Game.State;
using HarmonyLib;
using Helpers;
using Track;
using UnityEngine;
using MapEnhancer.UMM;

namespace MapEnhancer;

public class TurntableHelper : MonoBehaviour
{
    private TurntableController _controller;
    private List<TrackNode> _nodes;
    private HashSet<int> _activeIndexes;
    private bool _isInitialized = false;

    public TurntableController Controller => _controller;
    public List<int> ActiveTrackIndexes => _activeIndexes?.ToList() ?? new List<int>();
    public bool IsInitialized => _isInitialized;

    public void Awake()
    {
        _controller = GetComponent<TurntableController>();
        if (_controller == null || _controller.turntable == null)
        {
            Loader.Log($"TurntableHelper initialization FAILED: No controller or turntable found on {gameObject.name}");
            return;
        }

        // Get the private nodes list from turntable
        _nodes = Traverse.Create(_controller.turntable).Field<List<TrackNode>>("nodes").Value;
        if (_nodes == null || _nodes.Count == 0)
        {
            Loader.Log($"TurntableHelper initialization FAILED: No nodes found for turntable at {_controller.turntable.transform.position}");
            return;
        }

        // Find active track nodes (ones with connections)
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
    }

    private float? _targetAngle;
    private int _targetIndex;
    public System.Action OnRotationComplete;

    // Anti-spam safeguards
    private bool _isRotating = false;
    private float _lastRotationTime = 0f;
    private const float ROTATION_COOLDOWN = 0.5f; // Minimum 0.5 seconds between rotations

    public void MoveToIndex(int trackNodeIndex)
    {
        // Anti-spam check: prevent rapid-fire rotations
        if (_isRotating)
        {
            Loader.LogDebug("TurntableHelper: Rotation already in progress, ignoring command");
            return;
        }

        float timeSinceLastRotation = Time.time - _lastRotationTime;
        if (timeSinceLastRotation < ROTATION_COOLDOWN)
        {
            Loader.LogDebug($"TurntableHelper: Cooldown active ({ROTATION_COOLDOWN - timeSinceLastRotation:F2}s remaining)");
            return;
        }

        if (_controller == null || _controller.turntable == null)
        {
            Loader.Log("Turntable rotation FAILED: No controller available");
            return;
        }

        if (_nodes == null || trackNodeIndex < 0 || trackNodeIndex >= _nodes.Count)
        {
            Loader.Log($"Turntable rotation FAILED: Invalid track node index {trackNodeIndex}");
            return;
        }

        // Mark as rotating and update timestamp
        _isRotating = true;
        _lastRotationTime = Time.time;

        // MULTIPLAYER FIX: Use the game's networked IPickable system
        // This is how the in-game turntable UI works - it properly syncs to all clients!
        var targetNode = _nodes[trackNodeIndex];
        var pickable = targetNode.GetComponent<IPickable>();
        
        if (pickable != null)
        {
            // Simulate clicking the physical turntable track node
            // The game's IPickable system handles network synchronization automatically
            Loader.Log($"Rotating turntable to track {trackNodeIndex} (network-synced via IPickable)");
            pickable.Activate(new PickableActivateEvent());
        }
        else
        {
            // Fallback for single-player if IPickable not found
            Loader.Log($"WARNING: Rotating turntable to track {trackNodeIndex} using direct call (NOT network-synced!)");
            _targetIndex = trackNodeIndex;
            _targetAngle = _controller.turntable.AngleForIndex(trackNodeIndex);
        }
    }

    public void FixedUpdate()
    {
        // Check if rotation is complete by comparing current index to target
        if (_isRotating && _controller != null && _controller.turntable != null)
        {
            var currentIndex = _controller.turntable.StopIndex;
            if (currentIndex.HasValue && currentIndex.Value == _targetIndex)
            {
                // Rotation reached target, allow new commands
                _isRotating = false;
                Loader.Log($"Turntable rotation completed at track index {currentIndex.Value}");
                OnRotationComplete?.Invoke();
            }
        }

        // Only needed for fallback direct calls (when IPickable not available)
        // The IPickable system handles rotation automatically and network-synced
        if (!_targetAngle.HasValue || _controller == null || _controller.turntable == null)
        {
            return;
        }

        // Fallback path for single-player when IPickable not found
        Loader.LogDebug($"TurntableHelper: FixedUpdate fallback - rotating to index {_targetIndex}");
        _controller.SetAngle(_targetAngle.Value);
        _controller.turntable.SetStopIndex(_targetIndex);
        _targetAngle = null;

        // For fallback, mark rotation as complete immediately
        _isRotating = false;
        OnRotationComplete?.Invoke();
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
            // Current index not in active list, return first active
            return sortedIndexes[0];
        }

        if (clockwise)
        {
            return sortedIndexes[(currentPos + 1) % sortedIndexes.Count];
        }
        else
        {
            return sortedIndexes[(currentPos - 1 + sortedIndexes.Count) % sortedIndexes.Count];
        }
    }

    public void RotateClockwise()
    {
        var nextIndex = GetNextTrackIndex(clockwise: true);
        if (nextIndex != -1)
        {
            Loader.LogDebug($"TurntableHelper: Rotating clockwise to index {nextIndex}");
            MoveToIndex(nextIndex);
        }
    }

    public void RotateCounterClockwise()
    {
        var nextIndex = GetNextTrackIndex(clockwise: false);
        if (nextIndex != -1)
        {
            Loader.LogDebug($"TurntableHelper: Rotating counter-clockwise to index {nextIndex}");
            MoveToIndex(nextIndex);
        }
    }

    public void Rotate180()
    {
        if (_nodes == null || _controller == null || _controller.turntable == null)
        {
            Loader.Log("TurntableHelper: Cannot rotate 180 - no nodes or controller");
            return;
        }

        var currentIndex = GetCurrentIndex();
        if (currentIndex == -1)
        {
            Loader.Log("TurntableHelper: Cannot rotate 180 - no current index");
            return;
        }

        // Calculate the opposite index (180 degrees)
        var oppositeIndex = (currentIndex + _nodes.Count / 2) % _nodes.Count;
        
        Loader.LogDebug($"TurntableHelper: Rotating 180 degrees from index {currentIndex} to {oppositeIndex}");
        MoveToIndex(oppositeIndex);
    }
}
