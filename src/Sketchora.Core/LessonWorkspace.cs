namespace Sketchora.Core;

/// <summary>Authored lesson data and isolated canvas histories; temporary ink never enters this model.</summary>
public sealed class LessonWorkspace
{
    private LessonDocument _document = new();
    private readonly Dictionary<Guid, SnapshotHistory<LessonPage>> _history = [];
    private readonly Stack<(LessonPage Page, int Index)> _deleted = new();
    private long _savedRevision;
    public long Revision { get; private set; }
    public bool IsDirty => Revision != _savedRevision;
    public string? FilePath { get; private set; }
    public event EventHandler? Changed;
    public LessonDocument Snapshot => _document.Copy();
    public LessonPage ActivePage => Active.Copy();
    public bool IsDesktop => _document.IsDesktop;
    public string BoardPageName => _document.Pages[ActivePageIndex].Name;
    public int PageCount => _document.Pages.Count;
    public int ActivePageIndex => _document.ActivePageIndex;
    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public bool CanRestorePage => _deleted.Count > 0 && PageCount < LessonStore.MaximumPages;
    private LessonPage Active => IsDesktop ? _document.Desktop : _document.Pages[ActivePageIndex];
    private SnapshotHistory<LessonPage> History
    {
        get
        {
            if (!_history.TryGetValue(Active.Id, out var history))
                _history[Active.Id] = history = new SnapshotHistory<LessonPage>(Active, page => page.Copy());
            return history;
        }
    }

    public void Load(LessonDocument document, string? path = null, bool recovered = false)
    {
        LessonStore.Validate(document);
        var copy = document.Copy();
        _document = copy;
        _history.Clear();
        _deleted.Clear();
        FilePath = path;
        Revision++;
        _savedRevision = recovered ? Revision - 1 : Revision;
        Notify();
    }

    public void New(double width, double height) => Load(new LessonDocument { CanvasWidth = width, CanvasHeight = height });
    public void MarkSaved(string path) { FilePath = path; _savedRevision = Revision; Notify(); }
    public void MarkSaved(string path, long revision)
    {
        FilePath = path;
        _savedRevision = revision;
        Notify();
    }

    public void SetContent(string ink, IEnumerable<TextLabel> labels) => Edit(Active with { Ink = ink, Texts = labels.ToList() });
    public void SetBackground(PageBackground background) { if (!IsDesktop && Active.Background != background) Edit(Active with { Background = background }); }
    public void RenamePage(string name)
    {
        if (IsDesktop) return;
        name = name.Trim();
        if (name.Length is < 1 or > 120) throw new ArgumentException("Use a board name between 1 and 120 characters.");
        if (name != Active.Name) Edit(Active with { Name = name });
    }
    public void UpsertText(TextLabel label)
    {
        var labels = Active.Texts.Where(text => text.Id != label.Id).Append(label).ToList();
        Edit(Active with { Texts = labels });
    }
    public void DeleteText(Guid id) => Edit(Active with { Texts = Active.Texts.Where(text => text.Id != id).ToList() });
    public void Clear() { if (Active.Ink.Length > 0 || Active.Texts.Count > 0) Edit(Active with { Ink = "", Texts = [] }); }
    public void Undo() { if (CanUndo) { ReplaceActive(History.Undo()); Revision++; Notify(); } }
    public void Redo() { if (CanRedo) { ReplaceActive(History.Redo()); Revision++; Notify(); } }

    public void ShowDesktop(bool desktop) { _document = _document with { IsDesktop = desktop }; Notify(); }
    public void Navigate(int delta)
    {
        _document = _document with { IsDesktop = false, ActivePageIndex = Math.Clamp(ActivePageIndex + delta, 0, PageCount - 1) };
        Notify();
    }
    public void AddPage(bool duplicate = false)
    {
        if (PageCount >= LessonStore.MaximumPages) throw new InvalidOperationException("The lesson already has 100 board pages.");
        var page = duplicate ? _document.Pages[ActivePageIndex].Copy() with
            { Id = Guid.NewGuid(), Name = _document.Pages[ActivePageIndex].Name[..Math.Min(113, _document.Pages[ActivePageIndex].Name.Length)] + " (copy)" }
            : new LessonPage { Name = $"Board {PageCount + 1}" };
        // A duplicate receives new label identities as well as a new canvas identity.
        page = page with { Texts = page.Texts.Select(text => text with { Id = Guid.NewGuid() }).ToList() };
        _document.Pages.Insert(ActivePageIndex + 1, page);
        _document = _document with { ActivePageIndex = ActivePageIndex + 1, IsDesktop = false };
        Revision++;
        Notify();
    }
    public void DeletePage()
    {
        if (IsDesktop || PageCount <= 1) return;
        _deleted.Push((Active.Copy(), ActivePageIndex));
        _document.Pages.RemoveAt(ActivePageIndex);
        _document = _document with { ActivePageIndex = Math.Min(ActivePageIndex, PageCount - 1) };
        Revision++;
        Notify();
    }
    public void RestorePage()
    {
        if (!CanRestorePage) return;
        var (page, index) = _deleted.Pop();
        var insertion = Math.Min(index, PageCount);
        _document.Pages.Insert(insertion, page);
        _document = _document with { ActivePageIndex = insertion, IsDesktop = false };
        Revision++;
        Notify();
    }

    private void Edit(LessonPage next)
    {
        // Validate edits before altering the active canvas or its redo branch.
        // Other canvases were validated when loaded or edited; avoid decoding
        // every board's ink on each new stroke.
        LessonStore.Validate(new LessonDocument
        {
            Desktop = IsDesktop ? next.Copy() : new LessonPage { Name = "Desktop" },
            Pages = IsDesktop ? [new LessonPage()] : [next.Copy()]
        });
        History.Capture(next);
        ReplaceActive(next.Copy());
        Revision++;
        Notify();
    }
    private void ReplaceActive(LessonPage page)
    {
        if (IsDesktop) _document = _document with { Desktop = page };
        else _document.Pages[ActivePageIndex] = page;
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
