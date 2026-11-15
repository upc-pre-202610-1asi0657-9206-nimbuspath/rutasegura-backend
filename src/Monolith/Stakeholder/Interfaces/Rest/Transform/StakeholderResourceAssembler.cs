using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Entities;
using Monolith.Stakeholder.Interfaces.Rest.Resources;

namespace Monolith.Stakeholder.Interfaces.Rest.Transform;

public static class StakeholderResourceAssembler
{
    public static string PhotoUrl(Guid studentId) => $"/api/v1/stakeholders/students/{studentId}/photo";

    public static StudentResource ToResource(StudentDetails details)
    {
        var student = details.Student;
        return new StudentResource(
            student.Id.Identifier,
            student.FullName.FirstName,
            student.FullName.LastName,
            student.FullName.ToString(),
            student.BirthDate,
            student.SchoolGrade,
            student.SchoolName,
            ToResource(student),
            student.Photo is null
                ? null
                : new StudentPhotoResource($"{PhotoUrl(student.Id.Identifier)}?v={student.Photo.Version}",
                    student.Photo.ContentType, student.Photo.SizeBytes, student.Photo.UploadedAt,
                    student.Photo.Version),
            student.Status,
            details.Enrollments.Select(ToResource).ToList(),
            student.CreatedAt);
    }

    public static EnrollmentResource ToResource(EnrollmentDetails details)
    {
        var enrollment = details.Enrollment;
        return new EnrollmentResource(
            enrollment.Id.Identifier,
            enrollment.RouteId,
            details.Route?.Name ?? enrollment.RouteName,
            enrollment.OrganizationId,
            details.OrganizationName,
            enrollment.Direction,
            enrollment.Status.Value,
            enrollment.EnrolledAt,
            enrollment.EndedAt,
            enrollment.EndReason,
            details.Route?.Status,
            details.Route?.DepartureTime,
            details.Route?.EstimatedArrivalTime,
            details.Route?.OperatingDays,
            ToResource(details.Stop));
    }

    public static StudentStopResource? ToResource(StudentStop? stop) =>
        stop is null
            ? null
            : new StudentStopResource(stop.StopId, stop.StopName, stop.StopAddress, stop.Sequence, stop.Latitude,
                stop.Longitude);

    public static PickupLocationResource ToResource(Student student) =>
        new(student.PickupLocation.Address, student.PickupLocation.Reference, student.PickupLocation.Latitude,
            student.PickupLocation.Longitude);

    public static RouteOfferResource ToResource(RouteOffer offer) =>
        new(offer.Route.RouteId, offer.Route.Name, offer.OrganizationName, offer.Route.Direction, offer.Route.Status,
            offer.Route.DepartureTime, offer.Route.EstimatedArrivalTime, offer.Route.TimeZoneId,
            offer.Route.OperatingDays, offer.Route.AvailableSeats, offer.Route.AcceptsEnrollments);

    public static RouteStudentResource ToResource(RouteStudentDetails details, bool includeGuardianEmail)
    {
        var student = details.Student;
        var enrollment = details.Enrollment;
        return new RouteStudentResource(
            student.Id.Identifier,
            enrollment.Id.Identifier,
            enrollment.RouteId,
            enrollment.RouteName,
            student.FullName.FirstName,
            student.FullName.LastName,
            student.FullName.ToString(),
            student.SchoolGrade,
            student.SchoolName,
            student.Photo is null ? null : $"{PhotoUrl(student.Id.Identifier)}?v={student.Photo.Version}",
            ToResource(student),
            ToResource(details.Stop),
            new GuardianContactResource(student.GuardianUserId, details.Guardian?.FullName,
                details.GuardianProfile?.PhoneNumber.Value, includeGuardianEmail ? details.Guardian?.Email : null),
            enrollment.Status.Value,
            enrollment.EnrolledAt,
            enrollment.EndedAt,
            enrollment.EndReason);
    }

    public static EnrollmentResource ToResource(RouteEnrollment enrollment, StudentStop? stop = null) =>
        ToResource(new EnrollmentDetails(enrollment, null, stop, null));

    public static GuardianProfileResource ToResource(GuardianProfileDetails details) =>
        new(details.UserId, details.Account?.FullName, details.Account?.Email, details.Profile?.PhoneNumber.Value,
            details.Profile?.DocumentNumber, details.Profile is not null);
}
