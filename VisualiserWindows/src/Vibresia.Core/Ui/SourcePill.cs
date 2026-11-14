using Vibresia.Core.Audio;

namespace Vibresia.Core.Ui;

/// <summary>What the source pill's dot says (upstream SourceState): live is magenta, demo amber, idle grey, failed red.</summary>
public enum SourceState { Idle, Live, Demo, Error }

/// <summary>
/// The words and state of the header's source picker (upstream HUDView.swift
/// SourcePicker and AppState.swift AudioSource), in one testable place.
/// </summary>
public static class SourcePill
{
    /// <summary>The picker's rows, in upstream's order.</summary>
    public static IReadOnlyList<AudioChoice> Choices { get; } = [AudioChoice.Loopback, AudioChoice.Microphone, AudioChoice.Demo];

    /// <param name="running">The source the pipeline is running, or null for none.</param>
    public static SourceState State(AudioStatus status, AudioChoice? running) => status switch
    {
        AudioStatus.Failed => SourceState.Error,
        AudioStatus.None => SourceState.Idle,
        _ when running is null => SourceState.Idle,
        _ when running == AudioChoice.Demo => SourceState.Demo,
        _ => SourceState.Live,
    };

    /// <summary>What the pill says: what is flowing, else why not (upstream: activeSource.title, else sourceLabel uppercased).</summary>
    public static string Title(SourceState state, AudioChoice chosen) => state switch
    {
        SourceState.Live or SourceState.Demo => FlowingTitle(chosen),
        SourceState.Error => chosen switch
        {
            AudioChoice.Microphone => "MIC FAILED",
            AudioChoice.Demo => "DEMO FAILED",
            _ => "SYSTEM AUDIO FAILED",
        },
        _ => "IDLE",
    };

    public static string FlowingTitle(AudioChoice c) => c switch
    {
        AudioChoice.Microphone => "MICROPHONE",
        AudioChoice.Demo => "DEMO TONE",
        _ => "SYSTEM AUDIO",
    };

    public static string MenuTitle(AudioChoice c) => c switch
    {
        AudioChoice.Microphone => "Use Microphone",
        AudioChoice.Demo => "Use Demo Tone",
        _ => "Share System Audio",
    };

    public static string Subtitle(AudioChoice c) => c switch
    {
        AudioChoice.Microphone => "Plays whatever the mic hears",
        AudioChoice.Demo => "No permissions needed",
        _ => "Spotify, YouTube, anything playing",
    };
}
