using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;

namespace RutaSegura.TripTracking.Domain.Trips;

/// <summary>
/// Estado de un alumno dentro de un viaje. Máquina de estados:
/// Pending → Boarded → Alighted, Pending → Absent, Absent → Boarded (llegó igual).
/// </summary>
public sealed class TripPassenger
{
    public StudentId StudentId { get; private set; }
    public StopId StopId { get; private set; }
    public PassengerStatus Status { get; private set; } = PassengerStatus.Pending;
    public DateTimeOffset? BoardedAt { get; private set; }
    public GeoPosition? BoardedPosition { get; private set; }
    public DateTimeOffset? AlightedAt { get; private set; }
    public GeoPosition? AlightedPosition { get; private set; }
    public AbsenceSource? AbsenceSource { get; private set; }
    public string? AbsenceNote { get; private set; }

    private TripPassenger() { } // EF Core

    internal TripPassenger(TripPassengerDefinition definition)
    {
        StudentId = definition.StudentId;
        StopId = definition.StopId;
    }

    internal void Board(DateTimeOffset at, GeoPosition? position)
    {
        if (Status is not (PassengerStatus.Pending or PassengerStatus.Absent))
            throw InvalidTransition(PassengerStatus.Boarded);

        Status = PassengerStatus.Boarded;
        BoardedAt = at;
        BoardedPosition = position;
        AbsenceSource = null;
        AbsenceNote = null;
    }

    internal void Alight(DateTimeOffset at, GeoPosition? position)
    {
        if (Status != PassengerStatus.Boarded)
            throw InvalidTransition(PassengerStatus.Alighted);

        Status = PassengerStatus.Alighted;
        AlightedAt = at;
        AlightedPosition = position;
    }

    internal void MarkAbsent(AbsenceSource source, string? note)
    {
        if (Status != PassengerStatus.Pending)
            throw InvalidTransition(PassengerStatus.Absent);

        Status = PassengerStatus.Absent;
        AbsenceSource = source;
        AbsenceNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    /// <summary>Botón "Deshacer": revierte un abordaje o un "No vino" marcado por el conductor.</summary>
    internal PassengerStatus Undo()
    {
        var previous = Status;
        switch (Status)
        {
            case PassengerStatus.Boarded:
                BoardedAt = null;
                BoardedPosition = null;
                break;
            case PassengerStatus.Absent when AbsenceSource == Trips.AbsenceSource.Driver:
                AbsenceSource = null;
                AbsenceNote = null;
                break;
            default:
                throw new DomainException(
                    TripErrors.PassengerInvalidTransition,
                    $"No se puede deshacer el estado {Status} del alumno {StudentId}.");
        }

        Status = PassengerStatus.Pending;
        return previous;
    }

    private DomainException InvalidTransition(PassengerStatus target) =>
        new(TripErrors.PassengerInvalidTransition,
            $"El alumno {StudentId} no puede pasar de {Status} a {target}.");
}
