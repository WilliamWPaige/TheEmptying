using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Events
{
    /// <summary>
    /// The only way modules talk. Events post during a tick and deliver at its
    /// end, in post order. An event posted while DeliverPending is dispatching
    /// gets queued for the NEXT DeliverPending call, never the current pass:
    /// DeliverPending only dispatches the events that were already pending when
    /// it started, then drops just that many off the front of the queue.
    /// </summary>
    public sealed class EventBus
    {
        private readonly EventDefinitionRegistry _definitions;
        private readonly List<PostedEvent> _pending = new List<PostedEvent>();
        private readonly Dictionary<Type, List<Action<ISimEvent>>> _typeSubscribers = new Dictionary<Type, List<Action<ISimEvent>>>();
        private readonly Dictionary<string, List<Action<PostedEvent>>> _idSubscribers = new Dictionary<string, List<Action<PostedEvent>>>();

        public EventBus(EventDefinitionRegistry definitions)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public void Post<T>(T typedEvent, long tick) where T : struct, ISimEvent
        {
            string definitionId = typedEvent.DefinitionId;
            EnsureRegistered(definitionId);
            _pending.Add(new PostedEvent(definitionId, tick, typedEvent, null));
        }

        public void Post(string definitionId, long tick, EventData data = null)
        {
            EnsureRegistered(definitionId);
            _pending.Add(new PostedEvent(definitionId, tick, null, data));
        }

        /// <summary>Receives only typed events of exactly type T.</summary>
        public EventSubscriptionToken Subscribe<T>(Action<T> handler) where T : struct, ISimEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type type = typeof(T);
            if (!_typeSubscribers.TryGetValue(type, out List<Action<ISimEvent>> list))
            {
                list = new List<Action<ISimEvent>>();
                _typeSubscribers[type] = list;
            }

            void Wrapped(ISimEvent e) => handler((T)e);
            list.Add(Wrapped);

            return new EventSubscriptionToken(() => list.Remove(Wrapped));
        }

        /// <summary>Receives every event (typed or data) posted under definitionId.</summary>
        public EventSubscriptionToken Subscribe(string definitionId, Action<PostedEvent> handler)
        {
            if (string.IsNullOrEmpty(definitionId))
            {
                throw new ArgumentException("definitionId must not be empty.", nameof(definitionId));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!_idSubscribers.TryGetValue(definitionId, out List<Action<PostedEvent>> list))
            {
                list = new List<Action<PostedEvent>>();
                _idSubscribers[definitionId] = list;
            }

            list.Add(handler);

            return new EventSubscriptionToken(() => list.Remove(handler));
        }

        public void DeliverPending()
        {
            int countToDeliver = _pending.Count;

            for (int i = 0; i < countToDeliver; i++)
            {
                Dispatch(_pending[i]);
            }

            _pending.RemoveRange(0, countToDeliver);
        }

        private void Dispatch(PostedEvent posted)
        {
            if (_idSubscribers.TryGetValue(posted.DefinitionId, out List<Action<PostedEvent>> idList))
            {
                Action<PostedEvent>[] snapshot = idList.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    Action<PostedEvent> handler = snapshot[i];
                    InvokeSafely(posted.DefinitionId, handler, () => handler(posted));
                }
            }

            if (posted.TypedEvent != null)
            {
                Type runtimeType = posted.TypedEvent.GetType();
                if (_typeSubscribers.TryGetValue(runtimeType, out List<Action<ISimEvent>> typeList))
                {
                    Action<ISimEvent>[] snapshot = typeList.ToArray();
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        Action<ISimEvent> handler = snapshot[i];
                        InvokeSafely(posted.DefinitionId, handler, () => handler(posted.TypedEvent));
                    }
                }
            }
        }

        private static void InvokeSafely(string definitionId, Delegate handler, Action invoke)
        {
            try
            {
                invoke();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Subscriber '{DescribeHandler(handler)}' threw while handling event '{definitionId}'.", ex);
            }
        }

        private static string DescribeHandler(Delegate handler)
        {
            string target = handler.Target != null ? handler.Target.GetType().Name : "<static>";
            return $"{target}.{handler.Method.Name}";
        }

        private void EnsureRegistered(string definitionId)
        {
            if (!_definitions.IsRegistered(definitionId))
            {
                throw new InvalidOperationException($"Cannot post event with unregistered definition ID '{definitionId}'.");
            }
        }
    }
}
