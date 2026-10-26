using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Application.CommandServices;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Commands;
using Monolith.Stakeholder.Domain.Model.Queries;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Interfaces.Rest.Resources;
using Monolith.Stakeholder.Interfaces.Rest.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Stakeholder.Interfaces.Rest;

/// <summary>
///     The authenticated parent registers its children (name, school data, photo, pickup address) and enrolls them in
///     the routes of a transport company with the code the company shared.
/// </summary>
[Route("api/v1/stakeholders/me/students")]
[Authorize(Policy = IamPolicies.Guardian)]
[SwaggerTag("Stakeholder · Children of the authenticated parent and their enrollments.")]
public class MyStudentsController(
    IStakeholderCommandService commandService,
    IStakeholderQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : StakeholderControllerBase(problemDetailsFactory)
{
    [HttpGet]
    [SwaggerOperation(Summary = "My children", OperationId = "GetMyStudents")]
    public async Task<IActionResult> GetMyStudents(CancellationToken cancellationToken)
    {
        var students = await queryService.Handle(new GetMyStudentsQuery(CurrentUserId), cancellationToken);
        return Ok(students.Select(StakeholderResourceAssembler.ToResource));
    }

    [HttpGet("{studentId:guid}")]
    [SwaggerOperation(Summary = "One of my children", OperationId = "GetMyStudent")]
    public async Task<IActionResult> GetMyStudent(Guid studentId, CancellationToken cancellationToken)
    {
        var student = await queryService.Handle(new GetMyStudentQuery(CurrentUserId, studentId), cancellationToken);
        return student is null
            ? StakeholderActionResultAssembler.Problem(this, ProblemDetails, StakeholderError.StudentNotFound,
                "Student was not found.")
            : Ok(StakeholderResourceAssembler.ToResource(student));
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Register a child", OperationId = "RegisterStudent")]
    public async Task<IActionResult> RegisterStudent(RegisterStudentResource resource,
        CancellationToken cancellationToken)
    {
        var pickup = resource.PickupLocation;
        if (pickup is null)
            return StakeholderActionResultAssembler.Problem(this, ProblemDetails,
                StakeholderError.InvalidStakeholderData, "The pickup location is required.");

        var result = await commandService.Handle(new RegisterStudentCommand(CurrentUserId, resource.FirstName,
            resource.LastName, resource.BirthDate, resource.SchoolGrade, resource.SchoolName, pickup.Address,
            pickup.Reference, pickup.Latitude, pickup.Longitude), cancellationToken);
        return await RespondAsync(result.IsSuccess ? result.Value!.Id.Identifier : Guid.Empty, result, created: true,
            cancellationToken);
    }

    [HttpPut("{studentId:guid}")]
    [SwaggerOperation(Summary = "Update name, birth date and school data", OperationId = "UpdateStudent")]
    public async Task<IActionResult> UpdateStudent(Guid studentId, UpdateStudentResource resource,
        CancellationToken cancellationToken) =>
        await RespondAsync(studentId, await commandService.Handle(new UpdateStudentCommand(CurrentUserId, studentId,
            resource.FirstName, resource.LastName, resource.BirthDate, resource.SchoolGrade, resource.SchoolName),
            cancellationToken), false, cancellationToken);

    [HttpPut("{studentId:guid}/pickup-location")]
    [SwaggerOperation(Summary = "Change the pickup address (the stops of its routes are updated)",
        OperationId = "ChangeStudentPickupLocation")]
    public async Task<IActionResult> ChangePickupLocation(Guid studentId, PickupLocationResource resource,
        CancellationToken cancellationToken) =>
        await RespondAsync(studentId, await commandService.Handle(new ChangeStudentPickupLocationCommand(CurrentUserId,
            studentId, resource.Address, resource.Reference, resource.Latitude, resource.Longitude),
            cancellationToken), false, cancellationToken);

    /// <summary>multipart/form-data with the image in the field <c>photo</c> (JPEG, PNG or WebP, max 1 MB).</summary>
    [HttpPut("{studentId:guid}/photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [SwaggerOperation(Summary = "Upload or replace the photo", OperationId = "UploadStudentPhoto")]
    public async Task<IActionResult> UploadPhoto(Guid studentId, IFormFile? photo, CancellationToken cancellationToken)
    {
        if (photo is null || photo.Length == 0)
            return StakeholderActionResultAssembler.Problem(this, ProblemDetails, StakeholderError.StudentPhotoInvalid,
                "Send the image in the multipart field 'photo'.");
        if (photo.Length > StudentPhoto.MaxSizeBytes)
            return StakeholderActionResultAssembler.Problem(this, ProblemDetails, StakeholderError.StudentPhotoInvalid,
                $"The photo must be at most {StudentPhoto.MaxSizeBytes / 1024} KB.");

        using var buffer = new MemoryStream();
        await photo.CopyToAsync(buffer, cancellationToken);
        return await RespondAsync(studentId, await commandService.Handle(
            new UploadStudentPhotoCommand(CurrentUserId, studentId, buffer.ToArray()), cancellationToken), false,
            cancellationToken);
    }

    [HttpDelete("{studentId:guid}/photo")]
    [SwaggerOperation(Summary = "Delete the photo", OperationId = "DeleteStudentPhoto")]
    public async Task<IActionResult> DeletePhoto(Guid studentId, CancellationToken cancellationToken) =>
        await RespondAsync(studentId,
            await commandService.Handle(new DeleteStudentPhotoCommand(CurrentUserId, studentId), cancellationToken),
            false, cancellationToken);

    [HttpDelete("{studentId:guid}")]
    [SwaggerOperation(Summary = "Remove the child (withdraws it from its routes)", OperationId = "RemoveStudent")]
    public async Task<IActionResult> RemoveStudent(Guid studentId, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new RemoveStudentCommand(CurrentUserId, studentId), cancellationToken);
        return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails, _ => NoContent());
    }

    // ── Enrollments ──────────────────────────────────────────────────────────

    [HttpPost("{studentId:guid}/enrollments")]
    [SwaggerOperation(Summary = "Enroll the child in a route with the code of the transport company",
        OperationId = "EnrollStudent")]
    public async Task<IActionResult> Enroll(Guid studentId, EnrollStudentResource resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(
            new EnrollStudentCommand(CurrentUserId, studentId, resource.EnrollmentCode), cancellationToken);
        if (!result.IsSuccess)
            return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails, _ => Ok());

        // Read back with the route and stop as Route Planning has them now.
        var details = await queryService.Handle(new GetMyStudentQuery(CurrentUserId, studentId), cancellationToken);
        var enrollment = details?.Enrollments.FirstOrDefault(e => e.Enrollment.Id == result.Value!.Enrollment.Id);
        var resourceBody = enrollment is null
            ? StakeholderResourceAssembler.ToResource(result.Value!.Enrollment)
            : StakeholderResourceAssembler.ToResource(enrollment);
        return CreatedAtAction(nameof(GetMyStudent), new { studentId }, resourceBody);
    }

    [HttpDelete("{studentId:guid}/enrollments/{enrollmentId:guid}")]
    [SwaggerOperation(Summary = "Withdraw the child from the route", OperationId = "WithdrawEnrollment")]
    public async Task<IActionResult> Withdraw(Guid studentId, Guid enrollmentId, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new WithdrawEnrollmentCommand(CurrentUserId, studentId, enrollmentId),
            cancellationToken);
        return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            enrollment => Ok(StakeholderResourceAssembler.ToResource(enrollment)));
    }

    private async Task<IActionResult> RespondAsync<T>(Guid studentId,
        Monolith.Shared.Application.Model.Result<T> result, bool created, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
            return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails, _ => Ok());
        var details = await queryService.Handle(new GetMyStudentQuery(CurrentUserId, studentId), cancellationToken);
        if (details is null)
            return StakeholderActionResultAssembler.Problem(this, ProblemDetails, StakeholderError.StudentNotFound,
                "Student was not found.");
        var body = StakeholderResourceAssembler.ToResource(details);
        return created ? CreatedAtAction(nameof(GetMyStudent), new { studentId }, body) : Ok(body);
    }
}
