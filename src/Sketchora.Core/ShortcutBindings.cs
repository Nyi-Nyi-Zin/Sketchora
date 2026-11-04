namespace Sketchora.Core;

public interface IShortcutRegistrar
{
    bool Register(int id, uint key);
    void Unregister(int id);
}

/// <summary>Reserve new keys first; a conflict must not destroy the current recovery shortcut.</summary>
public sealed class ShortcutBindings(IShortcutRegistrar registrar) : IDisposable
{
    private Dictionary<uint, (int Id, Action Action)> _bindings = [];
    private int _nextId = 1;
    public bool TryConfigure(IReadOnlyDictionary<uint, Action> requested)
    {
        var additions = new Dictionary<uint, int>();
        foreach (var key in requested.Keys.Where(key => !_bindings.ContainsKey(key)))
        {
            var id = _nextId++;
            if (!registrar.Register(id, key))
            {
                foreach (var added in additions.Values) registrar.Unregister(added);
                return false;
            }
            additions[key] = id;
        }
        foreach (var (key, binding) in _bindings.Where(pair => !requested.ContainsKey(pair.Key)))
            registrar.Unregister(binding.Id);
        _bindings = requested.ToDictionary(pair => pair.Key, pair =>
            (_bindings.TryGetValue(pair.Key, out var old) ? old.Id : additions[pair.Key], pair.Value));
        return true;
    }
    public bool Invoke(int id)
    {
        foreach (var binding in _bindings.Values)
            if (binding.Id == id) { binding.Action(); return true; }
        return false;
    }
    public void Dispose()
    {
        foreach (var binding in _bindings.Values) registrar.Unregister(binding.Id);
        _bindings.Clear();
    }
}
