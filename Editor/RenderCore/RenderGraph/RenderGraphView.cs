using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEditor.VFX.UI;

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

        AddElement(GenerateNode("OpaqueDepth", new string[] {}, new string[] {"DepthBuffer"}, new Rect(0, 0, 100, 150)));
        AddElement(GenerateNode("OpaqueGBuffer", new string[] {"DepthBuffer"}, new string[] {"GBufferA", "GBufferB"}, new Rect(200, 0, 100, 150)));
        AddElement(GenerateNode("SkyAtmosphere", new string[] {"InColorBuffer"}, new string[] { "OutColorBuffer" }, new Rect(450, 0, 100, 150)));
        AddElement(GenerateNode("PresentBackBuffer", new string[] { "SwapBuffer" }, new string[] { }, new Rect(740, 0, 100, 150)));
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

    Port GeneratePort(RenderPassNode node, Direction portDirection, Port.Capacity capacity = Port.Capacity.Single)
    {
        return node.InstantiatePort(Orientation.Horizontal, portDirection, capacity, typeof(float));
    }

    RenderPassNode GenerateNode(string NodeName, string[] InPinName, string[] OutPinName, Rect DrawRect)
    {
        RenderPassNode node = new RenderPassNode
        {
            title = NodeName,
        };

        if(InPinName.Length != 0) {
            for(uint i = 0; i <= InPinName.Length - 1; i++)
            {
                Port inputPin = GeneratePort(node, Direction.Input);
                inputPin.portName = InPinName[i];
                inputPin.portColor = new Color(0.1f, 1, 0.25f);
                node.inputContainer.Add(inputPin);  
            }
        }

        if(OutPinName.Length != 0) {
            for(uint j = 0; j <= OutPinName.Length - 1; j++)
            {
                Port outPin = GeneratePort(node, Direction.Output);
                outPin.portName = OutPinName[j];
                outPin.portColor = new Color(1, 0.4f, 0);
                node.outputContainer.Add(outPin);    
            }
        }

        node.RefreshPorts();
        node.RefreshExpandedState();
        node.SetPosition(DrawRect);
        node.styleSheets.Add(Resources.Load<StyleSheet>("StyleSheet/RenderPassNode"));
        return node;
    }
}
