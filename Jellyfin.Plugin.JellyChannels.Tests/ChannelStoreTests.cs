using System;
using System.IO;
using Jellyfin.Plugin.JellyChannels.Models;
using Jellyfin.Plugin.JellyChannels.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.JellyChannels.Tests;

public sealed class ChannelStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jellychannels-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private ChannelStore NewStore() => new(_folder, NullLogger<ChannelStore>.Instance);

    [Theory]
    [InlineData("Canal Retro", "canal-retro")]
    [InlineData("  Años 80 / Ñoño!! ", "anos-80-nono")]
    [InlineData("***", "canal")]
    public void Slugify_ProducesSafeIds(string name, string expected)
        => Assert.Equal(expected, ChannelStore.Slugify(name));

    [Fact]
    public void Create_FillsIdSeedStartAndNumber()
    {
        var store = NewStore();

        var first = store.Create(new Channel { Name = "Canal Retro" });
        var second = store.Create(new Channel { Name = "Canal Retro" });

        Assert.Equal("canal-retro", first.Id);
        Assert.Equal("canal-retro-2", second.Id);
        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.NotNull(first.Seed);
        Assert.NotNull(first.StartDate);
    }

    [Fact]
    public void Update_KeepsSeedAndStartDateWhenMissing()
    {
        var store = NewStore();
        var created = store.Create(new Channel { Name = "Canal", Seed = 99, StartDate = TestItems.Start });

        var updated = store.Update(created.Id, new Channel { Name = "Canal renombrado", Number = 5, Mode = PlaybackMode.SeededShuffle })!;

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(99, updated.Seed);
        Assert.Equal(TestItems.Start, updated.StartDate);
        Assert.Equal("Canal renombrado", updated.Name);
        Assert.Null(store.Update("no-existe", new Channel()));
    }

    [Fact]
    public void Channels_ArePersistedWithDocumentedJsonFormat()
    {
        var store = NewStore();
        var created = store.Create(new Channel
        {
            Name = "Canal",
            Mode = PlaybackMode.SeededShuffle,
            Sources = { new ChannelSource { Type = SourceType.Tag, Value = "80s" } }
        });

        var json = File.ReadAllText(Path.Combine(_folder, "channels.json"));
        Assert.Contains("\"mode\": \"seeded-shuffle\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"tag\"", json, StringComparison.Ordinal);

        var reloaded = NewStore().Get(created.Id)!;
        Assert.Equal(PlaybackMode.SeededShuffle, reloaded.Mode);
        Assert.Equal("80s", Assert.Single(reloaded.Sources).Value);
    }

    [Fact]
    public void ReturnedChannels_AreCopies()
    {
        var store = NewStore();
        var created = store.Create(new Channel { Name = "Canal" });

        store.Get(created.Id)!.Name = "Cambiado sin guardar";

        Assert.Equal("Canal", store.Get(created.Id)!.Name);
    }

    [Fact]
    public void Delete_RemovesChannelAndRaisesEvent()
    {
        var store = NewStore();
        var created = store.Create(new Channel { Name = "Canal" });
        string? changed = null;
        store.Changed += (_, e) => changed = e.ChannelId;

        Assert.True(store.Delete(created.Id));
        Assert.False(store.Delete(created.Id));
        Assert.Equal(created.Id, changed);
        Assert.Empty(NewStore().GetAll());
    }

    [Fact]
    public void CorruptFile_IsBackedUpInsteadOfLost()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "channels.json"), "{ esto no es json");

        Assert.Empty(NewStore().GetAll());
        Assert.Equal("{ esto no es json", File.ReadAllText(Path.Combine(_folder, "channels.json.corrupt")));
    }
}
