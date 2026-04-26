using KeyValue.Runtime;
using System.Collections.Generic;

namespace MapEnhancer;

internal sealed class TurntableRequestState
{
    private const string KeyTurntableId = "turntable_id";
    private const string KeyTrackNodeIndex = "track_node_index";
    private const string KeySequence = "sequence";

    public TurntableRequestState(string turntableId, int trackNodeIndex, int sequence)
    {
        TurntableId = turntableId;
        TrackNodeIndex = trackNodeIndex;
        Sequence = sequence;
    }

    public string TurntableId { get; }

    public int TrackNodeIndex { get; }

    public int Sequence { get; }

    public Value ToPropertyValue()
    {
        return Value.Dictionary(new Dictionary<string, Value>
        {
            [KeyTurntableId] = Value.String(TurntableId),
            [KeyTrackNodeIndex] = Value.Int(TrackNodeIndex),
            [KeySequence] = Value.Int(Sequence),
        });
    }

    public static TurntableRequestState? FromPropertyValue(Value value)
    {
        if (value.IsNull || value.Type != ValueType.Dictionary)
        {
            return null;
        }

        var dictionary = value.DictionaryValue;
        if (!dictionary.TryGetValue(KeyTurntableId, out var turntableIdValue) ||
            !dictionary.TryGetValue(KeyTrackNodeIndex, out var trackNodeIndexValue) ||
            !dictionary.TryGetValue(KeySequence, out var sequenceValue))
        {
            return null;
        }

        return new TurntableRequestState(
            turntableIdValue.StringValue,
            trackNodeIndexValue.IntValue,
            sequenceValue.IntValue);
    }
}
