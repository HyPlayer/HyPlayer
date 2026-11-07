using System;
using System.Collections.ObjectModel;
using Windows.UI.Core;

namespace HyPlayer.Shell.Input;

public interface IKeyboardShortcutService
{
    ObservableCollection<KeyboardShortcutEntry> Entries { get; }

    event EventHandler? MuteChanged;

    void Attach(CoreWindow window);

    void Detach(CoreWindow window);

    void BeginCapture(KeyboardShortcutEntry entry);

    void CancelCapture();

    void Clear(KeyboardShortcutEntry entry);

    void ResetAll();
}
