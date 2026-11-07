using System;

namespace TheEmptying.Core.Events
{
    public sealed class EventSubscriptionToken
    {
        private Action _unsubscribe;

        internal EventSubscriptionToken(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Unsubscribe()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}
