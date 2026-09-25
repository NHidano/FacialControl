using Hidano.FacialControl.Editor.Windows.Routing.Graph;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using NUnit.Framework;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Graph
{
    [TestFixture]
    public class AdapterNodeViewTests
    {
        [Test]
        public void Construct_RendersDisplayNameBadgeAndMultipleOutputPorts()
        {
            var data = new AdapterNodeData(
                "input-system",
                "input-system",
                supportsAutoWire: false,
                new[]
                {
                    new AdapterOutputData("input-system", "input-system"),
                    new AdapterOutputData("input-system:analog-expression", "analog-expression"),
                    new AdapterOutputData("input-system:overlay:blink", "blink"),
                });

            var view = new AdapterNodeView(data);

            Assert.That(view.title, Is.EqualTo("input-system"));
            Assert.That(view.TypeBadge, Is.Not.Null);
            Assert.That(view.TypeBadge.text, Is.EqualTo(AdapterNodeView.TypeBadgeText));
            Assert.That(AdapterNodeView.TypeBadgeText, Is.EqualTo("Adapter"));

            Port analogPort = view.GetOutputPort("input-system:analog-expression");
            Assert.That(analogPort, Is.Not.Null);
            Assert.That(analogPort.direction, Is.EqualTo(Direction.Output));
            Assert.That(analogPort.portName, Is.EqualTo("analog-expression"));
            Assert.That(analogPort.userData, Is.EqualTo("input-system:analog-expression"));

            Assert.That(view.GetOutputPort("input-system:overlay:blink"), Is.Not.Null);
            Assert.That(view.GetOutputPort("does-not-exist"), Is.Null);
        }

        [Test]
        public void Construct_AddsAllPortAsOutputWithoutCanonicalId()
        {
            var data = new AdapterNodeData(
                "ulipsync",
                "ulipsync",
                supportsAutoWire: true,
                new[] { new AdapterOutputData("lipsync-overlay:a", "a") });

            var view = new AdapterNodeView(data);

            Assert.That(view.AllPort, Is.Not.Null);
            Assert.That(view.AllPort.direction, Is.EqualTo(Direction.Output));
            Assert.That(view.AllPort.portName, Is.EqualTo(AdapterNodeView.AllPortName));
            Assert.That(view.AllPort.userData, Is.Null);
            // All ポートは個別 canonical id を持たないため GetOutputPort では引けない。
            Assert.That(view.GetOutputPort("lipsync-overlay:a"), Is.Not.SameAs(view.AllPort));
        }

        [Test]
        public void SetAdapterNodes_RebuildsGraphWithAdapterNodeViews()
        {
            var graphView = new RoutingGraphView();
            var adapterNodes = new[]
            {
                new AdapterNodeData(
                    "ulipsync",
                    "ulipsync",
                    supportsAutoWire: true,
                    new[] { new AdapterOutputData("lipsync-overlay:a", "a") }),
                new AdapterNodeData(
                    "input-system",
                    "input-system",
                    supportsAutoWire: false,
                    new[]
                    {
                        new AdapterOutputData("input-system", "input-system"),
                        new AdapterOutputData("input-system:analog-expression", "analog-expression"),
                    }),
            };

            graphView.SetAdapterNodes(adapterNodes);

            Assert.That(graphView.AdapterNodeViews, Has.Count.EqualTo(2));
            Assert.That(graphView.Query<AdapterNodeView>().ToList(), Has.Count.EqualTo(2));
            Assert.That(graphView.AdapterNodeViews[0].GetOutputPort("lipsync-overlay:a"), Is.Not.Null);
            Assert.That(graphView.AdapterNodeViews[0].AllPort, Is.Not.Null);
            Assert.That(
                graphView.AdapterNodeViews[1].GetOutputPort("input-system:analog-expression").portName,
                Is.EqualTo("analog-expression"));
        }
    }
}
