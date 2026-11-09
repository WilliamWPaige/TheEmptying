using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>One request to show the add-node menu: where, and (if dragged from a port) what it must connect to once a block is chosen.</summary>
    public sealed class AddNodeMenuRequest
    {
        public Vector2 ScreenPosition { get; }
        public Vector2 CanvasPosition { get; }
        public NodeViewModel DragSourceNode { get; }
        public PortViewModel DragSourcePort { get; }

        public AddNodeMenuRequest(Vector2 screenPosition, Vector2 canvasPosition, NodeViewModel dragSourceNode, PortViewModel dragSourcePort)
        {
            ScreenPosition = screenPosition;
            CanvasPosition = canvasPosition;
            DragSourceNode = dragSourceNode;
            DragSourcePort = dragSourcePort;
        }
    }
}
