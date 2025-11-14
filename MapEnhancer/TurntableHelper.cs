using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Track;
using UnityEngine;
using MapEnhancer.UMM;

namespace MapEnhancer;

public class TurntableHelper : MonoBehaviour
{
    private TurntableController _controller;
    private List<TrackNode> _nodes;
    private HashSet<int> _activeIndexes;

    public TurntableController Controller => _controller;
    public List<int> ActiveTrackIndexes => _activeIndexes?.ToList() ?? new List<int>();

    public void Awake()
    {
        _controller = GetComponent<TurntableController>();
        if (_controller == null || _controller.turntable == null)
        {
            Loader.Log("TurntableHelper: No controller or turntable found");
            return;
        }

        // Get the private nodes list from turntable
        _nodes = Traverse.Create(_controller.turntable).Field<List<TrackNode>>("nodes").Value;
        if (_nodes == null)
        {
            Loader.Log("TurntableHelper: No nodes found");
            return;
        }

        // Find active track nodes (ones with connections)
        _activeIndexes = new HashSet<int>();
        for (var i = 0; i < _nodes.Count; i++)
        {
            var j = (i + _nodes.Count / 2) % _nodes.Count;
            if (Graph.Shared.SegmentsConnectedTo(_nodes[i]).Count == 0 &&
                Graph.Shared.SegmentsConnectedTo(_nodes[j]).Count == 0)
            {
                continue;
            }

            _activeIndexes.Add(i);
            _activeIndexes.Add(j);
        }

        Loader.LogDebug($"TurntableHelper: Found {_activeIndexes.Count} active track connections");
    }

    private float? _targetAngle;
    private int _targetIndex;
    public System.Action OnRotationComplete;

    public void MoveToIndex(int trackNodeIndex)
    {
        if (_controller == null || _controller.turntable == null)
        {
            Loader.Log("TurntableHelper: Cannot move - no controller");
            return;
        }

        _targetIndex = trackNodeIndex;
        _targetAngle = _controller.turntable.AngleForIndex(trackNodeIndex);
        Loader.LogDebug($"TurntableHelper: Moving to index {trackNodeIndex}, angle {_targetAngle}");
    }

    public void FixedUpdate()
    {
        if (!_targetAngle.HasValue || _controller == null || _controller.turntable == null)
        {
            return;
        }

        _controller.SetAngle(_targetAngle.Value);
        _controller.turntable.SetStopIndex(_targetIndex);
        _targetAngle = null;
        
        // Trigger callback after rotation is complete
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
