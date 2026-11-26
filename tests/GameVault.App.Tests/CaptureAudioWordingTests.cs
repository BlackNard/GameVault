using System.Windows;
using System.Windows.Controls;
using System.Runtime.ExceptionServices;
using GameVault.Core;

namespace GameVault.App.Tests;

public sealed class CaptureAudioWordingTests
{
    private static readonly VideoHealth Healthy = new(true, false, TimeSpan.Zero, 0, TimeSpan.Zero);

    [Fact]
    public void Recording_line_states_the_audio_mode()
    {
        Assert.Equal("Recording · 00:01:05 · game audio on", CaptureStatusText.Recording(TimeSpan.FromSeconds(65), Healthy, true));
        var silent = CaptureStatusText.Recording(TimeSpan.FromSeconds(65), Healthy, false);
        Assert.Equal("Recording · 00:01:05 · no audio (turn on game audio in Settings)", silent);
    }

    [Fact]
    public void Stall_text_only_mentions_audio_continuing_when_audio_is_recorded()
    {
        var stalled = new VideoHealth(true, true, TimeSpan.FromSeconds(9.6), 1, TimeSpan.FromSeconds(9.6));
        Assert.Contains("video stalled 9 s (audio continues)", CaptureStatusText.Recording(TimeSpan.Zero, stalled, true));
        var withoutAudio = CaptureStatusText.Recording(TimeSpan.Zero, stalled, false);
        Assert.Contains("video stalled 9 s", withoutAudio);
        Assert.DoesNotContain("audio continues", withoutAudio);
        Assert.Contains("video paused 2×", CaptureStatusText.Recording(TimeSpan.Zero, new VideoHealth(true, false, TimeSpan.Zero, 2, TimeSpan.FromSeconds(8)), true));
    }

    [Fact]
    public void Idle_line_shows_the_audio_state_and_long_recordings_keep_counting_hours()
    {
        Assert.EndsWith("· game audio on", CaptureStatusText.Idle(true));
        Assert.EndsWith("· no audio", CaptureStatusText.Idle(false));
        Assert.StartsWith("Recording · 26:00:00", CaptureStatusText.Recording(TimeSpan.FromHours(26), Healthy, true));
    }

    [Fact]
    public void Settings_checkbox_says_game_audio_and_explains_that_unchecked_is_silent()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try
        {
            var view = new CaptureSettingsView();
            IEnumerable<T> All<T>(DependencyObject d) where T : DependencyObject
            {
                if (d is T hit) yield return hit;
                foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) foreach (var x in All<T>(child)) yield return x;
                if (d is ContentControl { Content: DependencyObject content }) foreach (var x in All<T>(content)) yield return x;
            }
            var box = All<CheckBox>(view).Distinct().Single();
            Assert.Equal("Record game audio (Windows 11)", box.Content);
            var help = System.Windows.Automation.AutomationProperties.GetHelpText(box);
            Assert.Contains("Off means the video is silent", help);
            Assert.Contains("never the microphone", help);
            Assert.DoesNotContain("Record only this game's audio", help);
            // Only short texts are shown; the explanations are on hover.
            Assert.All(All<TextBlock>(view).Distinct(), t => Assert.True(t.Text.Length <= 110, t.Text));
        } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

