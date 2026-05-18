using PKHeX.Core;
using System.Threading;

namespace PKHeXMCP;

public class SaveContext
{
    private SaveFile? _save;
    private readonly ReaderWriterLockSlim _lock = new();

    public bool HasSave => _save != null;

    public T? WithRead<T>(Func<SaveFile, T> action)
    {
        _lock.EnterReadLock();
        try { return _save is null ? default : action(_save); }
        finally { _lock.ExitReadLock(); }
    }

    public T WithWrite<T>(Func<SaveFile?, T> action)
    {
        _lock.EnterWriteLock();
        try { return action(_save); }
        finally { _lock.ExitWriteLock(); }
    }

    public void SetSave(SaveFile save)
    {
        _lock.EnterWriteLock();
        try { _save = save; }
        finally { _lock.ExitWriteLock(); }
    }
}
