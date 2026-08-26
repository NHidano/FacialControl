using Hidano.FacialControl.Domain.Adapters;
using VContainer.Unity;

namespace Hidano.FacialControl.Adapters.DependencyInjection
{
    internal sealed class PhonemeOverlayInputSourceRegistrationHost : IInitializable
    {
        private readonly AdapterBuildContext _buildContext;

        public PhonemeOverlayInputSourceRegistrationHost(AdapterBuildContext buildContext)
        {
            _buildContext = buildContext;
        }

        public void Initialize()
        {
            PhonemeOverlayInputSourceRegistration.RegisterDeclaredSlots(in _buildContext);
        }
    }
}
