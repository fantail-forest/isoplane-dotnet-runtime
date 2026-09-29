// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>
    /// A most-recently-used cache bounded by entry count and by the total size its entries
    /// report, for material that would otherwise be persisted to disk. Eviction runs outside
    /// the lock, so an evicted value may be released while another reader holds a value it
    /// looked up earlier; values must therefore be safe to use after eviction (immutable data)
    /// or be re-referenced by the reader before the lock is released.
    /// </summary>
    internal sealed class BoundedMruCache<TValue> where TValue : class
    {
        private readonly int _maxItems;
        private readonly long _maxTotalSize;
        private readonly Func<TValue, long>? _sizeOf;
        private readonly Action<TValue>? _onEvict;

        private readonly Lock _lock = new();
        private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);
        private readonly LinkedList<Entry> _mostRecentFirst = new();
        private long _totalSize;

        internal BoundedMruCache(int maxItems, long maxTotalSize, Func<TValue, long>? sizeOf, Action<TValue>? onEvict)
        {
            Debug.Assert(maxItems > 0);
            Debug.Assert(maxTotalSize > 0);
            Debug.Assert(sizeOf is not null || maxTotalSize == long.MaxValue);

            _maxItems = maxItems;
            _maxTotalSize = maxTotalSize;
            _sizeOf = sizeOf;
            _onEvict = onEvict;
        }

        internal bool TryGetValue(string key, [NotNullWhen(true)] out TValue? value)
        {
            lock (_lock)
            {
                if (_index.TryGetValue(key, out LinkedListNode<Entry>? node))
                {
                    _mostRecentFirst.Remove(node);
                    _mostRecentFirst.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Adds or replaces the value under <paramref name="key"/>; a value larger than the whole
        /// budget is not cached. Anything evicted to stay within bounds is released after the lock.
        /// </summary>
        internal void AddOrUpdate(string key, TValue value)
        {
            long size = _sizeOf?.Invoke(value) ?? 0;

            if (size > _maxTotalSize)
            {
                _onEvict?.Invoke(value);
                return;
            }

            List<TValue>? evicted = null;

            lock (_lock)
            {
                if (_index.TryGetValue(key, out LinkedListNode<Entry>? existing))
                {
                    Unlink(existing, ref evicted);
                }

                LinkedListNode<Entry> node = new(new Entry(key, value, size));
                _mostRecentFirst.AddFirst(node);
                _index[key] = node;
                _totalSize += size;

                while (_index.Count > _maxItems || _totalSize > _maxTotalSize)
                {
                    Debug.Assert(_mostRecentFirst.Last is not null && _mostRecentFirst.Last != node);
                    Unlink(_mostRecentFirst.Last, ref evicted);
                }
            }

            Release(evicted);
        }

        internal void Remove(string key)
        {
            List<TValue>? evicted = null;

            lock (_lock)
            {
                if (_index.TryGetValue(key, out LinkedListNode<Entry>? node))
                {
                    Unlink(node, ref evicted);
                }
            }

            Release(evicted);
        }

        /// <summary>
        /// Runs <paramref name="action"/> over every cached value, most recent first, under the
        /// cache's lock so no value is released while it is being visited.
        /// </summary>
        internal void ForEachValue<TState>(TState state, Action<TState, TValue> action)
        {
            lock (_lock)
            {
                for (LinkedListNode<Entry>? node = _mostRecentFirst.First; node is not null; node = node.Next)
                {
                    action(state, node.Value.Value);
                }
            }
        }

        private void Unlink(LinkedListNode<Entry> node, ref List<TValue>? evicted)
        {
            Debug.Assert(_lock.IsHeldByCurrentThread);

            _mostRecentFirst.Remove(node);
            _index.Remove(node.Value.Key);
            _totalSize -= node.Value.Size;

            if (_onEvict is not null)
            {
                (evicted ??= new List<TValue>()).Add(node.Value.Value);
            }
        }

        private void Release(List<TValue>? evicted)
        {
            if (evicted is null)
            {
                return;
            }

            Debug.Assert(_onEvict is not null);

            foreach (TValue value in evicted)
            {
                _onEvict(value);
            }
        }

        private readonly struct Entry
        {
            internal string Key { get; }
            internal TValue Value { get; }
            internal long Size { get; }

            internal Entry(string key, TValue value, long size)
            {
                Key = key;
                Value = value;
                Size = size;
            }
        }
    }
}
