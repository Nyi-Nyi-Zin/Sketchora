using Sketchora.Core;
using Xunit;

namespace Sketchora.Core.Tests;

public sealed class VersionTwoBehaviorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void TemporaryInkExpiresAtCompletionLifetimeBoundary(int seconds)
    {
        var clock = TimeSpan.Zero;
        var layer = new TemporaryInk<string>(() => clock);
        layer.Add("stroke", TimeSpan.FromSeconds(seconds));
        clock = TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1);
        Assert.InRange(Assert.Single(layer.Tick()).Opacity, 0.001, 1);
        Assert.Equal(1, layer.Count);
        clock = TimeSpan.FromSeconds(seconds);
        Assert.Equal(0, Assert.Single(layer.Tick()).Opacity);
        Assert.Equal(0, layer.Count);
        Assert.Empty(layer.Tick());
    }

    [Fact]
    public void ClearingTemporaryInkRemovesEveryPendingExpiry()
    {
        var layer = new TemporaryInk<string>(() => TimeSpan.Zero);
        layer.Add("a", TimeSpan.FromSeconds(1));
        layer.Add("b", TimeSpan.FromSeconds(3));
        layer.Clear();
        Assert.Empty(layer.Tick());
    }

    [Theory]
    [InlineData(UnsavedChoice.Cancel, true, false, 0)]
    [InlineData(UnsavedChoice.Discard, true, true, 0)]
    [InlineData(UnsavedChoice.Save, false, false, 1)]
    [InlineData(UnsavedChoice.Save, true, true, 1)]
    public async Task UnsavedChangesGuardHonorsChoiceAndSaveOutcome(UnsavedChoice choice, bool saveResult, bool mayReplace, int expectedSaves)
    {
        var saves = 0;
        var result = await UnsavedChangesGuard.CanReplaceAsync(true, () => choice, () => { saves++; return Task.FromResult(saveResult); });
        Assert.Equal(mayReplace, result);
        Assert.Equal(expectedSaves, saves);
    }

    [Fact]
    public async Task FailedSaveNeverAuthorizesReplacement()
    {
        await Assert.ThrowsAsync<IOException>(() => UnsavedChangesGuard.CanReplaceAsync(true,
            () => UnsavedChoice.Save, () => throw new IOException("Disk failed")));
    }

    [Fact]
    public async Task CleanLessonDoesNotPromptOrWrite()
    {
        Assert.True(await UnsavedChangesGuard.CanReplaceAsync(false,
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException()));
    }

    [Fact]
    public void InvalidPreferenceValuesKeepAUsableShortcutPairAndFadeLifetime()
    {
        var settings = new AppPreferences { DrawShortcut = 'D', HideShortcut = 'D', FadeSeconds = 99 }.Normalize();
        Assert.Equal('D', settings.DrawShortcut);
        Assert.Equal('H', settings.HideShortcut);
        Assert.Equal(3, settings.FadeSeconds);
    }

    private sealed class FakeRegistrar : IShortcutRegistrar
    {
        public Dictionary<int, uint> Keys { get; } = [];
        public HashSet<uint> Conflicts { get; } = [];
        public bool Register(int id, uint key)
        {
            if (Conflicts.Contains(key) || Keys.ContainsValue(key)) return false;
            Keys.Add(id, key); return true;
        }
        public void Unregister(int id) => Keys.Remove(id);
    }

    [Fact]
    public void ShortcutConflictRollsBackReservationsAndKeepsOldActions()
    {
        var registrar = new FakeRegistrar();
        using var manager = new ShortcutBindings(registrar);
        var invoked = 0;
        Assert.True(manager.TryConfigure(new Dictionary<uint, Action> { ['D'] = () => invoked++, ['H'] = () => invoked += 10 }));
        var old = registrar.Keys.ToDictionary();
        registrar.Conflicts.Add('K');
        Assert.False(manager.TryConfigure(new Dictionary<uint, Action> { ['J'] = () => { }, ['K'] = () => { } }));
        Assert.Equal(old.OrderBy(pair => pair.Key), registrar.Keys.OrderBy(pair => pair.Key));
        Assert.True(manager.Invoke(old.Single(pair => pair.Value == 'H').Key));
        Assert.Equal(10, invoked);
    }

    [Fact]
    public void SwappingShortcutKeysReusesReservationsAndChangesTheirActions()
    {
        var registrar = new FakeRegistrar();
        using var manager = new ShortcutBindings(registrar);
        var invoked = "";
        Assert.True(manager.TryConfigure(new Dictionary<uint, Action> { ['D'] = () => { }, ['H'] = () => { } }));
        Assert.True(manager.TryConfigure(new Dictionary<uint, Action> { ['H'] = () => invoked = "draw", ['D'] = () => invoked = "hide" }));
        manager.Invoke(registrar.Keys.Single(pair => pair.Value == 'H').Key);
        Assert.Equal("draw", invoked);
        Assert.Equal(2, registrar.Keys.Count);
    }
}
