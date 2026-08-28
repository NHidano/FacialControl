using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Adapters;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Playable
{
    public sealed class FacialControllerGazeChannelTests
    {
        [Test]
        public void ConfigureAdapterBindingsWithGazeChannels_TypedConsumerReceivesChannelIds()
        {
            var gameObject = new GameObject("FacialControllerGazeChannelTests");
            try
            {
                var controller = gameObject.AddComponent<FacialController>();
                var ids = new List<string> { "gaze", "camera" };
                var idsField = typeof(FacialController).GetField(
                    "_gazeChannelIds", BindingFlags.Instance | BindingFlags.NonPublic);
                ((List<string>)idsField.GetValue(controller)).AddRange(ids);

                var binding = new FakeGazeConsumer { Slug = "fake" };
                var bindings = new List<AdapterBindingBase> { binding };
                var method = typeof(FacialController).GetMethod(
                    "ConfigureAdapterBindingsWithGazeChannels",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method.Invoke(controller, new object[] { bindings });

                CollectionAssert.AreEqual(ids, binding.ReceivedIds);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [System.Serializable]
        private sealed class FakeGazeConsumer : AdapterBindingBase, IGazeChannelConsumer
        {
            public List<string> ReceivedIds { get; private set; }

            public void ConfigureGazeChannels(IReadOnlyList<string> channelIds)
            {
                ReceivedIds = new List<string>(channelIds);
            }
        }
    }
}
