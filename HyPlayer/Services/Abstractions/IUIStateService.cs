#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HyPlayer.Classes;
using Kawazu;
using Windows.System.Display;

using HyPlayer.Services.Abstractions;
using System.Timers;
namespace HyPlayer.Services.Abstractions;

/// <summary>
/// UI 状态服务，管理全局 UI 引用与状态
/// </summary>
public interface IUIStateService
{
    // UI page references (set during page initialization)
    object? PageExpandedPlayer { get; set; }
    object? PageCompactPlayer { get; set; }
    object? PageMain { get; set; }
    object? BarPlayBar { get; set; }
    object? PageBase { get; set; }
    object? GlobalTip { get; set; }
    object? XboxGameBarWidget { get; set; }
    KawazuConverter? KawazuConv { get; set; }

    // UI state
    bool IsExpanded { get; set; }
    bool IsInBackground { get; set; }
    bool ShowLyricSound { get; set; }
    bool ShowLyricTrans { get; set; }
    bool NavigatingBack { get; set; }
    int PlaybarSecondCounter { get; set; }
    bool PlaybarIsVisible { get; set; }
    DisplayRequest DisplayRequest { get; }
    BrushManagement BrushManagement { get; }
    List<string> ErrorMessageList { get; }
    ObservableCollection<string> Logs { get; }
    Queue<KeyValuePair<string, string?>> TeachingTipList { get; }
    Timer GlobalSecondTimer { get; }

    // Methods
    void ChangePlaybarVisibility();
    void RollTeachingTip(bool passiveRoll = true);
    void InvokeEnterForeground();
    void InvokePlaybarVisibilityChanged(bool isActivated);
}
