using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Monolith.Resources.Shared;
using Monolith.Resources.Errors;

namespace Monolith.Shared.Interfaces.Rest.ProblemDetails;

public class ProblemDetailsFactory
{
    private readonly Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory _aspNetCoreProblemDetailsFactory;
    private readonly IStringLocalizer<CommonMessages> _commonLocalizer;
    private readonly IStringLocalizer<ErrorMessages> _errorLocalizer;

    public ProblemDetailsFactory(
        IStringLocalizer<ErrorMessages> errorLocalizer,
        IStringLocalizer<CommonMessages> commonLocalizer,
        Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory aspNetCoreProblemDetailsFactory)
    {
        _errorLocalizer = errorLocalizer;
        _commonLocalizer = commonLocalizer;
        _aspNetCoreProblemDetailsFactory = aspNetCoreProblemDetailsFactory;
    }

    public IActionResult CreateProblemDetails(
        ControllerBase controller,
        int statusCode,
        Enum? errorEnum,
        string detailMessage)
    {
        var title = errorEnum is null
            ? LocalizeCommon("GenericError", "An error occurred.")
            : LocalizeError(errorEnum, detailMessage);
        var detail = errorEnum is null
            ? detailMessage
            : LocalizeError(errorEnum, detailMessage);

        var problemDetails = _aspNetCoreProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode,
            title,
            detail: detail,
            instance: controller.HttpContext.Request.Path
        );

        problemDetails.Title = title;
        problemDetails.Detail = detail;
        problemDetails.Instance = controller.HttpContext.Request.Path;

        return controller.StatusCode(statusCode, problemDetails);
    }

    public IActionResult CreateProblemDetails(
        ControllerBase controller,
        int statusCode,
        string titleKey,
        string detailKey,
        params object[] detailArgs)
    {
        var problemDetails = _aspNetCoreProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode,
            LocalizeCommon(titleKey, titleKey),
            detail: LocalizeError(detailKey, detailKey, detailArgs),
            instance: controller.HttpContext.Request.Path
        );
        return controller.StatusCode(statusCode, problemDetails);
    }

    private string LocalizeError(Enum errorEnum, string fallback)
    {
        var typeSpecificKey = $"{errorEnum.GetType().Name}.{errorEnum}";
        var typeSpecific = _errorLocalizer[typeSpecificKey];
        if (!typeSpecific.ResourceNotFound)
            return typeSpecific.Value;

        return LocalizeError(errorEnum.ToString(), fallback);
    }

    private string LocalizeError(string key, string fallback, params object[] args)
    {
        var localized = args.Length == 0 ? _errorLocalizer[key] : _errorLocalizer[key, args];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }

    private string LocalizeCommon(string key, string fallback)
    {
        var localized = _commonLocalizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }
}