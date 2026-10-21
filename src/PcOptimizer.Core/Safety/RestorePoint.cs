namespace PcOptimizer.Core.Safety;

public enum RestorePointOutcome
{
    /// <summary>Creato un nuovo punto di ripristino.</summary>
    Created,
    /// <summary>
    /// Windows non ne crea più di uno ogni 24 ore: ne esiste già uno recente, che viene usato.
    /// L'utente viene informato della data.
    /// </summary>
    ReusedRecent,
    /// <summary>Protezione sistema disattivata sull'unità di Windows.</summary>
    Disabled,
    Failed,
}

public sealed record RestorePointResult(RestorePointOutcome Outcome, DateTime? PointTime, string Message)
{
    /// <summary>Si può procedere con le modifiche solo se esiste un punto di ripristino valido.</summary>
    public bool IsUsable => Outcome is RestorePointOutcome.Created or RestorePointOutcome.ReusedRecent;
}

public interface IRestorePointService
{
    /// <summary>Garantisce un punto di ripristino recente (crea o riusa quello delle ultime 24 ore).</summary>
    RestorePointResult EnsureRestorePoint(string description);
}
