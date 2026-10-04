using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Domain.Access;

/// <summary>
/// Privacidad de menores (AC-01, Ley 29733): un tutor solo ve el viaje si uno de sus hijos
/// va en él, solo ve el estado de SUS hijos, y solo ve la ubicación mientras el viaje está en curso.
/// </summary>
public static class GuardianAccessPolicy
{
    public static bool CanViewTrip(Trip trip, IReadOnlyCollection<StudentId> guardianStudents) =>
        guardianStudents.Any(trip.HasPassenger);

    public static IReadOnlyList<TripPassenger> VisiblePassengers(Trip trip, IReadOnlyCollection<StudentId> guardianStudents) =>
        trip.Passengers.Where(p => guardianStudents.Contains(p.StudentId)).ToList();

    public static bool CanSeeLivePosition(Trip trip) => trip.IsActive;
}
