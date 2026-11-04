namespace Sketchora.Core;

public enum UnsavedChoice { Save, Discard, Cancel }

public static class UnsavedChangesGuard
{
    public static async Task<bool> CanReplaceAsync(bool dirty, Func<UnsavedChoice> choose, Func<Task<bool>> save)
    {
        if (!dirty) return true;
        return choose() switch
        {
            UnsavedChoice.Discard => true,
            UnsavedChoice.Save => await save(),
            _ => false
        };
    }
}
