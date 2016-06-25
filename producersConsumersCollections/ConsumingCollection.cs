using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace DirScanDomain.CustomCollections
{
    //Collection that provides access to shared resources from multiple threads.
    public class ConsumingCollection<T> : IDisposable
    {
        private readonly List<T> _list;

        private bool completed = false;

        private readonly object _locker = new object();

        //Lock object for event
        private readonly object _eventLocker = new object();
        //Event that notifying semaphores about new free resources
        private event Func<int> OnCollectionUpdated;

        public ConsumingCollection()
        {
            this._list = new List<T>();
        }

        //Add new item to the collection
        //and notify waiting threads about new item
        public void Add(T item)
        {
            lock (_locker)
            {
                this._list.Add(item);
                lock (_eventLocker)
                {
                    OnCollectionUpdated?.Invoke();
                }
            }
        }

        //Mark collection as completed so threads that wait for new items 
        //will read collection to the end and leave enumeration
        public void CompleteAdding()
        {
            this.completed = true;
            lock (_eventLocker)
            {
                OnCollectionUpdated?.Invoke();
            }
        }

        //Return collection if it is completed or empty if not.
        public IEnumerable<T> GetCompletedCollection()
        {
            return completed ? this._list : new List<T>();
        }

        //Get threadsafe enumerable that wait for new items
        public IEnumerable<T> GetConsumingEnumerable()
        {
            //Provide limited count of resourses and wait for new resourses
            var localSemaphore = new SemaphoreSlim(_list.Count);
            lock (_eventLocker)
            {
                //subscribe on new resourse adding
                this.OnCollectionUpdated += localSemaphore.Release;
            }

            var enumerator = GetEnumerator();
            while (true)
            {
                if (!completed)
                {
                    localSemaphore.Wait();
                }

                if (enumerator.MoveNext())
                {
                    yield return enumerator.Current;
                }
                else
                {
                    lock (_eventLocker)
                    {
                        //unsubscribe 
                        this.OnCollectionUpdated -= localSemaphore.Release;
                        //Dispose semaphore and leave loop
                        localSemaphore.Dispose();
                    }
                    yield break;
                }
            }
        }

        private IEnumerator<T> GetEnumerator()
        {
            return new SafeEnumerator(this);
        }

        private class SafeEnumerator : IEnumerator<T>
        {
            private ConsumingCollection<T> _collection;
            private int index;

            private T current;

            public SafeEnumerator(ConsumingCollection<T> collection)
            {
                this._collection = collection;            
            }

            #region Implementation of IDisposable

            public void Dispose()
            {
            }

            #endregion

            #region Implementation of IEnumerator
            public bool MoveNext()
            {
                List<T> localList = _collection._list;

                if (((uint) index < (uint) localList.Count))
                {
                    current = localList[index];
                    index++;
                    return true;
                }

                index = _collection._list.Count + 1;
                current = default(T);
                return false;
            }

            public void Reset()
            {
                index = 0;
                current = default(T);
            }

            public T Current
            {
                get
                {
                    return current;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return current;
                }
            }

            #endregion
        }

        public void Dispose()
        {
        }
    }
}
