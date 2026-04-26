using KeyValue.Runtime;
using System.Collections.Generic;

namespace MapEnhancer;

internal sealed class SwitchResetAuditState
{
    private const string KeyRequestId = "request_id";
    private const string KeyRequesterName = "requester_name";
    private const string KeyAction = "action";
    private const string KeySwitchCount = "switch_count";
    private const string KeyTimestampUtc = "timestamp_utc";
    private const string KeyHostSequence = "host_sequence";
    private const string KeyHostTotal = "host_total";

    public SwitchResetAuditState(string requestId, string requesterName, string action, int switchCount, string timestampUtc)
        : this(requestId, requesterName, action, switchCount, timestampUtc, 0, 0)
    {
    }

    public SwitchResetAuditState(string requestId, string requesterName, string action, int switchCount, string timestampUtc, int hostSequence, int hostTotal)
    {
        RequestId = requestId;
        RequesterName = requesterName;
        Action = action;
        SwitchCount = switchCount;
        TimestampUtc = timestampUtc;
        HostSequence = hostSequence;
        HostTotal = hostTotal;
    }

    public string RequestId { get; }

    public string RequesterName { get; }

    public string Action { get; }

    public int SwitchCount { get; }

    public string TimestampUtc { get; }

    public int HostSequence { get; }

    public int HostTotal { get; }

    public Value ToPropertyValue()
    {
        return Value.Dictionary(new Dictionary<string, Value>
        {
            [KeyRequestId] = Value.String(RequestId),
            [KeyRequesterName] = Value.String(RequesterName),
            [KeyAction] = Value.String(Action),
            [KeySwitchCount] = Value.Int(SwitchCount),
            [KeyTimestampUtc] = Value.String(TimestampUtc),
            [KeyHostSequence] = Value.Int(HostSequence),
            [KeyHostTotal] = Value.Int(HostTotal),
        });
    }

    public static SwitchResetAuditState? FromPropertyValue(Value value)
    {
        if (value.IsNull || value.Type != ValueType.Dictionary)
        {
            return null;
        }

        var dictionary = value.DictionaryValue;
        if (!dictionary.TryGetValue(KeyRequestId, out var requestIdValue) ||
            !dictionary.TryGetValue(KeyRequesterName, out var requesterNameValue) ||
            !dictionary.TryGetValue(KeyAction, out var actionValue) ||
            !dictionary.TryGetValue(KeySwitchCount, out var switchCountValue) ||
            !dictionary.TryGetValue(KeyTimestampUtc, out var timestampUtcValue))
        {
            return null;
        }

        var hostSequence = dictionary.TryGetValue(KeyHostSequence, out var hostSequenceValue)
            ? hostSequenceValue.IntValue
            : 0;
        var hostTotal = dictionary.TryGetValue(KeyHostTotal, out var hostTotalValue)
            ? hostTotalValue.IntValue
            : 0;

        return new SwitchResetAuditState(
            requestIdValue.StringValue,
            requesterNameValue.StringValue,
            actionValue.StringValue,
            switchCountValue.IntValue,
            timestampUtcValue.StringValue,
            hostSequence,
            hostTotal);
    }
}
