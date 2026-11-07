using TheEmptying.Core.Events;

namespace TheEmptying.Core.Saves
{
    /// <summary>Serializes one typed event struct for saving a pending event.</summary>
    public interface ITypedEventSerializer
    {
        void Write(SaveWriter writer, ISimEvent typedEvent);

        ISimEvent Read(SaveReader reader);
    }
}
