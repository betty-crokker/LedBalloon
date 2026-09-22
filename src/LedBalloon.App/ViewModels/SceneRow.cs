using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One entry in the list of named ways for the house to look.
/// <para>
/// Either a scene LedBalloon owns, or a preset somebody made in the WLED app that has not been
/// adopted yet. They share a list because to the person reading it they are the same kind of thing
/// — a name you can give the house — and which one it is governs only what can be done to it.
/// </para>
/// <para>
/// A published scene is both at once: it lives in the project and a copy of it sits on each
/// controller. That is one row, not two, because they are two renderings of one fact.
/// </para>
/// </summary>
public sealed partial class SceneRow : ObservableObject
{
    private readonly LedBalloonProject _project;

    private SceneRow(LedBalloonProject project, Scene? scene, HousePreset? preset)
    {
        _project = project;
        Scene = scene;
        Preset = preset;
    }

    /// <summary>The scene this row is, or null when it is an un-adopted preset.</summary>
    public Scene? Scene { get; }

    /// <summary>What the controllers hold under this name, or null when it has never been saved.</summary>
    public HousePreset? Preset { get; }

    /// <summary>True when LedBalloon owns this and can change it.</summary>
    public bool IsOwn => Scene is not null;

    /// <summary>
    /// True for a preset made in the WLED app. It is shown but not edited: its segments are LED
    /// ranges rather than runs, and some of them may match nothing in the layout at all.
    /// </summary>
    public bool NeedsAdopting => Scene is null;

    /// <summary>True once this scene has a copy on at least one controller.</summary>
    public bool IsPublished => Preset is not null;

    public string Name
    {
        get => Scene?.Name ?? Preset?.Name ?? "Scene";
        set
        {
            if (Scene is not { } scene || string.Equals(scene.Name, value, StringComparison.Ordinal))
            {
                return;
            }

            // Two scenes of one name would fight over a single preset slot on every controller,
            // each overwriting the other on alternate saves.
            scene.Name = _project.UniqueSceneName(value, scene.Id);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Origin));
        }
    }

    /// <summary>Where this came from and what can be done with it, in one line.</summary>
    public string Origin
    {
        get
        {
            if (Scene is not { } scene)
            {
                return "Made in the WLED app. It can be recalled but not changed, because its " +
                       "segments are LED numbers rather than runs.";
            }

            if (scene.PublishedAs is null)
            {
                return "Not on the controllers yet. Save to put it there, and the timers can reach it.";
            }

            return string.Equals(scene.PublishedAs.Trim(), scene.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                ? "On the controllers, so the timers and the wall button can reach it."
                : $"Saved as '{scene.PublishedAs}' on the controllers. Saving renames it there too, " +
                  "taking any timer pointing at it along.";
        }
    }

    public static SceneRow For(LedBalloonProject project, Scene scene, HousePreset? published) =>
        new(project, scene, published);

    public static SceneRow For(LedBalloonProject project, HousePreset preset) =>
        new(project, null, preset);

    public override string ToString() => Name;
}
