using System.Collections.Generic;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Provides one or more default input sources for an adapter-created layer.
    /// </summary>
    /// <remarks>
    /// This interface extends <see cref="IAdapterBindingDefaultLayer"/> without changing
    /// the legacy single-source <see cref="IAdapterBindingDefaultLayer.DefaultLayerInputSourceId"/>
    /// contract.
    /// </remarks>
    public interface IAdapterBindingDefaultLayerInputs
    {
        /// <summary>
        /// Returns default input source ids and weights for the specified layer name.
        /// </summary>
        IEnumerable<(string id, float weight)> GetDefaultLayerInputSources(string layerName);
    }
}
