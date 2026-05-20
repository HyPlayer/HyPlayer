using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Input;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
/// Concrete shared Win2D canvas host for the expanded playback surface.
/// Maintains an ordered collection of <see cref="IExpandedCanvasLayer"/> instances
/// and forwards the draw pipeline (CreateResources → Update → Draw) and pointer events
/// to each layer in layer order.
///
/// This is a standalone host; it does NOT own the <see cref="CanvasAnimatedControl"/>.
/// The owning ExpandedPlayer (or future surface) attaches the control's events to this host.
/// </summary>
public class ExpandedCanvasHost : IExpandedCanvasHost
{
    private readonly List<IExpandedCanvasLayer> _layers = new();

    /// <inheritdoc />
    public void AddLayer(IExpandedCanvasLayer layer)
    {
        if (layer is null) throw new ArgumentNullException(nameof(layer));
        if (_layers.Contains(layer)) return;

        _layers.Add(layer);
        _layers.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    /// <inheritdoc />
    public void RemoveLayer(IExpandedCanvasLayer layer)
    {
        _layers.Remove(layer);
    }

    /// <inheritdoc />
    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        foreach (var layer in _layers)
            layer.CreateResources(sender, args);
    }

    /// <inheritdoc />
    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        foreach (var layer in _layers)
            layer.Update(sender, args);
    }

    /// <inheritdoc />
    public void Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        using var session = args.DrawingSession;
        foreach (var layer in _layers)
            layer.Draw(sender, session, args.Timing);
    }

    /// <inheritdoc />
    public bool TryHandlePointer(PointerRoutedEventArgs args)
    {
        if (args is null) return false;

        // Offer pointer event to layers in reverse draw order (topmost first)
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            if (_layers[i].TryHandlePointer(args))
                return true;
        }

        return false;
    }
}
