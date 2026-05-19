using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
/// Shared Win2D canvas host for the expanded playback surface.
/// The owning surface supplies a <see cref="CanvasAnimatedControl"/>; this host renders
/// composable layers such as shader background, spectrum, and lyrics in deterministic order.
/// </summary>
public interface IExpandedCanvasHost
{
    /// <summary>Add a layer to the shared Win2D render pipeline.</summary>
    void AddLayer(IExpandedCanvasLayer layer);

    /// <summary>Remove a layer from the shared Win2D render pipeline.</summary>
    void RemoveLayer(IExpandedCanvasLayer layer);

    /// <summary>Notify all layers that the underlying canvas resources are being created.</summary>
    void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args);

    /// <summary>Update time-based state for all layers before drawing.</summary>
    void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args);

    /// <summary>Draw all layers onto the shared canvas in layer order.</summary>
    void Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args);

    /// <summary>Route pointer input from the shared canvas to interested layers.</summary>
    bool TryHandlePointer(PointerRoutedEventArgs args);
}

/// <summary>
/// A single composable layer in the expanded Win2D canvas pipeline.
/// </summary>
public interface IExpandedCanvasLayer
{
    /// <summary>Human-readable identifier for diagnostics.</summary>
    string LayerName { get; }

    /// <summary>Lower values draw earlier; background layers should use lower indexes than lyric layers.</summary>
    int Order { get; }

    /// <summary>Create or refresh Win2D resources owned by this layer.</summary>
    void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args);

    /// <summary>Update time-based state before drawing this frame.</summary>
    void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args);

    /// <summary>Draw this layer onto the shared drawing session.</summary>
    void Draw(ICanvasAnimatedControl sender, CanvasDrawingSession session, CanvasTimingInformation timing);

    /// <summary>Optional pointer handling hook. Return true when the event is consumed.</summary>
    bool TryHandlePointer(PointerRoutedEventArgs args) => false;
}
