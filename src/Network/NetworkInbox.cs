using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RTS.Network;

/// <summary>FIFO command barriers, with replaceable position-only updates between barriers.</summary>
internal sealed class NetworkInbox<T>(Func<T, NetworkMessage?> getMessage, Func<T, object?> getSource)
{
    private readonly object _sync = new();
    private readonly LinkedList<T> _items = new();
    private readonly Dictionary<(object? Source, Guid Unit, long Navigation), LinkedListNode<T>> _positions = new();
    public int Count { get { lock (_sync) return _items.Count; } }

    public void Enqueue(T item)
    {
        lock (_sync)
        {
            var key = PositionKey(item);
            if (key is { } position)
            {
                if (_positions.Remove(position, out LinkedListNode<T>? previous)) _items.Remove(previous);
                _positions[position] = _items.AddLast(item);
            }
            else
            {
                // Never move a position update across a command, snapshot, navigation
                // change, or disconnect. Those are semantic ordering boundaries.
                _positions.Clear();
                _items.AddLast(item);
            }
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (_sync)
        {
            if (_items.First is not LinkedListNode<T> first) { item = default!; return false; }
            item = first.Value;
            _items.RemoveFirst();
            var key = PositionKey(item);
            if (key is { } position && _positions.TryGetValue(position, out var node) && ReferenceEquals(node, first))
                _positions.Remove(position);
            return true;
        }
    }

    private (object? Source, Guid Unit, long Navigation)? PositionKey(T item)
    {
        NetworkMessage? message = getMessage(item);
        if (message?.Type != NetworkMessageType.UnitStateCommand ||
            message.UnitState is not { TypeId: "mobile-unit-state" } state) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(state.Payload);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("navigation", out var navigation) || navigation.ValueKind != JsonValueKind.Null ||
                !root.TryGetProperty("navigationRevision", out var revision) || !revision.TryGetInt64(out long value)) return null;
            return (getSource(item), state.UnitId, value);
        }
        catch (JsonException) { return null; }
    }
}
