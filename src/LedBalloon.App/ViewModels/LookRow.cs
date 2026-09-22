using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// A named appearance for one run, as something to pick from a list.
/// <para>
/// The reach is the whole point and also the thing people are surprised by, so this carries how
/// many scenes wear it and says so wherever it is being changed.
/// </para>
/// </summary>
public sealed partial class LookRow : ObservableObject
{
    private readonly LedBalloonProject _project;

    public LookRow(LedBalloonProject project, Look look)
    {
        _project = project;
        Look = look;
    }

    public Look Look { get; }

    public string Name
    {
        get => Look.Name;
        set
        {
            if (string.Equals(Look.Name, value, StringComparison.Ordinal))
            {
                return;
            }

            Look.Name = _project.UniqueLookName(value, Look.Id);
            OnPropertyChanged();
        }
    }

    /// <summary>How many scenes wear this, which is how far changing it reaches.</summary>
    public int SceneCount => _project.ScenesWearing(Look.Id);

    public string Reach => SceneCount switch
    {
        0 => "No scene uses this yet, so changing it changes nothing on its own.",
        1 => "Used in 1 scene. Changing it changes that scene too.",
        _ => $"Used in {SceneCount} scenes. Changing it changes all of them.",
    };

    /// <summary>Called when a scene changed, since the reach is counted rather than stored.</summary>
    public void RefreshReach()
    {
        OnPropertyChanged(nameof(SceneCount));
        OnPropertyChanged(nameof(Reach));
    }

    public override string ToString() => Name;
}
