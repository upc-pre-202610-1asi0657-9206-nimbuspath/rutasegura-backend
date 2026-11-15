using Monolith.Shared.Infrastructure.Pipeline.Middleware.Components;


namespace Monolith.Shared.Infrastructure.Pipeline.Middleware.Extensions;


/// <summary>
///     Middleware extensions
/// </summary>
public static class MiddlewareExtensions
{
    /**
     * <summary>
     *     Use the global exception handler middleware
     * </summary>
     * <param name="builder">The application builder</param>
     * <returns>The application builder</returns>
     */
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    }
}