using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Sirenix.Serialization;
using UnityEngine;

public static class BidirectionalDictionaryPool<TKey, TValue>
{
    static Queue<BidirectionalDictionary<TKey, TValue>> pool;

    static BidirectionalDictionaryPool()
    {
        pool = new Queue<BidirectionalDictionary<TKey, TValue>>();
        Expand(16);
    }

    public static BidirectionalDictionary<TKey, TValue> Get()
    {
        if (pool.Count == 0) Expand(16);
        return pool.Dequeue();
    }

    public static void Free(BidirectionalDictionary<TKey, TValue> dict)
    {
        dict.Clear();
        pool.Enqueue(dict);
    }

    static void Expand(int by)
    {
        for (int i = 0; i < by; i++)
        {
            pool.Enqueue(new BidirectionalDictionary<TKey, TValue>());
        }
    }
}

public class BidirectionalDictionary<T, K> : ICollection<KeyValuePair<T, K>>, IEnumerable<KeyValuePair<T, K>>, IEnumerable, IDictionary<T, K>, IReadOnlyCollection<KeyValuePair<T, K>>, IReadOnlyDictionary<T, K>, ICollection, IDictionary, IDeserializationCallback, ISerializable
{
    private Dictionary<T, K> forwardDict;
    private Dictionary<K, T> backwardsDict;

    public BidirectionalDictionary()
    {
        forwardDict = new Dictionary<T, K>();
        backwardsDict = new Dictionary<K, T>();
    }
    private BidirectionalDictionary(Dictionary<T, K> forward, Dictionary<K, T> backward)
    {
        forwardDict = forward;
        backwardsDict = backward;
    }
    protected BidirectionalDictionary(SerializationInfo info, StreamingContext context)
    {
        forwardDict = (Dictionary<T, K>)info.GetValue("forward", typeof(Dictionary<T, K>));
        backwardsDict = (Dictionary<K, T>)info.GetValue("backwards", typeof(Dictionary<K, T>));
    }

    public BidirectionalDictionary<T, K> Clone()
    {
        Dictionary<T, K> newF = new Dictionary<T, K>(forwardDict);
        Dictionary<K, T> newB = new Dictionary<K, T>(backwardsDict);
        var bidict = BidirectionalDictionaryPool<T, K>.Get();
        bidict.forwardDict = newF;
        bidict.backwardsDict = newB;
        return bidict;
    }

    public void Deserialize(SerializationInfo info, StreamingContext context)
    {
        forwardDict = (Dictionary<T, K>)info.GetValue("forward", typeof(Dictionary<T, K>));
        backwardsDict = (Dictionary<K, T>)info.GetValue("backwards", typeof(Dictionary<K, T>));
    }

    public K this[T key]
    {
        get => forwardDict[key];
        set
        {
            if (forwardDict.TryGetValue(key, out K oldValue))
                backwardsDict.Remove(oldValue);
            forwardDict[key] = value;
            backwardsDict[value] = key;
        }
    }
    public T this[K key]
    {
        get => backwardsDict[key];
        set
        {
            if (backwardsDict.TryGetValue(key, out T oldValue))
                forwardDict.Remove(oldValue);
            forwardDict[value] = key;
            backwardsDict[key] = value;
        }
    }

    public object this[object key]
    {
        get
        {
            if (key is T)
                return forwardDict[(T)key];
            else if (key is K)
                return backwardsDict[(K)key];
            else
                return null;
        }
        set { }
    }

    public int Count => forwardDict.Count;
    public bool IsFixedSize => false;

    public ICollection<T> Keys => forwardDict.Keys;
    public ICollection<K> Values => forwardDict.Values;

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => null;

    IEnumerable<T> IReadOnlyDictionary<T, K>.Keys => forwardDict.Keys;
    ICollection IDictionary.Keys => forwardDict.Keys;
    IEnumerable<K> IReadOnlyDictionary<T, K>.Values => forwardDict.Values;
    ICollection IDictionary.Values => forwardDict.Values;

    public void Add(T key, K value)
    {
        if (!ContainsKey(key) && !ContainsKey(value))
        {
            forwardDict.Add(key, value);
            backwardsDict.Add(value, key);
            return;
        }
        throw new InvalidOperationException("Matching key found.");
    }
    public void Add(K key, T value) => Add(value, key);
    public void Add(KeyValuePair<T, K> item) => Add(item.Key, item.Value);
    public void Add(KeyValuePair<K, T> item) => Add(item.Value, item.Key);
    public void Add(object key, object value)
    {
        if (key is T && value is K)
            Add((T)key, (K)value);
        else if (key is K && value is T)
            Add((K)value, (T)key);
    }

    public bool Remove(KeyValuePair<T, K> item) => forwardDict.Remove(item.Key) && backwardsDict.Remove(item.Value);
    public bool Remove(KeyValuePair<K, T> item) => backwardsDict.Remove(item.Key) && forwardDict.Remove(item.Value);
    public bool Remove(T key, K val) => forwardDict.Remove(key) && backwardsDict.Remove(val);
    public bool Remove(K key, T val) => forwardDict.Remove(val) && backwardsDict.Remove(key);
    public bool Remove(T key)
    {
        if (forwardDict.TryGetValue(key, out K val))
            return Remove(key, val);

        return false;
    }
    public bool Remove(K key)
    {
        if (backwardsDict.TryGetValue(key, out T val))
            return Remove(key, val);

        return false;
    }
    public void Remove(object key)
    {
        if (key is T)
            Remove((T)key);
        else if (key is K)
            Remove((K)key);
    }

    public void Clear()
    {
        forwardDict.Clear();
        backwardsDict.Clear();
    }

    public bool ContainsKey(T key) => forwardDict.ContainsKey(key);
    public bool ContainsKey(K key) => backwardsDict.ContainsKey(key);
    public bool Contains(KeyValuePair<T, K> item) => ContainsKey(item.Key);
    public bool Contains(KeyValuePair<K, T> item) => ContainsKey(item.Key);

    public bool Contains(object key)
    {
        if (key is T)
            return ContainsKey((T)key);
        else if (key is K)
            return ContainsKey((K)key);
        else
            return false;
    }

    public bool TryGetValue(T key, out K value) => forwardDict.TryGetValue(key, out value);
    public bool TryGetValue(K key, out T value) => backwardsDict.TryGetValue(key, out value);

    public void CopyTo(KeyValuePair<T, K>[] array, int arrayIndex) => throw new System.NotImplementedException();
    public void CopyTo(KeyValuePair<K, T>[] array, int arrayIndex) => throw new NotImplementedException();
    public void CopyTo(Array array, int index) => throw new NotImplementedException();
    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("forward", forwardDict, typeof(Dictionary<T, K>));
        info.AddValue("backward", backwardsDict, typeof(Dictionary<K, T>));
    }
    public void OnDeserialization(object sender)
    {
        return;
    }

    public IEnumerator<KeyValuePair<T, K>> GetEnumerator() => forwardDict.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IDictionaryEnumerator IDictionary.GetEnumerator() => forwardDict.GetEnumerator();
}