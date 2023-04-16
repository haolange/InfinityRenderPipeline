using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;

namespace InfinityTech.Rendering.RDG.Editor
{
    public class RenderGraphView : GraphView
    {
        public RenderGraphView()
        {
            styleSheets.Add(Resources.Load<StyleSheet>("StyleSheet/RenderGraphView"));
            SetupZoom(0.25f, 2.0f);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new FreehandSelector());

            GridBackground gridBackground = new GridBackground();
            Insert(0, gridBackground);
            gridBackground.StretchToParentSize();

            AddElement(GenerateNode("RenderDepth", new string[] { }, new string[] { "DepthBuffer" }, new Rect(0, 0, 100, 150)));
            AddElement(GenerateNode("RenderGBuffer", new string[] { "DepthBuffer" }, new string[] { "GBufferA", "GBufferB" }, new Rect(400, 0, 100, 150)));
            AddElement(GenerateNode("RenderMotion", new string[] { "DepthBuffer" }, new string[] { "MotionBuffer" }, new Rect(800, 0, 100, 150)));
            AddElement(GenerateNode("RenderForward", new string[] { "DepthBuffer" }, new string[] { "LightingBuffer" }, new Rect(1200, 0, 100, 150)));
            AddElement(GenerateNode("RenderSky", new string[] { "ColorBuffer", "DepthBuffer" }, new string[] { "ColorBuffer" }, new Rect(1600, 0, 100, 150)));
            AddElement(GenerateNode("ComputeAntiAliasing", new string[] { "DepthBuffer", "MotionBuffer", "HistoryBuffer" }, new string[] { "AliasingBuffer" }, new Rect(2000, 0, 100, 150)));
            AddElement(GenerateNode("RenderGizmos", new string[] { "ColorBuffer", "DepthBuffer"}, new string[] { "ColorBuffer" }, new Rect(2400, 0, 100, 150)));
            AddElement(GenerateNode("RenderPresent", new string[] { "ColorBuffer" }, new string[] { }, new Rect(2800, 0, 100, 150)));
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> CompatiblePorts = new List<Port>();
            var StartPortView = startPort;

            ports.ForEach((port) =>
            {
                var portView = port;
                if (StartPortView != portView && StartPortView.node != portView.node)
                    CompatiblePorts.Add(port);
            });

            return CompatiblePorts;
        }

        Port GeneratePort(RenderPassNode node, in Direction portDirection, in Port.Capacity capacity = Port.Capacity.Multi)
        {
            return node.InstantiatePort(Orientation.Horizontal, portDirection, capacity, typeof(float));
        }

        RenderPassNode GenerateNode(string nodeName, string[] inputPinName, string[] outputPinName, in Rect drawRect)
        {
            RenderPassNode node = new RenderPassNode
            {
                title = nodeName,
            };

            if (inputPinName.Length != 0)
            {
                for (uint i = 0; i < inputPinName.Length; ++i)
                {
                    Port inputPin = GeneratePort(node, Direction.Input);
                    inputPin.portName = inputPinName[i];
                    inputPin.portColor = new Color(0.1f, 1, 0.25f);
                    node.inputContainer.Add(inputPin);
                }
            }

            if (outputPinName.Length != 0)
            {
                for (uint j = 0; j < outputPinName.Length; ++j)
                {
                    Port outPin = GeneratePort(node, Direction.Output);
                    outPin.portName = outputPinName[j];
                    outPin.portColor = new Color(1, 0.4f, 0);
                    node.outputContainer.Add(outPin);
                }
            }

            node.RefreshPorts();
            node.RefreshExpandedState();
            node.SetPosition(drawRect);
            node.styleSheets.Add(Resources.Load<StyleSheet>("StyleSheet/RenderPassNode"));
            return node;
        }
    }
}
