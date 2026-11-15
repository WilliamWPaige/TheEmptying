using System;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>A quantity of a good, picked from a dropdown of GoodAsset instances: one line of a construction cost or a recipe's inputs or outputs.</summary>
    [Serializable]
    public sealed class GoodAmountAsset
    {
        [SerializeField] private GoodAsset _good;
        [SerializeField] private int _amount;

        public GoodAsset Good => _good;
        public int Amount => _amount;

        public GoodAmountAsset()
        {
        }

        public GoodAmountAsset(GoodAsset good, int amount)
        {
            _good = good;
            _amount = amount;
        }
    }
}
