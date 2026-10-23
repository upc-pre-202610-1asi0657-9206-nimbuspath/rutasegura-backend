using System.Net.Mime;
using System.Text.Json;
using Monolith.Resources.Errors;
using Monolith.Resources.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;


// For OperationCanceledException


namespace Monolith.Shared.Infrastructure.Pipeline.Middleware.Components;


/// <summary>
///     Global Exception Handling Middleware
/// </summary>
/// <remarks>
///     This middleware catches all unhandled exceptions and returns a Problem Details response.
/// </remarks>
public class GlobalExceptionHandlerMiddleware(
   RequestDelegate next,
   ILogger<GlobalExceptionHandlerMiddleware> logger,
   IStringLocalizer<ErrorMessages> errorLocalizer, // Inject IStringLocalizer for error messages
   IStringLocalizer<CommonMessages> // Corrected to Commons
       commonLocalizer) // Inject IStringLocalizer for common messages like "Internal Server Error"
{
   private readonly IStringLocalizer<CommonMessages> _commonLocalizer = commonLocalizer; // Corrected to Commons
   private readonly IStringLocalizer<ErrorMessages> _errorLocalizer = errorLocalizer;


   /**
    * <summary>
    *     Invoke the middleware
    * </summary>
    * <param name="context">The http context</param>
    * <returns>A task</returns>
    */
   public async Task InvokeAsync(HttpContext context)
   {
       try
       {
           await next(context);
       }
       catch (OperationCanceledException ex)
       {
           logger.LogWarning(ex, "Request was cancelled: {Message}", ex.Message);
           await HandleOperationCanceledExceptionAsync(context, ex);
       }
       catch (Exception ex)
       {
           logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
           await HandleGenericExceptionAsync(context, ex);
       }
   }


   /**
    * <summary>
    *     Handle the OperationCanceledException
    * </summary>
    * <param name="context">The http context</param>
    * <param name="exception">The exception</param>
    * <returns>A task</returns>
    */
   private async Task HandleOperationCanceledExceptionAsync(HttpContext context, OperationCanceledException exception)
   {
       context.Response.ContentType = MediaTypeNames.Application.Json;
       context.Response.StatusCode = StatusCodes.Status409Conflict; // Or 204 No Content if appropriate


       var problemDetails = new ProblemDetails
       {
           Status = StatusCodes.Status409Conflict,
           Title = _errorLocalizer["OperationCancelled"], // Localized title
           Detail = _errorLocalizer["OperationCancelled"], // Localized detail
           Instance = context.Request.Path
       };


       var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
       var result = JsonSerializer.Serialize(problemDetails, jsonOptions);


       await context.Response.WriteAsync(result);
   }


   /**
    * <summary>
    *     Handle a generic exception
    * </summary>
    * <param name="context">The http context</param>
    * <param name="exception">The exception</param>
    * <returns>A task</returns>
    */
   private async Task HandleGenericExceptionAsync(HttpContext context, Exception exception)
   {
       context.Response.ContentType = MediaTypeNames.Application.Json;
       context.Response.StatusCode = StatusCodes.Status500InternalServerError;


       var problemDetails = new ProblemDetails
       {
           Status = StatusCodes.Status500InternalServerError,
           Title = _commonLocalizer["InternalServerError"], // Localized title
           Detail = _errorLocalizer["GenericError"], // Localized generic error message
           Instance = context.Request.Path
       };


       var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
       var result = JsonSerializer.Serialize(problemDetails, jsonOptions);


       await context.Response.WriteAsync(result);
   }
}