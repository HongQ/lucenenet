using Lucene.Net.Support;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections;

namespace Lucene.Net.Util
{
    /*
     * Licensed to the Apache Software Foundation (ASF) under one or more
     * contributor license agreements.  See the NOTICE file distributed with
     * this work for additional information regarding copyright ownership.
     * The ASF licenses this file to You under the Apache License, Version 2.0
     * (the "License"); you may not use this file except in compliance with
     * the License.  You may obtain a copy of the License at
     *
     *     http://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS,
     * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
     * See the License for the specific language governing permissions and
     * limitations under the License.
     */

    /// <summary>
    /// Implements a combination of <seealso cref="java.util.WeakHashMap"/> and
    /// <seealso cref="java.util.IdentityHashMap"/>.
    /// Useful for caches that need to key off of a {@code ==} comparison
    /// instead of a {@code .equals}.
    ///
    /// <p>this class is not a general-purpose <seealso cref="java.util.Map"/>
    /// implementation! It intentionally violates
    /// Map's general contract, which mandates the use of the equals method
    /// when comparing objects. this class is designed for use only in the
    /// rare cases wherein reference-equality semantics are required.
    ///
    /// <p>this implementation was forked from <a href="http://cxf.apache.org/">Apache CXF</a>
    /// but modified to <b>not</b> implement the <seealso cref="java.util.Map"/> interface and
    /// without any set views on it, as those are error-prone and inefficient,
    /// if not implemented carefully. The map only contains <seealso cref="Iterator"/> implementations
    /// on the values and not-GCed keys. Lucene's implementation also supports {@code null}
    /// keys, but those are never weak!
    ///
    /// <p><a name="reapInfo" />The map supports two modes of operation:
    /// <ul>
    ///  <li>{@code reapOnRead = true}: this behaves identical to a <seealso cref="java.util.WeakHashMap"/>
    ///  where it also cleans up the reference queue on every read operation (<seealso cref="#get(Object)"/>,
    ///  <seealso cref="#containsKey(Object)"/>, <seealso cref="#size()"/>, <seealso cref="#valueIterator()"/>), freeing map entries
    ///  of already GCed keys.</li>
    ///  <li>{@code reapOnRead = false}: this mode does not call <seealso cref="#reap()"/> on every read
    ///  operation. In this case, the reference queue is only cleaned up on write operations
    ///  (like <seealso cref="#put(Object, Object)"/>). this is ideal for maps with few entries where
    ///  the keys are unlikely be garbage collected, but there are lots of <seealso cref="#get(Object)"/>
    ///  operations. The code can still call <seealso cref="#reap()"/> to manually clean up the queue without
    ///  doing a write operation.</li>
    /// </ul>
    ///
    /// @lucene.internal
    /// </summary>
    public sealed class WeakIdentityMap<K, V>
        where K : class
    {
        // LUCENENET TODO Make this class internal as it isn't required anywhere; need to have it exposed to tests though

        //private readonly ReferenceQueue<object> queue = new ReferenceQueue<object>();
        private readonly IDictionary<IdentityWeakReference, V> BackingStore;

        private readonly bool ReapOnRead;

        /// <summary>
        /// Creates a new {@code WeakIdentityMap} based on a non-synchronized <seealso cref="HashMap"/>.
        /// The map <a href="#reapInfo">cleans up the reference queue on every read operation</a>.
        /// </summary>
        public static WeakIdentityMap<K, V> NewHashMap()
        {
            return NewHashMap(false);
        }

        /// <summary>
        /// Creates a new {@code WeakIdentityMap} based on a non-synchronized <seealso cref="HashMap"/>. </summary>
        /// <param name="reapOnRead"> controls if the map <a href="#reapInfo">cleans up the reference queue on every read operation</a>. </param>
        public static WeakIdentityMap<K, V> NewHashMap(bool reapOnRead)
        {
            return new WeakIdentityMap<K, V>(new HashMap<IdentityWeakReference, V>(), reapOnRead);
        }

        /// <summary>
        /// Creates a new {@code WeakIdentityMap} based on a <seealso cref="ConcurrentHashMap"/>.
        /// The map <a href="#reapInfo">cleans up the reference queue on every read operation</a>.
        /// </summary>
        public static WeakIdentityMap<K, V> NewConcurrentHashMap()
        {
            return NewConcurrentHashMap(true);
        }

        /// <summary>
        /// Creates a new {@code WeakIdentityMap} based on a <seealso cref="ConcurrentHashMap"/>. </summary>
        /// <param name="reapOnRead"> controls if the map <a href="#reapInfo">cleans up the reference queue on every read operation</a>. </param>
        public static WeakIdentityMap<K, V> NewConcurrentHashMap(bool reapOnRead)
        {
            return new WeakIdentityMap<K, V>(new ConcurrentDictionary<IdentityWeakReference, V>(), reapOnRead);
        }

        /// <summary>
        /// Private only constructor, to create use the static factory methods. </summary>
        private WeakIdentityMap(IDictionary<IdentityWeakReference, V> backingStore, bool reapOnRead)
        {
            this.BackingStore = backingStore;
            this.ReapOnRead = reapOnRead;
        }

        /// <summary>
        /// Removes all of the mappings from this map. </summary>
        public void Clear()
        {
            BackingStore.Clear();
            Reap();
        }

        /// <summary>
        /// Returns {@code true} if this map contains a mapping for the specified key. </summary>
        public bool ContainsKey(object key)
        {
            if (ReapOnRead)
            {
                Reap();
            }
            return BackingStore.ContainsKey(new IdentityWeakReference(key));
        }

        /// <summary>
        /// Returns the value to which the specified key is mapped. </summary>
        public V Get(object key)
        {
            if (ReapOnRead)
            {
                Reap();
            }

            V val;
            if (BackingStore.TryGetValue(new IdentityWeakReference(key), out val))
            {
                return val;
            }
            else
            {
                return default(V);
            }
        }

        /// <summary>
        /// Associates the specified value with the specified key in this map.
        /// If the map previously contained a mapping for this key, the old value
        /// is replaced.
        /// </summary>
        public V Put(K key, V value)
        {
            Reap();
            return BackingStore[new IdentityWeakReference(key)] = value;
        }

        public IEnumerable<K> Keys
        {
            get
            {
                return new KeyWrapper(this);
            }
        }

        /// <summary>
        /// LUCENENET specific class to allow the 
        /// GetEnumerator() method to be overridden
        /// for the keys so we can return an enumerator
        /// that is smart enough to clean up the dead keys
        /// and also so that MoveNext() returns false in the
        /// event there are no more values left (instead of returning
        /// a null value in an extra enumeration).
        /// </summary>
        private class KeyWrapper : IEnumerable<K>
        {
            private readonly WeakIdentityMap<K, V> outerInstance;
            public KeyWrapper(WeakIdentityMap<K, V> outerInstance)
            {
                this.outerInstance = outerInstance;
            }
            public IEnumerator<K> GetEnumerator()
            {
                outerInstance.Reap();
                return new IteratorAnonymousInnerClassHelper(outerInstance);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public IEnumerable<V> Values
        {
            get
            {
                if (ReapOnRead) Reap();
                return BackingStore.Values;
            }
        }

        /// <summary>
        /// Returns {@code true} if this map contains no key-value mappings. </summary>
        public bool Empty
        {
            get
            {
                return Size() == 0;
            }
        }

        /// <summary>
        /// Removes the mapping for a key from this weak hash map if it is present.
        /// Returns the value to which this map previously associated the key,
        /// or {@code null} if the map contained no mapping for the key.
        /// A return value of {@code null} does not necessarily indicate that
        /// the map contained.
        /// </summary>
        public bool Remove(object key)
        {
            Reap();
            return BackingStore.Remove(new IdentityWeakReference(key));
        }

        /// <summary>
        /// Returns the number of key-value mappings in this map. this result is a snapshot,
        /// and may not reflect unprocessed entries that will be removed before next
        /// attempted access because they are no longer referenced.
        /// </summary>
        public int Size()
        {
            if (BackingStore.Count == 0)
            {
                return 0;
            }
            if (ReapOnRead)
            {
                Reap();
            }
            return BackingStore.Count;
        }

        private class IteratorAnonymousInnerClassHelper : IEnumerator<K>
        {
            private readonly WeakIdentityMap<K,V> outerInstance;

            public IteratorAnonymousInnerClassHelper(WeakIdentityMap<K,V> outerInstance)
            {
                this.outerInstance = outerInstance;
            }

            // holds strong reference to next element in backing iterator:
            private object next = null;
            private int position = -1; // start before the beginning of the set

            public K Current
            {
                get
                {
                    return (K)next;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }

            public void Dispose()
            {
                // Nothing to do
            }

            
            public bool MoveNext()
            {
                while (true)
                {
                    IdentityWeakReference key;

                    // If the next position doesn't exist, exit
                    if (++position >= outerInstance.BackingStore.Count)
                    {
                        position--;
                        return false;
                    }
                    try
                    {
                        key = outerInstance.BackingStore.Keys.ElementAt(position);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // some other thread beat us to the last element (or removed a prior element) - fail gracefully.
                        position--;
                        return false;
                    }
                    if (!key.IsAlive)
                    {
                        outerInstance.BackingStore.Remove(key);
                        position--;
                        continue;
                    }
                    // unfold "null" special value:
                    if (key.Target == NULL)
                    {
                        next = null;
                    }
                    else
                    {
                        next = key.Target;
                    }
                    return true;
                }
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Returns an iterator over all values of this map.
        /// this iterator may return values whose key is already
        /// garbage collected while iterator is consumed,
        /// especially if {@code reapOnRead} is {@code false}.
        /// </summary>
        public IEnumerator<V> ValueIterator()
        {
            if (ReapOnRead)
            {
                Reap();
            }
            return BackingStore.Values.GetEnumerator();
        }

        /// <summary>
        /// this method manually cleans up the reference queue to remove all garbage
        /// collected key/value pairs from the map. Calling this method is not needed
        /// if {@code reapOnRead = true}. Otherwise it might be a good idea
        /// to call this method when there is spare time (e.g. from a background thread). </summary>
        /// <seealso cref= <a href="#reapInfo">Information about the <code>reapOnRead</code> setting</a> </seealso>
        public void Reap()
        {
            List<IdentityWeakReference> keysToRemove = new List<IdentityWeakReference>();
            foreach (IdentityWeakReference zombie in BackingStore.Keys)
            {
                if (!zombie.IsAlive)
                {
                    keysToRemove.Add(zombie);
                }
            }

            foreach (var key in keysToRemove)
            {
                BackingStore.Remove(key);
            }
        }

        // we keep a hard reference to our NULL key, so map supports null keys that never get GCed:
        internal static readonly object NULL = new object();

        private sealed class IdentityWeakReference : WeakReference
        {
            internal readonly int Hash;

            internal IdentityWeakReference(object obj/*, ReferenceQueue<object> queue*/)
                : base(obj == null ? NULL : obj/*, queue*/)
            {
                Hash = RuntimeHelpers.GetHashCode(obj);
            }

            public override int GetHashCode()
            {
                return Hash;
            }

            public override bool Equals(object o)
            {
                if (this == o)
                {
                    return true;
                }
                if (o is IdentityWeakReference)
                {
                    IdentityWeakReference @ref = (IdentityWeakReference)o;
                    if (this.Target == @ref.Target)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}