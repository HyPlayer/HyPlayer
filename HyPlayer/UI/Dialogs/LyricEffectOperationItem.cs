using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.LyricEffects.Models;
using WinRT;

namespace HyPlayer.UI.Dialogs;

[GeneratedBindableCustomProperty]
public partial class LyricEffectOperationItem(
    LyricRenderOperationDefinition definition,
    LyricRenderOperationDescriptor? descriptor) : ObservableObject
{
    private string _status = string.Empty;

    public LyricRenderOperationDefinition Definition { get; } = definition;

    public string InstanceId => Definition.InstanceId;

    public string TypeId => Definition.TypeId;

    public LyricRenderOperationDescriptor? Descriptor { get; private set; } = descriptor;

    public string Description => Descriptor?.Description ?? TypeId;

    public string CategoryLabel => Descriptor?.Category switch
    {
        LyricRenderOperationCategory.Draw => "绘制节点",
        LyricRenderOperationCategory.Effect => "特效节点",
        _ => "未知节点"
    };

    public bool IsRequired => Descriptor?.IsRequired == true;

    public bool CanToggle => !IsRequired;

    public bool CanDuplicate => !IsRequired;

    public bool CanDelete => !IsRequired;

    public string DisplayName
    {
        get => Definition.DisplayName;
        set
        {
            if (Definition.DisplayName == value) return;
            Definition.DisplayName = value;
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => Definition.IsEnabled;
        set
        {
            if (IsRequired && !value) return;
            if (Definition.IsEnabled == value) return;
            Definition.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public void UpdateDescriptor(LyricRenderOperationDescriptor descriptor)
    {
        Descriptor = descriptor;
        OnPropertyChanged(nameof(TypeId));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CategoryLabel));
        OnPropertyChanged(nameof(IsRequired));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanDuplicate));
        OnPropertyChanged(nameof(CanDelete));
    }
}
