using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Shell.Navigation;

public sealed record SidebarEntryDefinition(string Key, string Name);

public sealed record SidebarSectionDefinition(
    string Key,
    string Name,
    IReadOnlyList<SidebarEntryDefinition> Items);

public sealed partial class SidebarEntry : ObservableObject
{
    public SidebarEntry(string key, string name, bool isVisible)
    {
        Key = key;
        Name = name;
        IsVisible = isVisible;
    }

    public string Key { get; }
    public string Name { get; }

    [ObservableProperty] public partial bool IsVisible { get; set; }

    public override string ToString()
    {
        return Name;
    }
}

public sealed partial class SidebarSection : ObservableObject
{
    public SidebarSection(string key, string name, bool isVisible)
    {
        Key = key;
        Name = name;
        IsVisible = isVisible;
    }

    public string Key { get; }
    public string Name { get; }
    public ObservableCollection<SidebarEntry> Items { get; } = [];

    [ObservableProperty] public partial bool IsVisible { get; set; }

    public override string ToString()
    {
        return Name;
    }
}

public sealed class SidebarLayout
{
    private readonly List<SavedSection> _saved;
    private readonly HyPlayer.Domain.Settings.UISettings _settings;
    private IReadOnlyList<SidebarSectionDefinition> _definitions = [];
    private bool _suspended;

    public SidebarLayout(HyPlayer.Domain.Settings.UISettings settings)
    {
        _settings = settings;
        _saved = Parse(settings.SidebarLayout);
        Sections.CollectionChanged += Collection_Changed;
    }

    public ObservableCollection<SidebarSection> Sections { get; } = [];

    public event EventHandler? Changed;

    public void Update(IReadOnlyList<SidebarSectionDefinition> definitions)
    {
        _definitions = definitions;
        Rebuild();
    }

    public void ResetToDefault()
    {
        _saved.Clear();
        _settings.SidebarLayout = string.Empty;
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        var arranged = Arrange();
        if (Matches(arranged)) return;

        _suspended = true;
        try
        {
            foreach (var section in Sections)
                Unsubscribe(section);
            Sections.Clear();

            foreach (var (definition, isVisible, items) in arranged)
            {
                var section = new SidebarSection(definition.Key, definition.Name, isVisible);
                foreach (var (item, isItemVisible) in items)
                {
                    var entry = new SidebarEntry(item.Key, item.Name, isItemVisible);
                    entry.PropertyChanged += Item_PropertyChanged;
                    section.Items.Add(entry);
                }

                section.PropertyChanged += Item_PropertyChanged;
                section.Items.CollectionChanged += Collection_Changed;
                Sections.Add(section);
            }
        }
        finally
        {
            _suspended = false;
        }
    }

    private List<(SidebarSectionDefinition Definition, bool IsVisible,
        List<(SidebarEntryDefinition Definition, bool IsVisible)> Items)> Arrange()
    {
        var result =
            new List<(SidebarSectionDefinition, bool, List<(SidebarEntryDefinition, bool)>)>();
        var sectionKeys = _saved.Select(section => section.Key)
            .Concat(_definitions.Select(definition => definition.Key))
            .Distinct();
        foreach (var sectionKey in sectionKeys)
        {
            var definition = _definitions.FirstOrDefault(candidate => candidate.Key == sectionKey);
            if (definition is null) continue;

            var saved = _saved.FirstOrDefault(section => section.Key == sectionKey);
            var items = new List<(SidebarEntryDefinition, bool)>();
            var itemKeys = (saved?.Items.Select(item => item.Key) ?? [])
                .Concat(definition.Items.Select(item => item.Key))
                .Distinct();
            foreach (var itemKey in itemKeys)
            {
                var item = definition.Items.FirstOrDefault(candidate => candidate.Key == itemKey);
                if (item is null) continue;

                items.Add((item, saved?.Items.FirstOrDefault(candidate => candidate.Key == itemKey)?.IsVisible ?? true));
            }

            result.Add((definition, saved?.IsVisible ?? true, items));
        }

        return result;
    }

    private bool Matches(
        List<(SidebarSectionDefinition Definition, bool IsVisible,
            List<(SidebarEntryDefinition Definition, bool IsVisible)> Items)> arranged)
    {
        if (arranged.Count != Sections.Count) return false;

        for (var i = 0; i < arranged.Count; i++)
        {
            var section = Sections[i];
            var (definition, isVisible, items) = arranged[i];
            if (section.Key != definition.Key || section.Name != definition.Name ||
                section.IsVisible != isVisible || section.Items.Count != items.Count)
                return false;

            for (var j = 0; j < items.Count; j++)
            {
                var entry = section.Items[j];
                if (entry.Key != items[j].Definition.Key || entry.Name != items[j].Definition.Name ||
                    entry.IsVisible != items[j].IsVisible)
                    return false;
            }
        }

        return true;
    }

    private void Unsubscribe(SidebarSection section)
    {
        section.PropertyChanged -= Item_PropertyChanged;
        section.Items.CollectionChanged -= Collection_Changed;
        foreach (var entry in section.Items)
            entry.PropertyChanged -= Item_PropertyChanged;
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnChanged();
    }

    private void Collection_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Move)
            OnChanged();
    }

    private void OnChanged()
    {
        if (_suspended) return;

        var sections = MergeOrder(
            _saved,
            Sections.Select(section =>
            {
                var saved = _saved.FirstOrDefault(candidate => candidate.Key == section.Key);
                var items = MergeOrder(
                    saved?.Items ?? [],
                    section.Items.Select(entry => new SavedItem(entry.Key, entry.IsVisible)).ToList(),
                    item => item.Key);
                return new SavedSection(section.Key, section.IsVisible, items);
            }).ToList(),
            section => section.Key);
        _saved.Clear();
        _saved.AddRange(sections);
        _settings.SidebarLayout = Serialize(_saved);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<T> MergeOrder<T>(List<T> previous, List<T> present, Func<T, string> keySelector)
    {
        var presentKeys = present.Select(keySelector).ToHashSet();
        var queue = new Queue<T>(present);
        var result = new List<T>(previous.Count + present.Count);
        foreach (var item in previous)
            if (!presentKeys.Contains(keySelector(item)))
                result.Add(item);
            else if (queue.Count > 0)
                result.Add(queue.Dequeue());

        while (queue.Count > 0)
            result.Add(queue.Dequeue());
        return result;
    }

    private static string Serialize(List<SavedSection> sections)
    {
        return string.Join(";", sections.Select(section =>
            $"{Uri.EscapeDataString(section.Key)}={(section.IsVisible ? 1 : 0)}:" +
            string.Join(",", section.Items.Select(item =>
                $"{Uri.EscapeDataString(item.Key)}={(item.IsVisible ? 1 : 0)}"))));
    }

    private static List<SavedSection> Parse(string saved)
    {
        var result = new List<SavedSection>();
        foreach (var part in saved.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':');
            var head = (colon < 0 ? part : part[..colon]).Split('=');
            if (head.Length != 2) continue;

            var items = new List<SavedItem>();
            if (colon >= 0)
                foreach (var item in part[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = item.Split('=');
                    if (pair.Length == 2)
                        items.Add(new SavedItem(Uri.UnescapeDataString(pair[0]), pair[1] != "0"));
                }

            result.Add(new SavedSection(Uri.UnescapeDataString(head[0]), head[1] != "0", items));
        }

        return result;
    }

    private sealed record SavedItem(string Key, bool IsVisible);

    private sealed record SavedSection(string Key, bool IsVisible, List<SavedItem> Items);
}
